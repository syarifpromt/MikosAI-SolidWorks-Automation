using System.Linq;
using MikosAI.Core.Agents;
using Xunit;

namespace MikosAI.Core.Tests
{
    public class ProviderStateTests
    {
        [Theory]
        [InlineData(ProviderState.NotInstalled, false)]
        [InlineData(ProviderState.Installed, false)]
        [InlineData(ProviderState.AuthFailed, false)]
        [InlineData(ProviderState.AuthUnverified, true)]
        [InlineData(ProviderState.AuthVerified, true)]
        [InlineData(ProviderState.ExecutionVerified, true)]
        public void hanya_keadaan_terautentikasi_yang_boleh_mencoba_permintaan(ProviderState state, bool boleh)
        {
            // AuthUnverified ikut diizinkan dengan sengaja: beberapa CLI tidak
            // punya cara murah memeriksa login, dan permintaan pertama itulah
            // yang memverifikasinya.
            Assert.Equal(boleh, ProviderStateRules.MayAttemptRequest(state));
        }

        [Theory]
        [InlineData(ProviderState.Installed)]
        [InlineData(ProviderState.AuthUnverified)]
        public void terpasang_dan_belum_terverifikasi_TIDAK_boleh_disebut_terhubung(ProviderState state)
        {
            // CLI ada di PATH tidak membuktikan permintaan ke model bisa jalan.
            // Menampilkannya sebagai terhubung adalah kebohongan yang akan
            // membingungkan pengguna saat permintaan pertama gagal.
            Assert.False(ProviderStateRules.MayBeShownAsConnected(state));
        }

        [Theory]
        [InlineData(ProviderState.AuthVerified)]
        [InlineData(ProviderState.ExecutionVerified)]
        public void hanya_yang_terbukti_boleh_disebut_terhubung(ProviderState state)
        {
            Assert.True(ProviderStateRules.MayBeShownAsConnected(state));
        }

        [Fact]
        public void keadaan_tak_dikenal_tidak_pernah_tampak_siap()
        {
            var takDikenal = (ProviderState)99;

            Assert.False(ProviderStateRules.MayAttemptRequest(takDikenal));
            Assert.False(ProviderStateRules.MayBeShownAsConnected(takDikenal));
            Assert.Contains("tidak dikenali", ProviderStateRules.Describe(takDikenal, "X"));
        }

        [Fact]
        public void keadaan_yang_butuh_tindakan_manusia_menyebutkan_tindakannya()
        {
            Assert.NotNull(ProviderStateRules.NextAction(ProviderState.NotInstalled, "Antigravity"));
            Assert.NotNull(ProviderStateRules.NextAction(ProviderState.Installed, "Antigravity"));
            Assert.NotNull(ProviderStateRules.NextAction(ProviderState.AuthFailed, "Antigravity"));

            Assert.Null(ProviderStateRules.NextAction(ProviderState.AuthVerified, "Antigravity"));
        }

        [Fact]
        public void deskripsi_tidak_memuat_jargon_internal()
        {
            // Pengguna Lapis 1 tidak perlu tahu istilah AuthUnverified.
            foreach (ProviderState state in System.Enum.GetValues(typeof(ProviderState)))
            {
                var teks = ProviderStateRules.Describe(state, "Antigravity");

                Assert.DoesNotContain("AuthUnverified", teks);
                Assert.DoesNotContain("NotInstalled", teks);
                Assert.DoesNotContain("ExecutionVerified", teks);
            }
        }
    }

    public class ProviderOutcomeTests
    {
        // ====================================================================
        // Provider yang diam - aturan paling penting
        // ====================================================================

