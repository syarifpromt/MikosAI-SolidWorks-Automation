using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MikosAI.Core.Capabilities;

namespace MikosAI.Core.Tools
{
    /// <summary>Satu definisi tool dari schemas/tools.json.</summary>
    public sealed class ToolDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string CapabilityId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>true kalau tool ini mengubah model. Menentukan apakah verifikasi wajib.</summary>
        public bool Mutates { get; set; }

        /// <summary>JSON Schema untuk argumen, apa adanya dari file.</summary>
        public JsonElement InputSchema { get; set; }

        public string? Notes { get; set; }

        /// <summary>Fase roadmap. Hanya informasi, bukan gating.</summary>
        public int? Phase { get; set; }
    }

    /// <summary>
    /// Katalog tool, digabung dengan ledger kemampuan.
    ///
    /// Daftar tool MCP di-generate dari sini. Tool yang kemampuannya belum
    /// verified atau pilot TIDAK PERNAH muncul - itu yang membuat AI secara
    /// struktural tidak bisa memanggilnya.
    /// </summary>
    public sealed class ToolCatalog
    {
        private readonly Dictionary<string, ToolDefinition> _byName;
        private readonly CapabilityLedger _ledger;

        private ToolCatalog(CapabilityLedger ledger, IReadOnlyList<ToolDefinition> tools)
        {
            _ledger = ledger;
            All = tools;
            _byName = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Semua tool yang terdefinisi, tanpa memandang status.</summary>
        public IReadOnlyList<ToolDefinition> All { get; }

        public static ToolCatalog Load(string toolsJsonPath, CapabilityLedger ledger)
        {
            if (!File.Exists(toolsJsonPath))
            {
                throw new FileNotFoundException($"tools.json tidak ditemukan: {toolsJsonPath}", toolsJsonPath);
            }

            return Parse(File.ReadAllText(toolsJsonPath), ledger);
        }

        public static ToolCatalog Parse(string json, CapabilityLedger ledger)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var tools = new List<ToolDefinition>();

            foreach (var arrayName in new[] { "tools", "orchestrationTools" })
            {
                if (!root.TryGetProperty(arrayName, out var array))
                {
                    continue;
                }

                foreach (var element in array.EnumerateArray())
                {
                    tools.Add(ReadTool(element));
                }
            }

            var duplicates = tools
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
            {
                throw new InvalidDataException(
                    "Nama tool duplikat di tools.json: " + string.Join(", ", duplicates));
            }

            return new ToolCatalog(ledger, tools);
        }

        public ToolDefinition? Find(string toolName)
            => _byName.TryGetValue(toolName, out var tool) ? tool : null;

        /// <summary>
        /// Tool yang boleh diekspos sebagai tool MCP: terdefinisi di katalog DAN
        /// kemampuannya verified atau pilot di ledger.
        ///
        /// Kedua syarat harus dipenuhi. Tool yang ada di katalog tapi
        /// kemampuannya belum siap tidak muncul, dan kemampuan yang siap tapi
        /// tool-nya belum terdefinisi juga tidak muncul.
        /// </summary>
        public IReadOnlyList<ToolDefinition> ExposableTools()
            => All
                .Where(tool => _ledger.Find(tool.CapabilityId)?.IsExposable == true)
                .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// Memeriksa konsistensi katalog terhadap ledger. Dipanggil saat startup -
        /// lebih baik gagal keras saat load daripada menemukan tool yang menggantung
        /// di tengah eksekusi.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();

            foreach (var tool in All)
            {
                var capability = _ledger.Find(tool.CapabilityId);

                if (capability is null)
                {
                    problems.Add($"Tool '{tool.Name}' merujuk capabilityId '{tool.CapabilityId}' yang tidak ada di capabilities.yaml.");
                    continue;
                }

                // Tool yang menulis WAJIB punya daftar verifikasi di ledger.
                // Tanpa itu, tidak ada cara membuktikan hasilnya benar.
                if (tool.Mutates && capability.IsExposable && capability.Verification.Count == 0)
                {
                    problems.Add(
                        $"Tool '{tool.Name}' mengubah model dan kemampuannya sudah diekspos, " +
                        $"tapi '{capability.Id}' tidak punya daftar verification. " +
                        "Operasi tulis tanpa verifikasi tidak boleh diekspos.");
                }
            }

            // Arah sebaliknya: tool yang disebut ledger tapi tidak ada di katalog.
            foreach (var capability in _ledger.Capabilities)
            {
                foreach (var toolName in capability.Tools)
                {
                    if (Find(toolName) is null)
                    {
                        problems.Add($"capabilities.yaml menyebut tool '{toolName}' (kemampuan '{capability.Id}') yang tidak terdefinisi di tools.json.");
                    }
                }
            }

            return problems;
        }

        private static ToolDefinition ReadTool(JsonElement element)
        {
            var definition = new ToolDefinition
            {
                Name = GetString(element, "name") ?? throw new InvalidDataException("Ada tool tanpa name di tools.json."),
                CapabilityId = GetString(element, "capabilityId") ?? string.Empty,
                Description = GetString(element, "description") ?? string.Empty,
                Notes = GetString(element, "notes"),
                Mutates = element.TryGetProperty("mutates", out var mutates)
                          && mutates.ValueKind == JsonValueKind.True,
            };

            if (element.TryGetProperty("inputSchema", out var schema))
            {
                definition.InputSchema = schema.Clone();
            }

            if (element.TryGetProperty("phase", out var phase) && phase.ValueKind == JsonValueKind.Number)
            {
                definition.Phase = phase.GetInt32();
            }

            return definition;
        }

        private static string? GetString(JsonElement element, string propertyName)
            => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
