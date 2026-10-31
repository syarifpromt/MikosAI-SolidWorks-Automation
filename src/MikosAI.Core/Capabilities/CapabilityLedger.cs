using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MikosAI.Core.Capabilities
{
    /// <summary>
    /// Status kemampuan. Hanya Verified dan Pilot yang boleh diekspos ke AI.
    /// Status yang tidak dikenali di file menjadi NotImplemented - default aman.
    /// </summary>
    public enum CapabilityStatus
    {
        /// <summary>Belum ada. Tidak diekspos.</summary>
        NotImplemented = 0,

        /// <summary>Ada dokumentasi, belum ada implementasi teruji. Tidak diekspos.</summary>
        ReferenceOnly = 1,

        /// <summary>Jalan, tapi butuh review manusia. Diekspos dengan review_required.</summary>
        Pilot = 2,

        /// <summary>Golden test lulus di SolidWorks nyata, versi tercatat. Diekspos.</summary>
        Verified = 3,
    }

    public sealed class Capability
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public CapabilityStatus Status { get; set; } = CapabilityStatus.NotImplemented;
        public bool ReviewRequired { get; set; } = true;
        public IReadOnlyList<string> Tools { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Verification { get; set; } = Array.Empty<string>();
        public string? Notes { get; set; }
        public string? Phase { get; set; }

        /// <summary>
        /// Apakah tool dari kemampuan ini boleh muncul di daftar MCP.
        /// Inilah mekanisme yang membuat AI secara struktural tidak bisa
        /// memanggil hal yang belum kita uji - bukan dilarang lewat prompt,
        /// tool-nya memang tidak ada.
        /// </summary>
        public bool IsExposable
            => Status == CapabilityStatus.Verified || Status == CapabilityStatus.Pilot;

        /// <summary>
        /// Pilot selalu butuh review manusia, apa pun isi field di YAML.
        /// Ditegakkan di sini supaya tidak bisa dilonggarkan lewat konfigurasi.
        /// </summary>
        public bool EffectiveReviewRequired
            => Status == CapabilityStatus.Pilot || ReviewRequired;
    }

    /// <summary>
    /// Pembaca capabilities.yaml - satu-satunya sumber kebenaran kemampuan.
    ///
    /// Tidak ada registry kedua. Kalau butuh tahu apakah sesuatu boleh
    /// dijalankan, jawabannya ada di sini.
    /// </summary>
    public sealed class CapabilityLedger
    {
        private readonly Dictionary<string, Capability> _byId;
        private readonly Dictionary<string, Capability> _byToolName;

        public IReadOnlyList<string> VerifiedSolidWorksVersions { get; }
        public string SchemaVersion { get; }

        private CapabilityLedger(
            string schemaVersion,
            IReadOnlyList<string> verifiedVersions,
            IReadOnlyList<Capability> capabilities)
        {
            SchemaVersion = schemaVersion;
            VerifiedSolidWorksVersions = verifiedVersions;
            Capabilities = capabilities;

            _byId = capabilities.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

            _byToolName = new Dictionary<string, Capability>(StringComparer.OrdinalIgnoreCase);
            foreach (var capability in capabilities)
            {
                foreach (var tool in capability.Tools)
                {
                    // Satu tool hanya boleh dimiliki satu kemampuan. Kalau tidak,
                    // gating jadi ambigu dan bisa dimanfaatkan untuk melewatinya.
                    if (_byToolName.ContainsKey(tool))
                    {
                        throw new InvalidDataException(
                            $"Tool '{tool}' terdaftar di lebih dari satu kemampuan " +
                            $"('{_byToolName[tool].Id}' dan '{capability.Id}'). " +
                            "Setiap tool harus dimiliki tepat satu kemampuan.");
                    }

                    _byToolName[tool] = capability;
                }
            }
        }

        public IReadOnlyList<Capability> Capabilities { get; }

        public static CapabilityLedger Load(string yamlPath)
        {
            if (!File.Exists(yamlPath))
            {
                throw new FileNotFoundException($"capabilities.yaml tidak ditemukan: {yamlPath}", yamlPath);
            }

            return Parse(File.ReadAllText(yamlPath));
        }

        public static CapabilityLedger Parse(string yaml)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var raw = deserializer.Deserialize<LedgerFile>(yaml)
                      ?? throw new InvalidDataException("capabilities.yaml kosong atau tidak bisa dibaca.");

            var capabilities = (raw.Capabilities ?? new List<CapabilityEntry>())
                .Select(ToCapability)
                .ToList();

            var versions = raw.VerifiedVersions?.SolidWorks ?? new List<string>();

            return new CapabilityLedger(
                raw.SchemaVersion ?? "unknown",
                versions,
                capabilities);
        }

        public Capability? Find(string capabilityId)
            => _byId.TryGetValue(capabilityId, out var capability) ? capability : null;

        /// <summary>Kemampuan yang memiliki tool ini, atau null kalau tool tidak terdaftar.</summary>
        public Capability? FindByTool(string toolName)
            => _byToolName.TryGetValue(toolName, out var capability) ? capability : null;

        /// <summary>
        /// Apakah tool ini boleh dipanggil. Tool yang tidak terdaftar di ledger
        /// dijawab false - bukan karena diblokir, tapi karena kita tidak punya
        /// dasar untuk mengizinkannya.
        /// </summary>
        public bool IsToolExposable(string toolName)
            => FindByTool(toolName)?.IsExposable == true;

        /// <summary>Semua nama tool yang boleh diekspos ke AI.</summary>
        public IReadOnlyList<string> ExposableToolNames()
            => Capabilities
                .Where(c => c.IsExposable)
                .SelectMany(c => c.Tools)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static Capability ToCapability(CapabilityEntry entry) => new Capability
        {
            Id = entry.Id ?? throw new InvalidDataException("Ada entri kemampuan tanpa id."),
            Label = entry.Label ?? string.Empty,
            Status = ParseStatus(entry.Status),
            ReviewRequired = entry.ReviewRequired ?? true,
            Tools = (IReadOnlyList<string>?)entry.Tools ?? Array.Empty<string>(),
            Verification = (IReadOnlyList<string>?)entry.Verification ?? Array.Empty<string>(),
            Notes = entry.Notes,
            Phase = entry.Phase,
        };

        /// <summary>
        /// Status yang tidak dikenali menjadi NotImplemented, bukan Verified.
        /// Salah tulis di YAML harus membuat kemampuan hilang dari daftar,
        /// bukan muncul sebagai sudah teruji.
        /// </summary>
        internal static CapabilityStatus ParseStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return CapabilityStatus.NotImplemented;
            }

            switch (status!.Trim().ToLowerInvariant())
            {
                case "verified": return CapabilityStatus.Verified;
                case "pilot": return CapabilityStatus.Pilot;
                case "reference_only": return CapabilityStatus.ReferenceOnly;
                case "not_implemented": return CapabilityStatus.NotImplemented;
                default: return CapabilityStatus.NotImplemented;
            }
        }

        // --- Bentuk mentah YAML ---------------------------------------------

        private sealed class LedgerFile
        {
            public string? SchemaVersion { get; set; }
            public VerifiedVersionsEntry? VerifiedVersions { get; set; }
            public List<CapabilityEntry>? Capabilities { get; set; }
        }

        private sealed class VerifiedVersionsEntry
        {
            // Alias eksplisit: UnderscoredNamingConvention akan mengubah
            // SolidWorks menjadi solid_works, yang tidak cocok dengan YAML.
            [YamlMember(Alias = "solidworks")]
            public List<string>? SolidWorks { get; set; }
        }

        private sealed class CapabilityEntry
        {
            public string? Id { get; set; }
            public string? Label { get; set; }
            public string? Status { get; set; }
            public bool? ReviewRequired { get; set; }
            public List<string>? Tools { get; set; }
            public List<string>? Verification { get; set; }
            public string? Notes { get; set; }
            public string? Phase { get; set; }
        }
    }
}
