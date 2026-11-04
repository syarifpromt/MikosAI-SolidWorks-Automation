using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MikosAI.Core.Execution;
using MikosAI.Core.Tracing;

namespace MikosAI.Core.Plan
{
    /// <summary>Opsi menjalankan plan.</summary>
    public sealed class PlanRunOptions
    {
        /// <summary>
        /// Jalur Lapis 2: ubah nilai parameter dan jalankan ulang tanpa
        /// memanggil AI. Hanya boleh mengubah parameter yang sudah ada.
        /// </summary>
        public IReadOnlyDictionary<string, object?>? ParameterOverrides { get; set; }

        /// <summary>Untuk debugging: berhenti setelah step dengan id ini.</summary>
        public string? StopAtStep { get; set; }

        /// <summary>
        /// Mengizinkan run walaupun ada pertanyaan terbuka yang blocking.
        /// Hanya boleh true kalau manusia sudah melihat pertanyaannya dan
        /// memilih lanjut - jangan pernah di-default true.
        /// </summary>
        public bool BlockingQuestionsAcknowledged { get; set; }
    }

    /// <summary>
    /// Menjalankan DesignPlan step demi step.
    ///
    /// Urutan yang dijamin:
    ///
    ///   1. Validasi. Plan tidak valid DITOLAK tanpa menyentuh model sama sekali
    ///      - executor tidak dipanggil sekali pun.
    ///   2. Selesaikan parameter, termasuk override.
    ///   3. Jalankan step berurutan. Gagal berarti berhenti, tidak ada opsi
    ///      "continue" - melanjutkan di atas step yang gagal menghasilkan model
    ///      yang salah secara diam-diam.
    ///   4. Evaluasi kriteria penerimaan setelah semua step selesai.
    ///   5. Agregasi putusan akhir.
    ///
    /// Runner tidak tahu apa pun tentang SolidWorks. Itu disengaja: seluruh
    /// logikanya bisa diuji dengan FakeToolExecutor tanpa membuka CAD.
    /// </summary>
    public sealed class PlanRunner
    {
        private readonly PlanValidator _validator;
        private readonly IToolExecutor _executor;
        private readonly IAcceptanceEvaluator? _acceptanceEvaluator;
        private readonly TraceWriter? _trace;

        public PlanRunner(
            PlanValidator validator,
            IToolExecutor executor,
            IAcceptanceEvaluator? acceptanceEvaluator = null,
            TraceWriter? trace = null)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _acceptanceEvaluator = acceptanceEvaluator;
            _trace = trace;
        }

