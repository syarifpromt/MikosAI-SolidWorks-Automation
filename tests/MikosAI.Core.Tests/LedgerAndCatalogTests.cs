using System;
using System.IO;
using System.Linq;
using MikosAI.Core.Capabilities;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Test ini memuat capabilities.yaml dan tools.json yang SEBENARNYA dari akar
    /// repo. Kalau kedua artefak itu jadi tidak konsisten, test ini merah.
    /// </summary>
    public class LedgerAndCatalogTests
    {
        private static CapabilityLedger Ledger() => CapabilityLedger.Load(RepoFiles.CapabilitiesYaml);

        private static ToolCatalog Catalog() => ToolCatalog.Load(RepoFiles.ToolsJson, Ledger());

        // --- Memuat artefak nyata -------------------------------------------

        [Fact]
        public void capabilities_yaml_bisa_dimuat()
        {
            var ledger = Ledger();

            Assert.Equal("1.0", ledger.SchemaVersion);
            Assert.NotEmpty(ledger.Capabilities);
            Assert.All(ledger.Capabilities, c => Assert.False(string.IsNullOrWhiteSpace(c.Id)));
        }

        [Fact]
        public void tools_json_memuat_18_tool_CAD_dan_2_orchestration()
        {
            Assert.Equal(20, Catalog().All.Count);
        }

        [Fact]
        public void katalog_dan_ledger_konsisten()
        {
            // Setiap tool merujuk kemampuan yang ada, dan setiap tool yang
            // disebut ledger terdefinisi di katalog.
            var problems = Catalog().Validate();

            Assert.True(
                problems.Count == 0,
                "Katalog dan ledger tidak konsisten:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems));
        }

        // --- Keadaan jujur saat ini -----------------------------------------

        [Fact]
        public void belum_ada_kemampuan_yang_verified_karena_belum_ada_golden_test_yang_lulus()
        {
            // Ini test yang MEMANG harus merah nanti - saat kemampuan pertama
            // benar-benar lulus golden test di SolidWorks nyata, ubah test ini.
            // Sampai hari itu, mengaku verified adalah berbohong.
            var ledger = Ledger();

            Assert.Empty(ledger.VerifiedSolidWorksVersions);
            Assert.DoesNotContain(ledger.Capabilities, c => c.Status == CapabilityStatus.Verified);
        }

        [Fact]
        public void hanya_kemampuan_read_only_yang_sudah_dipromosikan_ke_pilot()
        {
            // document_inspection berstatus pilot: logikanya sudah diuji
            // otomatis, penerjemahan API-nya terbukti di SW2025. Semua
            // kemampuan yang MENULIS masih not_implemented.
            var ledger = Ledger();

            var dipromosikan = ledger.Capabilities
                .Where(c => c.IsExposable)
                .Select(c => c.Id)
                .ToList();

            Assert.Equal(new[] { "document_inspection" }, dipromosikan);
        }

        [Fact]
        public void TIDAK_ADA_tool_yang_menulis_yang_diekspos()
        {
            // Invarian terpenting di ledger. Tool read-only yang gagal tidak
            // bisa merusak pekerjaan pengguna; tool yang menulis bisa.
            var menulisTapiTerbuka = Catalog().ExposableTools()
                .Where(t => t.Mutates)
                .Select(t => t.Name)
                .ToList();

            Assert.Empty(menulisTapiTerbuka);
        }

        [Fact]
        public void keenam_tool_read_only_diekspos()
        {
            Assert.Equal(
                new[]
                {
                    "get_active_document", "get_bounding_box", "get_mass_properties",
                    "get_selection", "inspect_sketches", "list_features",
                },
                Catalog().ExposableTools().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        }

        // --- Gating ---------------------------------------------------------

        [Fact]
        public void status_yang_tidak_dikenal_menjadi_NotImplemented_bukan_Verified()
        {
            // Salah tulis di YAML harus membuat kemampuan HILANG, bukan muncul
            // sebagai sudah teruji.
            var yaml = @"
schema_version: '1.0'
verified_versions:
  solidworks: []
capabilities:
  - id: salah_tulis
    label: Status salah ketik
    status: verfied
    tools: [sesuatu]
";

            var ledger = CapabilityLedger.Parse(yaml);
            var capability = ledger.Find("salah_tulis");

            Assert.NotNull(capability);
            Assert.Equal(CapabilityStatus.NotImplemented, capability!.Status);
            Assert.False(capability.IsExposable);
            Assert.False(ledger.IsToolExposable("sesuatu"));
        }

        [Fact]
        public void pilot_selalu_butuh_review_manusia_walaupun_yaml_bilang_tidak()
        {
            var yaml = @"
schema_version: '1.0'
capabilities:
  - id: coba_pilot
    status: pilot
    review_required: false
    tools: [tool_pilot]
";

            var capability = CapabilityLedger.Parse(yaml).Find("coba_pilot");

            Assert.NotNull(capability);
            Assert.True(capability!.IsExposable);
            Assert.True(capability.EffectiveReviewRequired);
        }

        [Fact]
        public void verified_dan_pilot_diekspos_sisanya_tidak()
        {
            var yaml = @"
schema_version: '1.0'
capabilities:
  - id: a
    status: verified
    tools: [tool_a]
  - id: b
    status: pilot
    tools: [tool_b]
  - id: c
    status: reference_only
    tools: [tool_c]
  - id: d
    status: not_implemented
    tools: [tool_d]
";

            var ledger = CapabilityLedger.Parse(yaml);

            Assert.True(ledger.IsToolExposable("tool_a"));
            Assert.True(ledger.IsToolExposable("tool_b"));
            Assert.False(ledger.IsToolExposable("tool_c"));
            Assert.False(ledger.IsToolExposable("tool_d"));

            Assert.Equal(new[] { "tool_a", "tool_b" }, ledger.ExposableToolNames());
        }

        [Fact]
        public void tool_yang_tidak_terdaftar_tidak_boleh_dipanggil()
        {
            // Bukan karena diblokir - karena kita tidak punya dasar
            // untuk mengizinkannya.
            Assert.False(Ledger().IsToolExposable("tool_yang_tidak_pernah_ada"));
        }

        [Fact]
        public void satu_tool_tidak_boleh_dimiliki_dua_kemampuan()
        {
            // Kalau ambigu, gating bisa dimanfaatkan untuk melewatinya -
            // pilih kemampuan yang statusnya lebih longgar.
            var yaml = @"
schema_version: '1.0'
capabilities:
  - id: ketat
    status: not_implemented
    tools: [tool_bersama]
  - id: longgar
    status: verified
    tools: [tool_bersama]
";

            var exception = Assert.Throws<InvalidDataException>(() => CapabilityLedger.Parse(yaml));
            Assert.Contains("tool_bersama", exception.Message);
        }

        // --- Konsistensi katalog --------------------------------------------

        [Fact]
        public void nama_tool_duplikat_di_katalog_ditolak()
        {
            var json = @"{
              ""tools"": [
                { ""name"": ""dua_kali"", ""capabilityId"": ""x"", ""mutates"": false },
                { ""name"": ""dua_kali"", ""capabilityId"": ""x"", ""mutates"": false }
              ]
            }";

            var exception = Assert.Throws<InvalidDataException>(
                () => ToolCatalog.Parse(json, CapabilityLedger.Parse("schema_version: '1.0'")));

            Assert.Contains("dua_kali", exception.Message);
        }

        [Fact]
        public void operasi_tulis_tanpa_daftar_verifikasi_ditolak_saat_diekspos()
        {
            // Mengekspos tool yang menulis tanpa cara membuktikan hasilnya benar
            // adalah justru hal yang paling ingin kita cegah.
            var yaml = @"
schema_version: '1.0'
capabilities:
  - id: nekat
    status: verified
    tools: [tulis_tanpa_bukti]
";

            var json = @"{
              ""tools"": [
                { ""name"": ""tulis_tanpa_bukti"", ""capabilityId"": ""nekat"", ""mutates"": true }
              ]
            }";

            var problems = ToolCatalog.Parse(json, CapabilityLedger.Parse(yaml)).Validate();

            Assert.Contains(problems, p => p.Contains("verification"));
        }

        [Fact]
        public void setiap_tool_v01_punya_capabilityId_dan_deskripsi()
        {
            foreach (var tool in Catalog().All)
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.CapabilityId), $"Tool '{tool.Name}' tanpa capabilityId.");
                Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"Tool '{tool.Name}' tanpa description.");
            }
        }

        [Fact]
        public void ada_7_tool_read_only_dan_13_tool_yang_menulis()
        {
            // 6 tool CAD read-only + validate_design_plan.
            // 12 tool CAD yang menulis + run_design_plan.
            var all = Catalog().All;

            Assert.Equal(7, all.Count(t => !t.Mutates));
            Assert.Equal(13, all.Count(t => t.Mutates));
        }
    }
}
