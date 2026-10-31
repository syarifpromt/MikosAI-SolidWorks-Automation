using System.IO;
using System.Linq;
using MikosAI.Core.Capabilities;
using MikosAI.Core.Plan;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    public class PlanValidatorTests
    {
        /// <summary>
        /// Validator dengan ledger yang semua tool v0.1-nya dibuat verified,
        /// supaya test bisa fokus ke aturan plan - bukan ke gating.
        /// Gating diuji terpisah di <see cref="LedgerAndCatalogTests"/>.
        /// </summary>
        private static PlanValidator ValidatorWithAllToolsAvailable()
        {
            var realLedger = CapabilityLedger.Load(RepoFiles.CapabilitiesYaml);
            var yaml = File.ReadAllText(RepoFiles.CapabilitiesYaml)
                           .Replace("status: not_implemented", "status: verified");

            var ledger = CapabilityLedger.Parse(yaml);
            Assert.NotEmpty(ledger.ExposableToolNames());
            Assert.NotEmpty(realLedger.Capabilities);

            return new PlanValidator(ToolCatalog.Load(RepoFiles.ToolsJson, ledger));
        }

        private static PlanValidator ValidatorWithRealLedger()
            => new PlanValidator(ToolCatalog.Load(
                RepoFiles.ToolsJson,
                CapabilityLedger.Load(RepoFiles.CapabilitiesYaml)));

        private const string MinimalPlanTemplate = @"{
          ""schemaVersion"": ""1.0"",
          ""title"": ""Uji"",
          ""units"": { ""length"": ""mm"", ""angle"": ""deg"" },
          ""document"": { ""type"": ""part"" },
          ""parameters"": { PARAMS },
          ""steps"": [ STEPS ],
          ""acceptance"": [ { ""id"": ""a1"", ""check"": ""rebuild_clean"" } ],
          ""openQuestions"": [ QUESTIONS ]
        }";

        private static string Plan(string steps, string parameters = "", string questions = "")
            => MinimalPlanTemplate
                .Replace("PARAMS", parameters)
                .Replace("STEPS", steps)
                .Replace("QUESTIONS", questions);

        // --- Contoh nyata ---------------------------------------------------

        [Fact]
        public void contoh_mounting_plate_sah_secara_struktur()
        {
            var result = ValidatorWithAllToolsAvailable()
                .Validate(File.ReadAllText(RepoFiles.MountingPlatePlan));

            Assert.True(
                result.Errors.Count == 0,
                "Contoh plan punya error:\n" + string.Join("\n", result.Errors.Select(e => $"{e.Path}: {e.Message}")));
        }

        [Fact]
        public void contoh_mounting_plate_boleh_jalan_karena_tidak_ada_pertanyaan_blocking()
        {
            var result = ValidatorWithAllToolsAvailable()
                .Validate(File.ReadAllText(RepoFiles.MountingPlatePlan));

            Assert.True(result.IsValid);
            Assert.True(result.MayRunUnattended);
        }

        [Fact]
        public void dengan_ledger_sebenarnya_contoh_plan_belum_bisa_dijalankan()
        {
            // Keadaan jujur hari ini: plan-nya benar, tapi tool-nya belum ada
            // yang terverifikasi. Ini bukan error penulisan plan.
            var result = ValidatorWithRealLedger()
                .Validate(File.ReadAllText(RepoFiles.MountingPlatePlan));

            Assert.Empty(result.Errors);
            Assert.NotEmpty(result.ToolsNotAvailable);
            Assert.False(result.IsValid);
        }

        // --- Aturan "jangan menebak diam-diam" ------------------------------

        [Fact]
        public void parameter_assumed_tanpa_pertanyaan_terbuka_ditolak()
        {
            var json = Plan(
                steps: @"{ ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} }",
                parameters: @"""tebakan"": { ""value"": 5, ""source"": ""assumed"" }");

            var result = ValidatorWithAllToolsAvailable().Validate(json);

            Assert.Contains(result.Errors, e => e.Path == "parameters.tebakan" && e.Message.Contains("assumed"));
        }

        [Fact]
        public void parameter_assumed_dengan_pertanyaan_terbuka_diterima()
        {
            var json = Plan(
                steps: @"{ ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} }",
                parameters: @"""tebakan"": { ""value"": 5, ""source"": ""assumed"" }",
                questions: @"{ ""question"": ""Radius fillet berapa?"", ""affectsParameters"": [""tebakan""], ""blocking"": false }");

            var result = ValidatorWithAllToolsAvailable().Validate(json);

            Assert.Empty(result.Errors);
        }

        [Fact]
        public void pertanyaan_blocking_menahan_eksekusi_tanpa_pengawasan()
        {
            var json = Plan(
                steps: @"{ ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} }",
                questions: @"{ ""question"": ""Material apa?"", ""blocking"": true }");

            var result = ValidatorWithAllToolsAvailable().Validate(json);

            Assert.True(result.IsValid);
            Assert.False(result.MayRunUnattended);
            Assert.Single(result.BlockingQuestions);
        }

        // --- Referensi parameter --------------------------------------------

        [Fact]
        public void referensi_parameter_yang_tidak_ada_ditolak()
        {
            var json = Plan(
                steps: @"{ ""id"": ""s1"", ""tool"": ""add_circle"", ""arguments"": { ""center"": { ""x"": 0, ""y"": 0 }, ""diameter"": ""$tidakAda"" } }");

            var result = ValidatorWithAllToolsAvailable().Validate(json);

            Assert.Contains(result.Errors, e => e.Message.Contains("tidakAda"));
        }

        [Fact]
        public void ekspresi_aritmetika_ditolak_dengan_pesan_yang_menjelaskan_batasannya()
        {
            // Keterbatasan v0.1 yang sudah kita putuskan secara sadar. Pesannya
            // harus memberi jalan keluar, bukan sekadar menolak.
            var json = Plan(
                steps: @"{ ""id"": ""s1"", ""tool"": ""add_circle"", ""arguments"": { ""center"": { ""x"": 0, ""y"": 0 }, ""diameter"": ""$lebar / 2"" } }",
                parameters: @"""lebar"": { ""value"": 100 }");

            var result = ValidatorWithAllToolsAvailable().Validate(json);

            var problem = Assert.Single(result.Errors);
            Assert.Contains("aritmetika", problem.Message);
        }

        [Fact]
        public void referensi_parameter_yang_benar_di_dalam_array_dan_objek_bersarang_diterima()
        {
            var json = Plan(
                steps: @"{ ""id"": ""s1"", ""tool"": ""hole_wizard"", ""arguments"": {
                    ""positions"": [ { ""x"": ""$inset"", ""y"": ""$inset"" } ]
                } }",
                parameters: @"""inset"": { ""value"": 15 }");

            var result = ValidatorWithAllToolsAvailable().Validate(json);

            Assert.Empty(result.Errors);
        }

        // --- Satuan ---------------------------------------------------------

        [Theory]
        [InlineData(@"{ ""length"": ""inch"", ""angle"": ""deg"" }", "units.length")]
        [InlineData(@"{ ""length"": ""mm"", ""angle"": ""rad"" }", "units.angle")]
        public void satuan_selain_mm_dan_deg_ditolak(string units, string expectedPath)
        {
            var json = $@"{{
              ""schemaVersion"": ""1.0"",
              ""title"": ""Uji"",
              ""units"": {units},
              ""document"": {{ ""type"": ""part"" }},
              ""steps"": [ {{ ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {{}} }} ],
              ""acceptance"": [ {{ ""id"": ""a1"", ""check"": ""rebuild_clean"" }} ]
            }}";

            var result = ValidatorWithAllToolsAvailable().Validate(json);

            Assert.Contains(result.Errors, e => e.Path == expectedPath);
        }

        [Fact]
        public void units_yang_hilang_ditolak()
        {
            var json = @"{
              ""schemaVersion"": ""1.0"",
              ""title"": ""Uji"",
              ""document"": { ""type"": ""part"" },
              ""steps"": [ { ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} } ],
              ""acceptance"": [ { ""id"": ""a1"", ""check"": ""rebuild_clean"" } ]
            }";

            Assert.Contains(ValidatorWithAllToolsAvailable().Validate(json).Errors, e => e.Path == "units");
        }

        // --- Step -----------------------------------------------------------

        [Fact]
        public void id_step_duplikat_ditolak()
        {
            var json = Plan(steps: @"
                { ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} },
                { ""id"": ""s1"", ""tool"": ""list_features"", ""arguments"": {} }");

            Assert.Contains(ValidatorWithAllToolsAvailable().Validate(json).Errors,
                e => e.Message.Contains("lebih dari sekali"));
        }

        [Fact]
        public void tool_yang_tidak_terdefinisi_ditolak()
        {
            var json = Plan(steps: @"{ ""id"": ""s1"", ""tool"": ""tool_karangan"", ""arguments"": {} }");

            Assert.Contains(ValidatorWithAllToolsAvailable().Validate(json).Errors,
                e => e.Message.Contains("tool_karangan"));
        }

        [Fact]
        public void plan_tanpa_step_ditolak()
        {
            var json = @"{
              ""schemaVersion"": ""1.0"",
              ""title"": ""Uji"",
              ""units"": { ""length"": ""mm"", ""angle"": ""deg"" },
              ""document"": { ""type"": ""part"" },
              ""steps"": [],
              ""acceptance"": [ { ""id"": ""a1"", ""check"": ""rebuild_clean"" } ]
            }";

            Assert.Contains(ValidatorWithAllToolsAvailable().Validate(json).Errors, e => e.Path == "steps");
        }

        // --- Ketergantungan -------------------------------------------------

        [Fact]
        public void dependsOn_ke_step_yang_tidak_ada_ditolak()
        {
            var json = Plan(steps: @"{ ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {}, ""dependsOn"": [""s99""] }");

            Assert.Contains(ValidatorWithAllToolsAvailable().Validate(json).Errors,
                e => e.Message.Contains("s99"));
        }

        [Fact]
        public void dependsOn_ke_step_yang_belum_dijalankan_ditolak()
        {
            // Runner mengeksekusi berurutan, jadi ketergantungan ke depan
            // berarti urutan step-nya salah.
            var json = Plan(steps: @"
                { ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {}, ""dependsOn"": [""s2""] },
                { ""id"": ""s2"", ""tool"": ""list_features"", ""arguments"": {} }");

            Assert.Contains(ValidatorWithAllToolsAvailable().Validate(json).Errors,
                e => e.Message.Contains("belum dijalankan"));
        }

        // --- Acceptance -----------------------------------------------------

        [Fact]
        public void plan_tanpa_acceptance_ditolak()
        {
            var json = @"{
              ""schemaVersion"": ""1.0"",
              ""title"": ""Uji"",
              ""units"": { ""length"": ""mm"", ""angle"": ""deg"" },
              ""document"": { ""type"": ""part"" },
              ""steps"": [ { ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} } ]
            }";

            Assert.Contains(ValidatorWithAllToolsAvailable().Validate(json).Errors, e => e.Path == "acceptance");
        }

        // --- Versi skema ----------------------------------------------------

        [Fact]
        public void schemaVersion_yang_salah_ditolak()
        {
            var json = Plan(steps: @"{ ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} }")
                .Replace(@"""schemaVersion"": ""1.0""", @"""schemaVersion"": ""2.0""");

            Assert.Contains(ValidatorWithAllToolsAvailable().Validate(json).Errors, e => e.Path == "schemaVersion");
        }
    }
}
