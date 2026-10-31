using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MikosAI.Core.Tools;

namespace MikosAI.Core.Plan
{
    public sealed class PlanProblem
    {
        /// <summary>Lokasi masalah, mis. steps[2].arguments.depth</summary>
        public string Path { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public PlanProblem(string path, string message)
        {
            Path = path;
            Message = message;
        }
    }

    public sealed class PlanValidationResult
    {
        public IReadOnlyList<PlanProblem> Errors { get; set; } = Array.Empty<PlanProblem>();

        /// <summary>Pertanyaan terbuka yang menandai blocking=true.</summary>
        public IReadOnlyList<string> BlockingQuestions { get; set; } = Array.Empty<string>();

        /// <summary>Tool yang diminta plan tapi tidak boleh diekspos.</summary>
        public IReadOnlyList<string> ToolsNotAvailable { get; set; } = Array.Empty<string>();

        /// <summary>Plan secara struktur sah.</summary>
        public bool IsValid => Errors.Count == 0 && ToolsNotAvailable.Count == 0;

        /// <summary>
        /// Boleh dieksekusi tanpa pengawasan. Plan yang valid tapi masih punya
        /// pertanyaan blocking TIDAK boleh jalan sendiri - manusia harus menjawab
        /// dulu.
        /// </summary>
        public bool MayRunUnattended => IsValid && BlockingQuestions.Count == 0;
    }

