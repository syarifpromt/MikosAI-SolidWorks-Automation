using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MikosAI.Core.Plan
{
    /// <summary>
    /// Dilempar kalau referensi parameter tidak bisa diselesaikan saat eksekusi.
    /// Normalnya PlanValidator sudah menangkap ini lebih dulu; kalau sampai ke
    /// sini berarti ada yang lolos, dan lebih baik gagal keras daripada
    /// mengirim string "$plateWidth" ke API SolidWorks sebagai dimensi.
    /// </summary>
    public sealed class ParameterResolutionException : InvalidOperationException
    {
        public ParameterResolutionException(string message) : base(message) { }
    }

    /// <summary>
    /// Mengganti referensi "$nama" di argumen step dengan nilai parameternya.
    ///
    /// v0.1 hanya mendukung referensi langsung - tidak ada aritmetika. Keputusan
    /// itu beserta jalan keluarnya didokumentasikan di
    /// docs/skema-tool-dan-plan.md bagian 2.
    /// </summary>
    public static class ParameterResolver
    {
        private static readonly Regex ParameterRefPattern =
            new Regex(@"^\$[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

        /// <summary>
        /// Membaca parameters dari plan, lalu menerapkan override di atasnya.
        ///
        /// Override hanya boleh mengubah parameter yang SUDAH ADA. Menambah
        /// parameter baru lewat override ditolak - itu mengubah bentuk plan, dan
        /// plan yang berubah bentuk harus divalidasi ulang, bukan ditambal saat
        /// jalan.
        /// </summary>
        public static Dictionary<string, JsonNode?> BuildParameterTable(
            JsonObject? planParameters,
            IReadOnlyDictionary<string, object?>? overrides)
        {
            var table = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

            if (planParameters is not null)
            {
                foreach (var parameter in planParameters)
                {
                    if (parameter.Value is JsonObject definition
                        && definition.TryGetPropertyValue("value", out var value))
                    {
                        table[parameter.Key] = value?.DeepClone();
                    }
                }
            }

            if (overrides is not null)
            {
                foreach (var pair in overrides)
                {
                    if (!table.ContainsKey(pair.Key))
                    {
                        throw new ParameterResolutionException(
                            $"Override menyebut parameter '{pair.Key}' yang tidak ada di plan. " +
                            "Override hanya boleh mengubah nilai parameter yang sudah ada.");
                    }

                    table[pair.Key] = pair.Value is null ? null : JsonValue.Create(pair.Value);
                }
            }

            return table;
        }

        /// <summary>
        /// Menyalin argumen dan mengganti setiap referensi parameter dengan
        /// nilainya. Argumen asli tidak diubah, supaya plan bisa dijalankan
        /// ulang dengan override berbeda.
        /// </summary>
        public static JsonObject Resolve(
            JsonObject? arguments,
            IReadOnlyDictionary<string, JsonNode?> parameters,
            string stepId)
        {
            if (arguments is null)
            {
                return new JsonObject();
            }

            var resolved = arguments.DeepClone()!.AsObject();
            ResolveInPlace(resolved, parameters, stepId);
            return resolved;
        }

        private static void ResolveInPlace(
            JsonNode node,
            IReadOnlyDictionary<string, JsonNode?> parameters,
            string stepId)
        {
            switch (node)
            {
                case JsonObject obj:
                    // Materialisasi dulu: mengganti nilai sambil mengiterasi
                    // JsonObject akan melempar exception.
                    var properties = new List<KeyValuePair<string, JsonNode?>>(obj);

                    foreach (var property in properties)
                    {
                        if (TryResolveLeaf(property.Value, parameters, stepId, out var replacement))
                        {
                            obj[property.Key] = replacement;
                        }
                        else if (property.Value is not null)
                        {
                            ResolveInPlace(property.Value, parameters, stepId);
                        }
                    }
                    break;

                case JsonArray array:
                    for (var index = 0; index < array.Count; index++)
                    {
                        if (TryResolveLeaf(array[index], parameters, stepId, out var replacement))
                        {
                            array[index] = replacement;
                        }
                        else if (array[index] is not null)
                        {
                            ResolveInPlace(array[index]!, parameters, stepId);
                        }
                    }
                    break;
            }
        }

        private static bool TryResolveLeaf(
            JsonNode? node,
            IReadOnlyDictionary<string, JsonNode?> parameters,
            string stepId,
            out JsonNode? replacement)
        {
            replacement = null;

            if (node is not JsonValue value
                || !value.TryGetValue<string>(out var text)
                || text is null
                || !text.StartsWith("$", StringComparison.Ordinal))
            {
                return false;
            }

            if (!ParameterRefPattern.IsMatch(text))
            {
                throw new ParameterResolutionException(
                    $"Step '{stepId}': '{text}' bukan referensi parameter yang sah. " +
                    "v0.1 hanya mendukung referensi langsung seperti \"$plateWidth\", belum ada ekspresi aritmetika.");
            }

            var name = text.Substring(1);

            if (!parameters.TryGetValue(name, out var parameterValue))
            {
                throw new ParameterResolutionException(
                    $"Step '{stepId}': referensi '{text}' menunjuk parameter '{name}' yang tidak ada.");
            }

            replacement = parameterValue?.DeepClone();
            return true;
        }
    }
}
