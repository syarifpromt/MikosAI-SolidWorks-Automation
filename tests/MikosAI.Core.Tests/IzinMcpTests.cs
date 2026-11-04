using System;
using System.Linq;
using MikosAI.Core.Agents;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Tanpa aturan izin, SETIAP tool kita ditolak agy di mode headless.
    ///
    /// BAGAIMANA INI DITEMUKAN.
    ///
    /// Round-trip MCP dari CLI lulus 10/10 setelah aturan izin ditambahkan ke
    /// scripts/mcp-roundtrip.ps1. Lalu pembangun perintah provider diperiksa -
    /// dan ia TIDAK mengirim aturan itu sama sekali.
    ///
    /// Artinya chat di panel akan menabrak dinding yang sama persis, dengan
    /// gejala yang jauh lebih sulit dibaca:
    ///
    ///   {"status":"SUCCESS","response":"",
    ///    "denied_actions":[{"action":"mcp","display_name":"CallMcpTool"}]}
    ///
    /// Exit code 0. Status SUCCESS. stdout TIDAK kosong, jadi DetectSilent tidak
    /// menyala. Panel akan menampilkan jawaban kosong tanpa penjelasan apa pun.
    ///
    /// Dua invarian dijaga di sini: aturannya DIKIRIM, dan penolakannya TIDAK
    /// pernah lolos sebagai "AI menjawab string kosong".
    /// </summary>
    public class IzinMcpTests
    {
        private static ProviderProbe ProbeLengkap()
        {
            return new ProviderProbe
            {
                SupportedFlags = new[] { "--settings", "--output-format", "--print-timeout", "--model" },
            };
        }

        // -------------------------------------------------------------------
        // Aturan izin dikirim
        // -------------------------------------------------------------------

        [Fact]
        public void argumen_memuat_settings_kalau_berkas_izin_diberikan()
        {
            var argumen = AntigravityCommand.BuatArgumen(
                new AgentRequest { Prompt = "halo", BerkasIzin = @"C:\tmp\agy-settings.json" },
                ProbeLengkap());

            var i = argumen.ToList().IndexOf("--settings");

            Assert.True(i >= 0, "Argumen tidak memuat --settings: " + string.Join(" ", argumen));
            Assert.Equal(@"C:\tmp\agy-settings.json", argumen[i + 1]);
        }

        /// <summary>
        /// Flag hanya dikirim kalau versi terpasang mengenalnya. Mengirim flag
        /// tak dikenal menggagalkan SELURUH permintaan, bukan cuma flag itu -
        /// aturan yang sudah berlaku untuk flag lain di pembangun ini.
        /// </summary>
        [Fact]
        public void settings_tidak_dikirim_kalau_versi_agy_tidak_mengenalnya()
        {
            var probe = new ProviderProbe { SupportedFlags = new[] { "--output-format" } };

            var argumen = AntigravityCommand.BuatArgumen(
                new AgentRequest { Prompt = "halo", BerkasIzin = @"C:\tmp\agy-settings.json" },
                probe);

            Assert.DoesNotContain("--settings", argumen);
        }

        [Fact]
        public void settings_tidak_dikirim_kalau_berkas_izin_tidak_ada()
        {
            var argumen = AntigravityCommand.BuatArgumen(
                new AgentRequest { Prompt = "halo", BerkasIzin = null },
                ProbeLengkap());

            Assert.DoesNotContain("--settings", argumen);
        }

        // -------------------------------------------------------------------
        // Bentuk aturannya
        // -------------------------------------------------------------------

        /// <summary>
        /// Garis miring itu yang menentukan.
        ///
        /// mcp(mikosai-solidworks) diterima sebagai JSON yang sah tapi TIDAK
        /// cocok dengan apa pun, jadi tool-nya tetap ditolak - tanpa satu pun
        /// keluhan soal aturannya. Bentuk benarnya diambil dari string di dalam
        /// agy.exe: mcp(&lt;server_name&gt;/&lt;tool_name&gt;).
        /// </summary>
        [Fact]
        public void aturan_izin_memakai_garis_miring_bukan_nama_server_saja()
        {
            var isi = AntigravityCommand.IsiBerkasIzin();

            Assert.Contains("mcp(" + AntigravityCommand.NamaServerMcp + "/*)", isi);

            // Bentuk tanpa garis miring tidak boleh muncul.
            Assert.DoesNotContain("mcp(" + AntigravityCommand.NamaServerMcp + ")", isi);
        }

        /// <summary>
        /// Lingkupnya server KITA, bukan mcp(*).
        ///
        /// Memberi izin lebih luas daripada yang dibutuhkan berarti menukar satu
        /// masalah dengan masalah yang lebih besar - dan di mode headless tidak
        /// ada manusia yang akan menolak permintaan berikutnya.
        /// </summary>
        [Fact]
        public void aturan_izin_tidak_memberi_izin_lebih_luas_daripada_yang_dibutuhkan()
        {
            var isi = AntigravityCommand.IsiBerkasIzin();

            Assert.DoesNotContain("mcp(*)", isi);
            Assert.DoesNotContain("dangerously", isi);

            // Hanya SATU aturan, supaya tidak ada yang menyelipkan izin kedua.
            var jumlah = isi.Split(new[] { "mcp(" }, StringSplitOptions.None).Length - 1;
            Assert.Equal(1, jumlah);
        }

        [Fact]
        public void berkas_izin_tidak_memuat_secret()
        {
            var isi = AntigravityCommand.IsiBerkasIzin();

            foreach (var kata in new[] { "Bearer", "token", "Authorization", "serverUrl" })
            {
                Assert.DoesNotContain(kata, isi, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void isi_berkas_izin_adalah_json_yang_sah()
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(AntigravityCommand.IsiBerkasIzin());

            var allow = node!["permissions"]!["allow"]!.AsArray();

            Assert.Single(allow);
        }

        // -------------------------------------------------------------------
        // Penolakan tidak lolos sebagai jawaban kosong
        // -------------------------------------------------------------------

        /// <summary>
        /// Jawaban nyata dari percobaan pertama, apa adanya.
        /// </summary>
        private const string JawabanDitolak =
            "{\"conversation_id\":\"78726cd9\",\"status\":\"SUCCESS\",\"response\":\"\","
            + "\"duration_seconds\":13.57,\"num_turns\":1,"
            + "\"denied_actions\":[{\"action\":\"mcp\",\"display_name\":\"CallMcpTool\"}]}";

        [Fact]
        public void izin_ditolak_dilaporkan_sebagai_kegagalan_bukan_jawaban_kosong()
        {
            var hasil = ProviderOutcome.Shape(JawabanDitolak, 0);

            Assert.NotNull(hasil.Failure);
            Assert.Equal("provider_permission_denied", hasil.Failure!.Code);

            // Pesannya harus menyebut APA yang ditolak, bukan cuma "gagal".
            Assert.Contains("CallMcpTool", hasil.Failure.Message);

            // Dan harus menyatakan bahwa ini BUKAN kesalahan server MCP kita,
            // karena itu tempat pertama yang akan dicurigai orang.
            Assert.Contains("MCP kita", hasil.Failure.Message);

            Assert.False(string.IsNullOrWhiteSpace(hasil.Failure.NextAction));
        }

        /// <summary>
        /// DetectSilent TIDAK bisa menangkap kasus ini, dan itu sebabnya
        /// pemeriksaan denied_actions harus ada.
        ///
        /// Test ini menegaskan premisnya: kalau suatu hari DetectSilent ikut
        /// menangkapnya, pemeriksaan ganda tidak merugikan - tapi mengandalkan
        /// DetectSilent saja akan salah.
        /// </summary>
        [Fact]
        public void DetectSilent_tidak_menangkapnya_karena_stdout_tidak_kosong()
        {
            Assert.Null(ProviderOutcome.DetectSilent(0, JawabanDitolak));
        }

        [Fact]
        public void jawaban_normal_tanpa_denied_actions_tetap_sukses()
        {
            var hasil = ProviderOutcome.Shape(
                "{\"status\":\"SUCCESS\",\"response\":\"Dokumen aktif: Part1.SLDPRT\"}", 0);

            Assert.Null(hasil.Failure);
            Assert.Contains("Part1.SLDPRT", hasil.Summary);
        }

        /// <summary>
        /// denied_actions kosong bukan penolakan. Array kosong harus dibedakan
        /// dari array berisi, kalau tidak setiap jawaban normal akan dilaporkan
        /// gagal.
        /// </summary>
        [Fact]
        public void denied_actions_kosong_bukan_penolakan()
        {
            var hasil = ProviderOutcome.Shape(
                "{\"status\":\"SUCCESS\",\"response\":\"beres\",\"denied_actions\":[]}", 0);

            Assert.Null(hasil.Failure);
        }
    }
}