    /// <summary>
    /// Validasi DesignPlan sebelum eksekusi.
    ///
    /// Dijalankan SEBELUM model disentuh. Plan yang tidak valid ditolak tanpa
    /// membuat dokumen, tanpa membuka sketsa, tanpa efek samping apa pun.
    ///
    /// Validator ini memeriksa aturan semantik - hal yang JSON Schema tidak bisa
    /// nyatakan sendiri: tool tersedia di ledger, referensi parameter bisa
    /// diselesaikan, dan setiap tebakan AI punya pertanyaan terbuka yang
    /// menyertainya.
    /// </summary>
    public sealed class PlanValidator
    {
        private static readonly Regex StepIdPattern = new Regex("^[a-z0-9][a-z0-9_-]*$", RegexOptions.Compiled);
        private static readonly Regex ParameterRefPattern = new Regex(@"^\$[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private readonly ToolCatalog _catalog;

        public PlanValidator(ToolCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public PlanValidationResult Validate(string planJson)
        {
            using var document = JsonDocument.Parse(planJson);
            return Validate(document.RootElement);
        }

        public PlanValidationResult Validate(JsonElement plan)
        {
            var errors = new List<PlanProblem>();
            var unavailable = new List<string>();

            ValidateVersionAndUnits(plan, errors);

            var parameters = ReadParameterNames(plan, errors);
            var stepIds = ValidateSteps(plan, parameters, errors, unavailable);
            var blocking = ValidateOpenQuestions(plan, parameters, errors);

            ValidateAcceptance(plan, errors);

            // Pemeriksaan silang: dependsOn harus menunjuk step yang ada dan
            // sudah lebih dulu. Ketergantungan ke depan berarti urutannya salah.
            ValidateDependencies(plan, stepIds, errors);

            return new PlanValidationResult
            {
                Errors = errors,
                BlockingQuestions = blocking,
                ToolsNotAvailable = unavailable.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            };
        }

        private static void ValidateVersionAndUnits(JsonElement plan, List<PlanProblem> errors)
        {
            var version = GetString(plan, "schemaVersion");
            if (version != "1.0")
            {
                errors.Add(new PlanProblem("schemaVersion", $"schemaVersion harus \"1.0\", bukan \"{version ?? "(kosong)"}\"."));
            }

            if (string.IsNullOrWhiteSpace(GetString(plan, "title")))
            {
                errors.Add(new PlanProblem("title", "title wajib ada dan tidak boleh kosong."));
            }

            if (!plan.TryGetProperty("units", out var units))
            {
                errors.Add(new PlanProblem("units", "units wajib dinyatakan eksplisit. Satuan yang ambigu adalah sumber bug yang mahal."));
                return;
            }

            // mm dan deg adalah satu-satunya nilai yang sah. Konversi ke SI
            // terjadi di ToolExecutor, bukan di plan.
            if (GetString(units, "length") != "mm")
            {
                errors.Add(new PlanProblem("units.length", "units.length harus \"mm\"."));
            }

            if (GetString(units, "angle") != "deg")
            {
                errors.Add(new PlanProblem("units.angle", "units.angle harus \"deg\"."));
            }
        }

        private static HashSet<string> ReadParameterNames(JsonElement plan, List<PlanProblem> errors)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            if (!plan.TryGetProperty("parameters", out var parameters)
                || parameters.ValueKind != JsonValueKind.Object)
            {
                return names;
            }

            foreach (var parameter in parameters.EnumerateObject())
            {
                names.Add(parameter.Name);

                if (!parameter.Value.TryGetProperty("value", out _))
                {
                    errors.Add(new PlanProblem($"parameters.{parameter.Name}", "Parameter wajib punya field value."));
                }
            }

            return names;
        }

        private HashSet<string> ValidateSteps(
            JsonElement plan,
            HashSet<string> parameters,
            List<PlanProblem> errors,
            List<string> unavailable)
        {
            var stepIds = new HashSet<string>(StringComparer.Ordinal);

            if (!plan.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            {
                errors.Add(new PlanProblem("steps", "steps wajib ada dan berupa array."));
                return stepIds;
            }

            var index = 0;
            foreach (var step in steps.EnumerateArray())
            {
                var path = $"steps[{index}]";

                var id = GetString(step, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    errors.Add(new PlanProblem($"{path}.id", "Step wajib punya id."));
                }
                else if (!StepIdPattern.IsMatch(id!))
                {
                    errors.Add(new PlanProblem($"{path}.id", $"id '{id}' tidak sesuai pola [a-z0-9][a-z0-9_-]*."));
                }
                else if (!stepIds.Add(id!))
                {
                    errors.Add(new PlanProblem($"{path}.id", $"id '{id}' dipakai lebih dari sekali. Id step harus unik - trace jadi tidak bisa dibaca kalau duplikat."));
                }

                var toolName = GetString(step, "tool");
                if (string.IsNullOrWhiteSpace(toolName))
                {
                    errors.Add(new PlanProblem($"{path}.tool", "Step wajib menyebut tool."));
                }
                else
                {
                    var tool = _catalog.Find(toolName!);

                    if (tool is null)
                    {
                        errors.Add(new PlanProblem($"{path}.tool", $"Tool '{toolName}' tidak terdefinisi di tools.json."));
                    }
                    else if (!_catalog.ExposableTools().Any(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Tool-nya ada, tapi kemampuannya belum verified/pilot.
                        // Ini bukan error penulisan plan - ini batas kemampuan
                        // kita saat ini, dan pesannya harus mengatakan begitu.
                        unavailable.Add(toolName!);
                    }
                }

                // Referensi parameter di argumen harus bisa diselesaikan.
                if (step.TryGetProperty("arguments", out var arguments))
                {
                    ValidateParameterRefs(arguments, parameters, $"{path}.arguments", errors);
                }

                index++;
            }

            if (index == 0)
            {
                errors.Add(new PlanProblem("steps", "Plan harus punya minimal satu step."));
            }

            return stepIds;
        }

        /// <summary>
        /// Menelusuri argumen dan memastikan setiap string berbentuk "$nama"
        /// merujuk parameter yang benar-benar ada.
        ///
        /// v0.1 hanya mendukung referensi langsung - tidak ada aritmetika.
        /// String seperti "$plateWidth / 2" ditolak di sini dengan pesan yang
        /// menjelaskan batasannya, bukan dibiarkan gagal misterius saat eksekusi.
        /// </summary>
        private static void ValidateParameterRefs(
            JsonElement element,
            HashSet<string> parameters,
            string path,
            List<PlanProblem> errors)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        ValidateParameterRefs(property.Value, parameters, $"{path}.{property.Name}", errors);
                    }
                    break;

                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        ValidateParameterRefs(item, parameters, $"{path}[{index}]", errors);
                        index++;
                    }
                    break;

                case JsonValueKind.String:
                    var value = element.GetString();
                    if (value is null || !value.StartsWith("$", StringComparison.Ordinal))
                    {
                        break;
                    }

                    if (!ParameterRefPattern.IsMatch(value))
                    {
                        errors.Add(new PlanProblem(
                            path,
                            $"'{value}' bukan referensi parameter yang sah. v0.1 hanya mendukung referensi langsung " +
                            "seperti \"$plateWidth\" - belum ada ekspresi aritmetika. " +
                            "Hitung nilainya di parameters, atau tulis angkanya langsung."));
                        break;
                    }

                    var name = value.Substring(1);
                    if (!parameters.Contains(name))
                    {
                        errors.Add(new PlanProblem(path, $"Referensi '{value}' menunjuk parameter '{name}' yang tidak ada di parameters."));
                    }
                    break;
            }
        }

