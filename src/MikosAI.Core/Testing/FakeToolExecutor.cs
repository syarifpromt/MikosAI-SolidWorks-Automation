using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MikosAI.Core.Execution;

namespace MikosAI.Core.Testing
{
    /// <summary>
    /// Executor tool palsu untuk pengembangan dan test.
    ///
    /// Sengaja ada di MikosAI.Core, bukan di proyek test, karena justru dipakai
    /// di luar test: Jalur Platform membangun PlanRunner, UI, McpHttpServer, dan
    /// ProviderHost sepenuhnya di atas fake ini - tanpa SolidWorks, tanpa
    /// lisensi, tanpa menunggu Jalur CAD selesai.
    ///
    /// Secara default setiap tool "berhasil" dengan satu pemeriksaan verifikasi
    /// yang lulus. Perilaku per tool bisa diatur lewat <see cref="When"/>.
    /// </summary>
    public sealed class FakeToolExecutor : IToolExecutor
    {
        private readonly Dictionary<string, Func<JsonObject, ToolResult>> _handlers =
            new Dictionary<string, Func<JsonObject, ToolResult>>(StringComparer.OrdinalIgnoreCase);

        private readonly List<RecordedCall> _calls = new List<RecordedCall>();

        /// <summary>Catatan setiap pemanggilan, untuk assertion di test.</summary>
        public sealed class RecordedCall
        {
            public string Tool { get; set; } = string.Empty;
            public JsonObject Arguments { get; set; } = new JsonObject();
        }

        public IReadOnlyList<RecordedCall> Calls => _calls;

        /// <summary>Jumlah pemanggilan. 0 membuktikan model tidak pernah disentuh.</summary>
        public int CallCount => _calls.Count;

        /// <summary>Apakah tool ini pernah dipanggil.</summary>
        public bool WasCalled(string toolName)
            => _calls.Any(c => string.Equals(c.Tool, toolName, StringComparison.OrdinalIgnoreCase));

        /// <summary>Argumen yang diterima pada pemanggilan pertama tool ini.</summary>
        public JsonObject? ArgumentsFor(string toolName)
            => _calls.FirstOrDefault(c => string.Equals(c.Tool, toolName, StringComparison.OrdinalIgnoreCase))?.Arguments;

        /// <summary>Mengatur perilaku satu tool.</summary>
        public FakeToolExecutor When(string toolName, Func<JsonObject, ToolResult> handler)
        {
            _handlers[toolName] = handler;
            return this;
        }

        /// <summary>Tool ini gagal dengan kelas error tertentu.</summary>
        public FakeToolExecutor Fails(string toolName, ErrorKind kind, string message)
            => When(toolName, _ => ToolResult.Failure("fake", kind, message));

        /// <summary>
        /// Tool ini melaporkan sukses tapi verifikasinya gagal - skenario nyata
        /// yang paling penting diuji: extrude_cut yang tidak memotong apa pun.
        /// </summary>
        public FakeToolExecutor SucceedsButVerificationFails(string toolName, string checkId, string message)
            => When(toolName, _ => new ToolResult
            {
                Ok = true,
                CapabilityId = "fake",
                Verification = VerificationGate.Evaluate(new List<VerificationCheck>
                {
                    VerificationCheck.Fail(checkId, message),
                }),
            });

        /// <summary>Tool ini melaporkan sukses tanpa bukti verifikasi apa pun.</summary>
        public FakeToolExecutor SucceedsWithoutEvidence(string toolName)
            => When(toolName, _ => new ToolResult { Ok = true, CapabilityId = "fake" });

        /// <summary>Tool ini berhasil dan mengembalikan data tertentu.</summary>
        public FakeToolExecutor Returns(string toolName, params (string Key, object? Value)[] data)
            => When(toolName, _ => Success(data));

        /// <summary>Tool ini melempar exception - kontrak dilanggar.</summary>
        public FakeToolExecutor Throws(string toolName, Exception exception)
            => When(toolName, _ => throw exception);

        /// <summary>Tool ini berhasil tapi menyisakan kewajiban review manusia.</summary>
        public FakeToolExecutor NeedsManualReview(string toolName)
            => When(toolName, _ => new ToolResult
            {
                Ok = true,
                CapabilityId = "fake",
                Verification = VerificationGate.Evaluate(
                    new List<VerificationCheck> { VerificationCheck.Pass("fake_check") },
                    manualReviewRequired: true),
            });

        public Task<ToolResult> ExecuteAsync(
            string toolName,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            _calls.Add(new RecordedCall
            {
                Tool = toolName,
                Arguments = arguments.DeepClone()!.AsObject(),
            });

            if (_handlers.TryGetValue(toolName, out var handler))
            {
                return Task.FromResult(handler(arguments));
            }

            return Task.FromResult(Success());
        }

        private static ToolResult Success(params (string Key, object? Value)[] data)
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var item in data)
            {
                payload[item.Key] = item.Value;
            }

            return new ToolResult
            {
                Ok = true,
                CapabilityId = "fake",
                Data = payload,
                Verification = VerificationGate.Evaluate(new List<VerificationCheck>
                {
                    VerificationCheck.Pass("fake_check"),
                }),
            };
        }
    }

    /// <summary>
    /// Evaluator kriteria penerimaan palsu. Secara default semua kriteria lulus.
    /// </summary>
    public sealed class FakeAcceptanceEvaluator : IAcceptanceEvaluator
    {
        private readonly Dictionary<string, Verdict> _verdicts =
            new Dictionary<string, Verdict>(StringComparer.Ordinal);

        public int EvaluateCount { get; private set; }

        /// <summary>Kriteria dengan id ini menghasilkan putusan tertentu.</summary>
        public FakeAcceptanceEvaluator With(string criterionId, Verdict verdict)
        {
            _verdicts[criterionId] = verdict;
            return this;
        }

        public Task<IReadOnlyList<VerificationCheck>> EvaluateAsync(
            IReadOnlyList<AcceptanceCriterion> criteria,
            CancellationToken cancellationToken = default)
        {
            EvaluateCount++;

            var checks = criteria
                .Select(criterion => new VerificationCheck
                {
                    Id = criterion.Id,
                    Verdict = _verdicts.TryGetValue(criterion.Id, out var verdict) ? verdict : Verdict.Pass,
                    Required = criterion.Required,
                    Message = $"Kriteria palsu '{criterion.Check}'.",
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<VerificationCheck>>(checks);
        }
    }
}