        [Fact]
        public void exit_0_dengan_stdout_kosong_BUKAN_sukses()
        {
            // `agy -p` dilaporkan diam total ketika stdout bukan terminal.
            // Mode kegagalan terburuk: kelihatan seperti berhasil.
            var gagal = ProviderOutcome.DetectSilent(0, string.Empty);

            Assert.NotNull(gagal);
            Assert.Equal("provider_silent", gagal!.Code);
            Assert.Contains("BUKAN jawaban AI yang kosong", gagal.Message);
            Assert.Contains("ConPTY", gagal.NextAction);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\r\n")]
        [InlineData(null)]
        public void stdout_yang_hanya_spasi_juga_dianggap_diam(string? stdout)
        {
            Assert.NotNull(ProviderOutcome.DetectSilent(0, stdout));
        }

        [Fact]
        public void stdout_berisi_dengan_exit_0_bukan_diam()
        {
            Assert.Null(ProviderOutcome.DetectSilent(0, "jawaban"));
        }

        [Fact]
        public void exit_bukan_0_bukan_kasus_diam_walaupun_kosong()
        {
            // Itu kegagalan biasa, bukan kelas provider_silent.
            Assert.Null(ProviderOutcome.DetectSilent(1, string.Empty));
        }

        [Fact]
        public void Shape_mengembalikan_kegagalan_provider_silent()
        {
            var hasil = ProviderOutcome.Shape(string.Empty, 0);

            Assert.False(hasil.IsSuccessful);
            Assert.Equal("provider_silent", hasil.Failure!.Code);
        }

        // ====================================================================
        // Pembersihan keluaran pseudo-terminal
        // ====================================================================

        [Fact]
        public void kode_ANSI_dibuang()
        {
            var mentah = "\u001b[32mhijau\u001b[0m dan \u001b[1mtebal\u001b[0m";

            Assert.Equal("hijau dan tebal", ProviderOutcome.Bersihkan(mentah));
        }

        [Fact]
        public void repaint_carriage_return_hanya_menyisakan_segmen_terakhir()
        {
            // Terminal menimpa baris; yang relevan hanya hasil akhirnya.
            var mentah = "memuat...\rmemuat 50%\rselesai";

            Assert.Equal("selesai", ProviderOutcome.Bersihkan(mentah));
        }

        [Fact]
        public void baris_kosong_dibuang()
        {
            Assert.Equal("a\nb", ProviderOutcome.Bersihkan("a\n\n\nb\n\n"));
        }

        [Fact]
        public void JSON_tetap_utuh_setelah_dibersihkan()
        {
            // Pembersihan tidak boleh merusak muatan yang sebenarnya dicari.
            var mentah = "\u001b[90m[info]\u001b[0m\n{\"summary\":\"selesai\"}";
            var bersih = ProviderOutcome.Bersihkan(mentah);

            Assert.Contains("{\"summary\":\"selesai\"}", bersih);
        }

        // ====================================================================
        // Pembentukan hasil
        // ====================================================================

        [Fact]
        public void envelope_JSON_lengkap_dipetakan_ke_protokol_terpadu()
        {
            var json = @"{
              ""summary"": ""Membuat mounting plate"",
              ""changedFiles"": [""plate.sldprt""],
              ""verification"": [""rebuild bersih""],
              ""risks"": [""material belum diisi""],
              ""nextSteps"": [""tambahkan fillet""]
            }";

            var hasil = ProviderOutcome.Shape(json, 0);

            Assert.True(hasil.IsSuccessful);
            Assert.Equal("Membuat mounting plate", hasil.Summary);
            Assert.Equal(new[] { "plate.sldprt" }, hasil.ChangedFiles);
            Assert.Single(hasil.Risks);
            Assert.Single(hasil.NextSteps);
        }

        [Theory]
        [InlineData(@"{""response"":""halo""}")]
        [InlineData(@"{""text"":""halo""}")]
        [InlineData(@"{""message"":""halo""}")]
        [InlineData(@"{""output"":""halo""}")]
        public void beberapa_nama_field_jawaban_dicoba(string json)
        {
            // Nama field untuk isi jawaban BELUM terkonfirmasi di dokumentasi
            // resmi Antigravity, jadi kandidatnya dicoba berurutan.
            Assert.Equal("halo", ProviderOutcome.Shape(json, 0).Summary);
        }

        [Fact]
        public void structured_output_ikut_diperiksa()
        {
            var json = @"{""status"":""ok"",""structured_output"":{""summary"":""dari skema""}}";

            Assert.Equal("dari skema", ProviderOutcome.Shape(json, 0).Summary);
        }

        [Fact]
        public void teks_biasa_jadi_summary()
        {
            // Sah - tidak semua versi mendukung keluaran JSON.
            var hasil = ProviderOutcome.Shape("Ini jawaban biasa.", 0);

            Assert.True(hasil.IsSuccessful);
            Assert.Equal("Ini jawaban biasa.", hasil.Summary);
        }