        public async Task<PlanRunResult> RunAsync(
            string planJson,
            PlanRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var node = JsonNode.Parse(planJson)
                       ?? throw new ArgumentException("Plan JSON kosong.", nameof(planJson));

            return await RunAsync(node.AsObject(), options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<PlanRunResult> RunAsync(
            JsonObject plan,
            PlanRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new PlanRunOptions();

            var runId = Guid.NewGuid().ToString("N").Substring(0, 12);
            var stopwatch = Stopwatch.StartNew();

            var result = new PlanRunResult { RunId = runId };

            _trace?.Write("run.started", new Dictionary<string, object?>
            {
                ["planTitle"] = plan["title"]?.GetValue<string>(),
            });

            // --- 1. Validasi ------------------------------------------------
            // Semua pengecekan ini terjadi SEBELUM model disentuh.

            var validation = _validator.Validate(plan);
            result.Validation = validation;

            var rejection = DetermineRejection(validation, options);
            if (rejection.reason != PlanRejectionReason.None)
            {
                result.Executed = false;
                result.RejectionReason = rejection.reason;
                result.RejectionMessage = rejection.message;
                result.Final = VerificationGate.Evaluate(
                    new List<VerificationCheck>
                    {
                        VerificationCheck.Blocked("plan_rejected", rejection.message),
                    });

                stopwatch.Stop();
                result.DurationMs = stopwatch.ElapsedMilliseconds;

                _trace?.Write("run.blocked", new Dictionary<string, object?>
                {
                    ["reason"] = rejection.reason.ToString(),
                    ["message"] = rejection.message,
                });

                return result;
            }

            // --- 2. Parameter -----------------------------------------------

            Dictionary<string, JsonNode?> parameters;
            try
            {
                parameters = ParameterResolver.BuildParameterTable(
                    plan["parameters"]?.AsObject(),
                    options.ParameterOverrides);
            }
            catch (ParameterResolutionException exception)
            {
                result.Executed = false;
                result.RejectionReason = PlanRejectionReason.ParameterResolution;
                result.RejectionMessage = exception.Message;
                result.Final = VerificationGate.Evaluate(new List<VerificationCheck>
                {
                    VerificationCheck.Blocked("parameter_resolution", exception.Message),
                });

                stopwatch.Stop();
                result.DurationMs = stopwatch.ElapsedMilliseconds;

                _trace?.Write("run.blocked", new Dictionary<string, object?>
                {
                    ["reason"] = "ParameterResolution",
                    ["message"] = exception.Message,
                });

                return result;
            }

            // --- 3. Step ----------------------------------------------------

            result.Executed = true;

            var outcomes = new List<StepOutcome>();
            var halted = false;

            foreach (var stepNode in plan["steps"]!.AsArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var step = stepNode!.AsObject();
                var stepId = step["id"]!.GetValue<string>();
                var toolName = step["tool"]!.GetValue<string>();

                if (halted)
                {
                    outcomes.Add(new StepOutcome
                    {
                        StepId = stepId,
                        Tool = toolName,
                        Skipped = true,
                        Verification = VerificationGate.Evaluate(
                            new List<VerificationCheck>
                            {
                                VerificationCheck.Blocked("step_skipped", "Dilewati karena step sebelumnya gagal.", required: false),
                            }),
                    });
                    continue;
                }

                var outcome = await RunStepAsync(step, stepId, toolName, parameters, cancellationToken)
                    .ConfigureAwait(false);

                outcomes.Add(outcome);

                // Tidak ada opsi "continue" di onFailure - satu-satunya pilihan
                // adalah berhenti. Lihat docs/skema-tool-dan-plan.md bagian 7.
                if (outcome.Verification.Verdict >= Verdict.Fail)
                {
                    halted = true;
                }

                if (!string.IsNullOrEmpty(options.StopAtStep)
                    && string.Equals(stepId, options.StopAtStep, StringComparison.Ordinal))
                {
                    halted = true;
                }
            }

            result.Steps = outcomes;

            // --- 4. Kriteria penerimaan -------------------------------------
            // Hanya bermakna kalau semua step benar-benar selesai. Kalau run
            // dihentikan di tengah, acceptance tidak diperiksa - memeriksanya
            // pada model setengah jadi hanya menghasilkan kebisingan.

            var acceptanceChecks = new List<VerificationCheck>();

            if (!halted)
            {
                acceptanceChecks.AddRange(
                    await EvaluateAcceptanceAsync(plan, cancellationToken).ConfigureAwait(false));
            }
            else if (plan["acceptance"] is JsonArray pending && pending.Count > 0)
            {
                // Sengaja OPSIONAL, jadi dibatasi maksimum WARN.
                //
                // Kalau run berhenti karena sebuah step gagal, putusan akhirnya
                // harus FAIL - itu diagnosis yang berguna. Menandai ini sebagai
                // BLOCKED required akan menimpa FAIL dan menyembunyikan
                // penyebab sebenarnya, padahal penyebabnya sangat jelas.
                //
                // BLOCKED berarti "tidak bisa ditentukan". Di sini kita justru
                // bisa menentukannya.
                acceptanceChecks.Add(new VerificationCheck
                {
                    Id = "acceptance_not_evaluated",
                    Verdict = Verdict.Blocked,
                    Required = false,
                    Message = "Kriteria penerimaan tidak diperiksa karena run berhenti sebelum semua step selesai.",
                });
            }

            result.AcceptanceChecks = acceptanceChecks;

            // --- 5. Agregasi ------------------------------------------------

            var allResults = outcomes
                .Where(o => !o.Skipped)
                .Select(o => o.Verification)
                .ToList();

            if (acceptanceChecks.Count > 0)
            {
                allResults.Add(VerificationGate.Evaluate(acceptanceChecks));
            }

            result.Final = allResults.Count > 0
                ? VerificationGate.Combine(allResults)
                : VerificationGate.Evaluate(null);

            result.DocumentPath = outcomes
                .Select(o => o.Result?.Evidence?.DocumentPath)
                .LastOrDefault(path => !string.IsNullOrEmpty(path));

            stopwatch.Stop();
            result.DurationMs = stopwatch.ElapsedMilliseconds;

            _trace?.Write(
                result.Final.Verdict == Verdict.Pass ? "run.completed" : "run.failed",
                new Dictionary<string, object?>
                {
                    ["verdict"] = VerdictParser.ToWire(result.Final.Verdict),
                    ["stepsRun"] = outcomes.Count(o => !o.Skipped),
                    ["stepsSkipped"] = outcomes.Count(o => o.Skipped),
                    ["durationMs"] = result.DurationMs,
                });

            return result;
        }

        private static (PlanRejectionReason reason, string message) DetermineRejection(
            PlanValidationResult validation,
            PlanRunOptions options)
        {
            if (validation.Errors.Count > 0)
            {
                var first = validation.Errors[0];
                var extra = validation.Errors.Count > 1
                    ? $" (dan {validation.Errors.Count - 1} masalah lain)"
                    : string.Empty;

                return (PlanRejectionReason.Invalid, $"{first.Path}: {first.Message}{extra}");
            }

            if (validation.ToolsNotAvailable.Count > 0)
            {
                // Ini BUKAN kesalahan penulisan plan - ini batas kemampuan kita
                // saat ini, dan pesannya harus mengatakan begitu.
                return (
                    PlanRejectionReason.ToolsNotAvailable,
                    "Rencana ini membutuhkan kemampuan yang belum terverifikasi: " +
                    string.Join(", ", validation.ToolsNotAvailable) + ".");
            }

            if (validation.BlockingQuestions.Count > 0 && !options.BlockingQuestionsAcknowledged)
            {
                return (
                    PlanRejectionReason.BlockingQuestions,
                    "Ada pertanyaan yang harus dijawab lebih dulu: " + validation.BlockingQuestions[0]);
            }

            return (PlanRejectionReason.None, string.Empty);
        }

        private async Task<StepOutcome> RunStepAsync(
            JsonObject step,
            string stepId,
            string toolName,
            IReadOnlyDictionary<string, JsonNode?> parameters,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            var outcome = new StepOutcome { StepId = stepId, Tool = toolName };

            _trace?.Write("step.started", new Dictionary<string, object?>
            {
                ["stepId"] = stepId,
                ["tool"] = toolName,
            });

            JsonObject arguments;
            try
            {
                arguments = ParameterResolver.Resolve(step["arguments"]?.AsObject(), parameters, stepId);
            }
            catch (ParameterResolutionException exception)
            {
                stopwatch.Stop();

                outcome.Result = ToolResult.Failure("unknown", ErrorKind.InvalidArgument, exception.Message);
                outcome.Verification = VerificationGate.Evaluate(new List<VerificationCheck>
                {
                    VerificationCheck.Fail("parameter_resolution", exception.Message),
                });
                outcome.Recovery = RecoveryAdvisor.Decide(ErrorKind.InvalidArgument);
                outcome.DurationMs = stopwatch.ElapsedMilliseconds;

                return outcome;
            }

            ToolResult toolResult;
            Exception? thrown = null;

            try
            {
                _trace?.Write("tool.called", new Dictionary<string, object?>
                {
                    ["stepId"] = stepId,
                    ["tool"] = toolName,
                    // Hanya nama argumen, bukan nilainya - nilai dimensi tidak
                    // sensitif, tapi ringkasan sudah cukup untuk diagnosis.
                    ["argumentKeys"] = arguments.Select(a => a.Key).ToArray(),
                });

                toolResult = await _executor.ExecuteAsync(toolName, arguments, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Executor melempar exception berarti kontraknya dilanggar, tapi
                // itu tidak boleh menjatuhkan seluruh run - apalagi karena kita
                // in-process di dalam SolidWorks.
                thrown = exception;
                toolResult = ToolResult.Failure(
                    "unknown",
                    RecoveryAdvisor.Classify(null, exception),
                    exception.Message);
            }

            outcome.Result = toolResult;

            // Verifikasi milik executor, ditambah pemeriksaan terhadap `expect`
            // di plan. Verification yang null berarti tidak ada bukti.
            var checks = new List<VerificationCheck>();

            if (toolResult.Verification is not null)
            {
                checks.AddRange(toolResult.Verification.Checks);
            }
            else if (toolResult.Ok)
            {
                checks.Add(VerificationCheck.Blocked(
                    "no_verification_evidence",
                    $"Tool '{toolName}' melaporkan sukses tanpa bukti verifikasi apa pun."));
            }

            if (toolResult.Error is not null)
            {
                checks.Add(VerificationCheck.Fail(
                    "tool_error",
                    toolResult.Error.Message,
                    observed: toolResult.Error.Kind.ToString()));
            }

            checks.AddRange(CheckExpectations(step, toolResult));

            outcome.Verification = VerificationGate.Evaluate(
                checks,
                manualReviewRequired: toolResult.Verification?.ManualReviewRequired ?? false);

            if (outcome.Verification.Verdict >= Verdict.Fail)
            {
                outcome.Recovery = RecoveryAdvisor.Advise(toolResult, thrown);

                _trace?.Write("tool.failed", new Dictionary<string, object?>
                {
                    ["stepId"] = stepId,
                    ["tool"] = toolName,
                    ["verdict"] = VerdictParser.ToWire(outcome.Verification.Verdict),
                    ["errorKind"] = outcome.Recovery.Kind.ToString(),
                    ["recoveryAction"] = outcome.Recovery.Action.ToString(),
                });
            }
            else
            {
                _trace?.Write("tool.succeeded", new Dictionary<string, object?>
                {
                    ["stepId"] = stepId,
                    ["tool"] = toolName,
                    ["verdict"] = VerdictParser.ToWire(outcome.Verification.Verdict),
                });
            }

            stopwatch.Stop();
            outcome.DurationMs = stopwatch.ElapsedMilliseconds;

            return outcome;
        }

        /// <summary>
        /// Memeriksa blok `expect` sebuah step terhadap data yang dikembalikan
        /// tool. Ini harapan tingkat plan - terpisah dari verifikasi milik
        /// executor, dan keduanya digabung.
        /// </summary>
        private static IEnumerable<VerificationCheck> CheckExpectations(JsonObject step, ToolResult result)
        {
            if (step["expect"] is not JsonObject expectations)
            {
                yield break;
            }

            foreach (var expectation in expectations)
            {
                var key = expectation.Key;
                var expected = expectation.Value;

                if (result.Data is null || !result.Data.TryGetValue(key, out var actual))
                {
                    // Nama fitur Hole Wizard berbeda antarversi dan antarbahasa
                    // UI, jadi harapan yang tidak bisa diperiksa menjadi WARN
                    // opsional - bukan kegagalan.
                    yield return new VerificationCheck
                    {
                        Id = $"expect_{key}",
                        Verdict = Verdict.Warn,
                        Required = false,
                        Expected = expected?.ToJsonString(),
                        Message = $"Tool tidak mengembalikan '{key}', harapan ini tidak bisa diperiksa.",
                    };
                    continue;
                }

                if (ValuesMatch(expected, actual))
                {
                    yield return VerificationCheck.Pass($"expect_{key}", actual);
                }
                else
                {
                    yield return VerificationCheck.Fail(
                        $"expect_{key}",
                        $"Plan mengharapkan {key}={Display(expected)}, model mengembalikan {Display(ToNode(actual))}.",
                        observed: actual,
                        expected: Display(expected));
                }
            }
        }

        /// <summary>
        /// Membandingkan nilai harapan dengan nilai yang dikembalikan tool,
        /// memakai semantik JSON - bukan <c>ToString()</c>.
        ///
        /// Ini penting dan pernah salah: <c>true.ToString()</c> di .NET
        /// menghasilkan "True", sementara JSON menulis "true". Membandingkan
        /// lewat ToString membuat SETIAP harapan bertipe boolean gagal palsu.
        /// Angka juga dibandingkan secara numerik, supaya 10 dan 10.0 dianggap
        /// sama.
        /// </summary>
        private static bool ValuesMatch(JsonNode? expected, object? actual)
        {
            if (expected is null || expected.GetValueKind() == JsonValueKind.Null)
            {
                return actual is null;
            }

            var actualNode = ToNode(actual);
            if (actualNode is null)
            {
                return false;
            }

            var expectedJson = expected.ToJsonString();
            var actualJson = actualNode.ToJsonString();

            if (double.TryParse(expectedJson, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber)
                && double.TryParse(actualJson, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualNumber))
            {
                return Math.Abs(expectedNumber - actualNumber) < 1e-9;
            }

            return string.Equals(expectedJson, actualJson, StringComparison.Ordinal);
        }

        private static JsonNode? ToNode(object? value)
        {
            if (value is null)
            {
                return null;
            }

            if (value is JsonNode node)
            {
                return node;
            }

            try
            {
                // Opsi diteruskan karena SerializeToNode MEMBAKAR teks
                // angkanya ke node. Tanpa ini, Display di bawah menampilkan
                // ekor G17 di pesan verifikasi yang dibaca manusia dan model.
                return JsonSerializer.SerializeToNode(value, MikosAI.Core.Json.JsonAngka.Opsi);
            }
            catch (NotSupportedException)
            {
                return JsonValue.Create(value.ToString());
            }
        }

        /// <summary>Bentuk yang enak dibaca manusia untuk pesan verifikasi.</summary>
        private static string Display(JsonNode? node)
            => node is null ? "(tidak ada)" : node.ToJsonString(MikosAI.Core.Json.JsonAngka.Opsi).Trim('"');

        private async Task<IReadOnlyList<VerificationCheck>> EvaluateAcceptanceAsync(
            JsonObject plan,
            CancellationToken cancellationToken)
        {
            if (plan["acceptance"] is not JsonArray acceptance || acceptance.Count == 0)
            {
                return Array.Empty<VerificationCheck>();
            }

            var criteria = acceptance
                .Select(node => node!.AsObject())
                .Select(obj => new AcceptanceCriterion
                {
                    Id = obj["id"]?.GetValue<string>() ?? string.Empty,
                    Check = obj["check"]?.GetValue<string>() ?? string.Empty,
                    Arguments = obj["arguments"]?.AsObject(),
                    Required = obj["required"] is null || obj["required"]!.GetValue<bool>(),
                })
                .ToList();

            if (_acceptanceEvaluator is null)
            {
                // Tanpa evaluator, kriteria penerimaan tidak bisa diperiksa.
                // Itu BLOCKED, bukan PASS - plan menuntut bukti yang tidak kita
                // punya caranya.
                return criteria
                    .Select(c => VerificationCheck.Blocked(
                        c.Id,
                        $"Kriteria '{c.Check}' tidak bisa diperiksa: tidak ada IAcceptanceEvaluator yang terpasang.",
                        c.Required))
                    .ToList();
            }

            try
            {
                var checks = await _acceptanceEvaluator
                    .EvaluateAsync(criteria, cancellationToken)
                    .ConfigureAwait(false);

                _trace?.Write("verification.completed", new Dictionary<string, object?>
                {
                    ["acceptanceCount"] = checks.Count,
                });

                return checks;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new List<VerificationCheck>
                {
                    VerificationCheck.Blocked(
                        "acceptance_evaluation_failed",
                        $"Evaluasi kriteria penerimaan gagal: {exception.Message}"),
                };
            }
        }
    }
}