        /// <summary>
        /// Menegakkan aturan "jangan menebak diam-diam": setiap parameter yang
        /// ditandai source=assumed WAJIB punya entri openQuestions yang
        /// merujuknya lewat affectsParameters.
        ///
        /// Tanpa aturan ini, AI bisa mengarang nilai dan plan tetap terlihat
        /// bersih. Dengan aturan ini, tebakan selalu terlihat di UI.
        /// </summary>
        private static List<string> ValidateOpenQuestions(
            JsonElement plan,
            HashSet<string> parameters,
            List<PlanProblem> errors)
        {
            var blocking = new List<string>();
            var answered = new HashSet<string>(StringComparer.Ordinal);

            if (plan.TryGetProperty("openQuestions", out var questions) && questions.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var question in questions.EnumerateArray())
                {
                    var text = GetString(question, "question");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        errors.Add(new PlanProblem($"openQuestions[{index}].question", "Pertanyaan terbuka tidak boleh kosong."));
                    }

                    if (question.TryGetProperty("blocking", out var isBlocking) && isBlocking.ValueKind == JsonValueKind.True)
                    {
                        blocking.Add(text ?? "(tanpa teks)");
                    }

                    if (question.TryGetProperty("affectsParameters", out var affects) && affects.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var affected in affects.EnumerateArray())
                        {
                            var name = affected.GetString();
                            if (name is null)
                            {
                                continue;
                            }

                            answered.Add(name);

                            if (!parameters.Contains(name))
                            {
                                errors.Add(new PlanProblem(
                                    $"openQuestions[{index}].affectsParameters",
                                    $"Menyebut parameter '{name}' yang tidak ada di parameters."));
                            }
                        }
                    }

                    index++;
                }
            }

            if (plan.TryGetProperty("parameters", out var parameterObject)
                && parameterObject.ValueKind == JsonValueKind.Object)
            {
                foreach (var parameter in parameterObject.EnumerateObject())
                {
                    if (GetString(parameter.Value, "source") != "assumed")
                    {
                        continue;
                    }

                    if (!answered.Contains(parameter.Name))
                    {
                        errors.Add(new PlanProblem(
                            $"parameters.{parameter.Name}",
                            $"Parameter '{parameter.Name}' ditandai source=assumed tapi tidak ada entri openQuestions " +
                            "yang merujuknya lewat affectsParameters. Setiap tebakan harus terlihat oleh pengguna."));
                    }
                }
            }

            return blocking;
        }

        private static void ValidateAcceptance(JsonElement plan, List<PlanProblem> errors)
        {
            if (!plan.TryGetProperty("acceptance", out var acceptance) || acceptance.ValueKind != JsonValueKind.Array)
            {
                // Tidak ada kriteria penerimaan bukan error struktural, tapi
                // layak diingatkan: plan tanpa acceptance tidak punya cara
                // membuktikan hasil akhirnya benar.
                errors.Add(new PlanProblem(
                    "acceptance",
                    "Plan tidak punya kriteria acceptance. Tanpa itu tidak ada cara membuktikan hasil akhir benar setelah dokumen dibuka ulang."));
                return;
            }

            var index = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var criterion in acceptance.EnumerateArray())
            {
                var id = GetString(criterion, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    errors.Add(new PlanProblem($"acceptance[{index}].id", "Kriteria acceptance wajib punya id."));
                }
                else if (!ids.Add(id!))
                {
                    errors.Add(new PlanProblem($"acceptance[{index}].id", $"id acceptance '{id}' duplikat."));
                }

                if (string.IsNullOrWhiteSpace(GetString(criterion, "check")))
                {
                    errors.Add(new PlanProblem($"acceptance[{index}].check", "Kriteria acceptance wajib menyebut check."));
                }

                index++;
            }
        }

        private static void ValidateDependencies(JsonElement plan, HashSet<string> stepIds, List<PlanProblem> errors)
        {
            if (!plan.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;

            foreach (var step in steps.EnumerateArray())
            {
                var id = GetString(step, "id");

                if (step.TryGetProperty("dependsOn", out var dependsOn) && dependsOn.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dependency in dependsOn.EnumerateArray())
                    {
                        var name = dependency.GetString();
                        if (name is null)
                        {
                            continue;
                        }

                        if (!stepIds.Contains(name))
                        {
                            errors.Add(new PlanProblem($"steps[{index}].dependsOn", $"Menunjuk step '{name}' yang tidak ada."));
                        }
                        else if (!seen.Contains(name))
                        {
                            errors.Add(new PlanProblem(
                                $"steps[{index}].dependsOn",
                                $"Menunjuk step '{name}' yang belum dijalankan pada titik ini. Runner mengeksekusi berurutan, jadi ketergantungan harus menunjuk ke belakang."));
                        }
                    }
                }

                if (id is not null)
                {
                    seen.Add(id);
                }

                index++;
            }
        }

        private static string? GetString(JsonElement element, string propertyName)
            => element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