        [Fact]
        public void dari_JSONL_baris_yang_memuat_field_protokol_yang_dipakai()
        {
            // stream-json mengeluarkan banyak peristiwa; hasil akhirnya yang
            // dicari, bukan progres di tengah.
            var jsonl = string.Join("\n", new[]
            {
                @"{""type"":""progress"",""step"":1}",
                @"{""type"":""progress"",""step"":2}",
                @"{""summary"":""selesai"",""changedFiles"":[]}",
            });

            Assert.Equal("selesai", ProviderOutcome.Shape(jsonl, 0).Summary);
        }

        [Fact]
        public void plan_dipertahankan_sebagai_JSON()
        {
            var json = @"{""summary"":""ok"",""plan"":{""schemaVersion"":""1.0"",""title"":""Uji""}}";

            var hasil = ProviderOutcome.Shape(json, 0);

            Assert.NotNull(hasil.Plan);
            Assert.Equal("1.0", hasil.Plan!["schemaVersion"]!.GetValue<string>());
        }

        [Fact]
        public void daftar_berisi_objek_diringkas_jadi_teks_bukan_dibuang()
        {
            var json = @"{""summary"":""ok"",""verification"":[{""command"":""rebuild"",""status"":""pass""}]}";

            var hasil = ProviderOutcome.Shape(json, 0);

            Assert.Single(hasil.Verification);
            Assert.Contains("rebuild", hasil.Verification[0]);
        }

        // ====================================================================
        // Kegagalan
        // ====================================================================

        [Fact]
        public void exit_bukan_0_menjadi_kegagalan_dengan_keluaran_dipertahankan()
        {
            var hasil = ProviderOutcome.Shape("sesuatu meledak", 1);

            Assert.False(hasil.IsSuccessful);
            Assert.Equal("provider_failed", hasil.Failure!.Code);
            Assert.Equal("sesuatu meledak", hasil.Summary);
        }

        [Theory]
        [InlineData("Authentication required", "login")]
        [InlineData("Error: rate limit exceeded", "Kuota")]
        [InlineData("flags provided but not defined: -output-format", "--help")]
        public void pesan_error_yang_dikenali_memberi_tindakan_perbaikan(string pesan, string diharapkan)
        {
            var tindakan = ProviderOutcome.TebakTindakan(pesan);

            Assert.NotNull(tindakan);
            Assert.Contains(diharapkan, tindakan);
        }

        [Fact]
        public void pesan_error_tak_dikenal_tidak_mengarang_tindakan()
        {
            // Lebih baik diam daripada mengarahkan ke jalan yang salah.
            Assert.Null(ProviderOutcome.TebakTindakan("galat yang aneh sekali"));
        }

        // ====================================================================
        // Parsing flag dari --help
        // ====================================================================

        [Fact]
        public void flag_dibaca_dari_keluaran_help()
        {
            var help = @"
Usage: agy [options]

  -p, --print <text>        Jalankan satu prompt
      --output-format <fmt> text, json, atau stream-json
      --json-schema <file>  Paksa mengikuti skema
      --dangerously-skip-permissions
";

            var flags = ProviderOutcome.ParseFlags(help);

            Assert.Contains("--output-format", flags);
            Assert.Contains("--json-schema", flags);
            Assert.Contains("-p", flags);
            Assert.Contains("--print", flags);
        }

        [Fact]
        public void flag_yang_tidak_ada_di_help_tidak_dilaporkan_ada()
        {
            // Inilah yang mencegah kita mengirim flag yang ditolak versi
            // terpasang - ada build agy yang menolak --output-format.
            var flags = ProviderOutcome.ParseFlags("Usage: agy [options]\n  -p, --print <text>");

            Assert.DoesNotContain("--output-format", flags);
        }

        [Fact]
        public void probe_Supports_tidak_peduli_huruf_besar_kecil()
        {
            var probe = new ProviderProbe { SupportedFlags = new[] { "--output-format" } };

            Assert.True(probe.Supports("--OUTPUT-FORMAT"));
            Assert.False(probe.Supports("--stream"));
        }

        [Fact]
        public void help_kosong_menghasilkan_daftar_kosong_bukan_exception()
        {
            Assert.Empty(ProviderOutcome.ParseFlags(null));
            Assert.Empty(ProviderOutcome.ParseFlags(string.Empty));
        }
    }
}
