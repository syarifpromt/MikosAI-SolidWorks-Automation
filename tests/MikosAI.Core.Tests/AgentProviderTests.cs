using System;
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

            // Dulu baris ini menuntut nasihat "ConPTY" ADA di NextAction.
            //
            // Test itu MENGABADIKAN NASIHAT BASI. Item 2 di daftar verifikasi
            // sudah dijawab: pipe biasa cukup di agy 1.4.11+, jadi menyuruh
            // orang memasang pseudo-terminal mengirim mereka memperbaiki hal
            // yang tidak rusak.
            //
            // Yang dituntut sekarang adalah sifat yang benar-benar berguna:
            // langkah berikutnya harus KONKRET, bukan sekadar ada.
            Assert.False(string.IsNullOrWhiteSpace(gagal.NextAction));
            Assert.DoesNotContain("ConPTY", gagal.NextAction!, StringComparison.OrdinalIgnoreCase);
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

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Test terhadap keluaran NYATA agy 1.4.11, ditangkap dari mesin bermesin
    /// SolidWorks pada 2026-11-01. Bukan payload karangan.
    ///
    /// Semua asumsi di docs/integrasi-antigravity.md sebelumnya berasal dari
    /// riset web pihak ketiga. Yang di bawah ini fakta.
    /// </summary>
    public class AntigravityNyataTests
    {
        // Ditangkap dari: agy -p "..." --output-format json
        private const string EnvelopeJson =
            @"{""conversation_id"":""1e58604b-b51f-4e65-875c-5f602011508b"",""status"":""SUCCESS"","
            + @"""response"":""merah\n"",""duration_seconds"":3.9414694,""num_turns"":1,"
            + @"""usage"":{""input_tokens"":6828,""output_tokens"":6,""thinking_tokens"":0,"
            + @"""cache_read_tokens"":0,""total_tokens"":6834}}";

        [Fact]
        public void envelope_json_nyata_terbaca_dan_field_jawabannya_response()
        {
            // Nama field ini sebelumnya hanya dugaan dari panduan pihak ketiga.
            // Sekarang terkonfirmasi.
            var hasil = MikosAI.Core.Agents.ProviderOutcome.Shape(EnvelopeJson, 0);

            Assert.True(hasil.IsSuccessful);
            Assert.Equal("merah", hasil.Summary);
        }

        [Fact]
        public void baris_newline_di_akhir_jawaban_dipangkas()
        {
            // agy mengakhiri response dengan \n. Membiarkannya membuat chat
            // punya baris kosong menggantung di setiap jawaban.
            var hasil = MikosAI.Core.Agents.ProviderOutcome.Shape(EnvelopeJson, 0);

            Assert.DoesNotContain("\n", hasil.Summary);
        }

        [Fact]
        public void status_selain_SUCCESS_menjadi_kegagalan()
        {
            var json = EnvelopeJson.Replace(@"""status"":""SUCCESS""", @"""status"":""ERROR""");

            var hasil = MikosAI.Core.Agents.ProviderOutcome.Shape(json, 0);

            Assert.False(hasil.IsSuccessful);
            Assert.Equal("provider_reported_failure", hasil.Failure!.Code);
        }

        [Fact]
        public void status_yang_tidak_dikenal_juga_menjadi_kegagalan()
        {
            // Default aman, sama seperti di VerificationGate: status yang tidak
            // kita mengerti tidak boleh lolos sebagai berhasil.
            var json = EnvelopeJson.Replace(@"""status"":""SUCCESS""", @"""status"":""PARTIAL""");

            Assert.False(MikosAI.Core.Agents.ProviderOutcome.Shape(json, 0).IsSuccessful);
        }

        // Ditangkap dari: agy -p "..." --output-format stream-json
        private static readonly string[] BarisStream = new[]
        {
            @"{""event"":""init"",""conversation_id"":""719446a4"",""init"":{""cwd"":""D:\x"",""tools"":[""ask_question""]}}",
            @"{""event"":""step_update"",""step_update"":{""step_index"":0,""state"":""DONE"",""step_type"":""user_input""}}",
            @"{""event"":""step_update"",""step_update"":{""step_index"":1,""state"":""ACTIVE"",""step_type"":""agent_response"",""text_delta"":""Merah\nBir""}}",
            @"{""event"":""step_update"",""step_update"":{""step_index"":1,""state"":""ACTIVE"",""step_type"":""agent_response"",""text_delta"":""u""}}",
            @"{""event"":""step_update"",""step_update"":{""step_index"":1,""state"":""DONE"",""step_type"":""agent_response"",""text_delta"":""\n""}}",
            @"{""event"":""result"",""result"":{""conversation_id"":""719446a4"",""status"":""SUCCESS"",""response"":""Merah\nBiru\n"",""num_turns"":1}}",
        };

        [Fact]
        public void potongan_teks_bertahap_terbaca_dari_stream_json()
        {
            // Inilah yang membuat chat terasa hidup alih-alih diam lalu
            // tiba-tiba selesai.
            var potongan = BarisStream
                .Select(MikosAI.Core.Agents.ProviderOutcome.AmbilPotonganTeks)
                .Where(p => p is not null)
                .ToList();

            Assert.Equal(new[] { "Merah\nBir", "u", "\n" }, potongan);
            Assert.Equal("Merah\nBiru\n", string.Concat(potongan));
        }

        [Fact]
        public void peristiwa_user_input_TIDAK_ditampilkan_sebagai_jawaban()
        {
            // Itu gema prompt kita sendiri.
            var barisUserInput = BarisStream[1];

            Assert.Null(MikosAI.Core.Agents.ProviderOutcome.AmbilPotonganTeks(barisUserInput));
        }

        [Fact]
        public void peristiwa_init_bukan_teks_jawaban()
        {
            Assert.Null(MikosAI.Core.Agents.ProviderOutcome.AmbilPotonganTeks(BarisStream[0]));
        }

        [Fact]
        public void hasil_akhir_diambil_dari_envelope_event_result()
        {
            // Hasil akhir bersarang di bawah "result", bukan di tingkat atas -
            // itu yang membedakannya dari envelope --output-format json.
            var seluruh = string.Join("\n", BarisStream);

            var hasil = MikosAI.Core.Agents.ProviderOutcome.Shape(seluruh, 0);

            Assert.True(hasil.IsSuccessful);
            Assert.Equal("Merah\nBiru", hasil.Summary);
        }

        [Fact]
        public void peristiwa_progres_tidak_salah_dianggap_hasil_akhir()
        {
            // Kalau baris step_update terakhir yang diambil, jawabannya akan
            // terpotong jadi "\n".
            var tanpaResult = string.Join("\n", BarisStream.Take(5));

            var hasil = MikosAI.Core.Agents.ProviderOutcome.Shape(tanpaResult, 0);

            // Tidak ada hasil akhir: tidak boleh mengarang jawaban dari progres.
            Assert.NotEqual("Merah\nBiru", hasil.Summary);
        }

        [Fact]
        public void flag_nyata_agy_1_4_11_terbaca_dari_help()
        {
            // Dipangkas dari keluaran agy --help yang sebenarnya.
            var help = @"Usage of agy.exe:
  --dangerously-skip-permissions  Auto-approve all tool permission requests
  --json-schema                   Optional JSON schema string or path
  --output-format                 Output format for print mode (text, json, stream-json)
  -p                              Short alias for --print
  --print                         Run a single prompt non-interactively
  --print-timeout                 Optional time limit for print mode
  --model                         Model for the current CLI session";

            var flags = MikosAI.Core.Agents.ProviderOutcome.ParseFlags(help);

            Assert.Contains("--output-format", flags);
            Assert.Contains("--print-timeout", flags);
            Assert.Contains("--json-schema", flags);
            Assert.Contains("-p", flags);
        }
    }
}

namespace MikosAI.Core.Tests
{
    public class AntigravityCommandTests
    {
        private static MikosAI.Core.Agents.ProviderProbe ProbePenuh()
            => new MikosAI.Core.Agents.ProviderProbe
            {
                State = MikosAI.Core.Agents.ProviderState.AuthUnverified,
                Version = "1.4.12",
                SupportedFlags = new[]
                {
                    "-p", "--print", "--output-format", "--print-timeout", "--model", "--json-schema",
                },
            };

        [Fact]
        public void prompt_langsung_menyusul_minus_p()
        {
            // Urutan ini terverifikasi bekerja terhadap agy 1.4.11.
            var argumen = MikosAI.Core.Agents.AntigravityCommand.BuatArgumen(
                new MikosAI.Core.Agents.AgentRequest { Prompt = "buat pelat" }, ProbePenuh());

            Assert.Equal("-p", argumen[0]);
            Assert.Equal("buat pelat", argumen[1]);
        }

        [Fact]
        public void stream_json_dipilih_kalau_minta_progres_bertahap()
        {
            var argumen = MikosAI.Core.Agents.AntigravityCommand.BuatArgumen(
                new MikosAI.Core.Agents.AgentRequest { Prompt = "x", PreferStreaming = true }, ProbePenuh());

            var i = argumen.ToList().IndexOf("--output-format");
            Assert.Equal("stream-json", argumen[i + 1]);
        }

        [Fact]
        public void json_biasa_dipilih_kalau_tidak_minta_progres()
        {
            var argumen = MikosAI.Core.Agents.AntigravityCommand.BuatArgumen(
                new MikosAI.Core.Agents.AgentRequest { Prompt = "x", PreferStreaming = false }, ProbePenuh());

            var i = argumen.ToList().IndexOf("--output-format");
            Assert.Equal("json", argumen[i + 1]);
        }

        [Fact]
        public void flag_yang_TIDAK_ada_di_help_tidak_pernah_dikirim()
        {
            // Ada build agy yang menolak --output-format, dan flag tak dikenal
            // menggagalkan seluruh permintaan.
            var probeMinim = new MikosAI.Core.Agents.ProviderProbe
            {
                SupportedFlags = new[] { "-p", "--print" },
            };

            var argumen = MikosAI.Core.Agents.AntigravityCommand.BuatArgumen(
                new MikosAI.Core.Agents.AgentRequest { Prompt = "x", Model = "gemini" }, probeMinim);

            Assert.DoesNotContain("--output-format", argumen);
            Assert.DoesNotContain("--print-timeout", argumen);
            Assert.DoesNotContain("--model", argumen);
            Assert.Equal(new[] { "-p", "x" }, argumen);
        }

        [Fact]
        public void batas_waktu_selalu_dikirim_kalau_didukung()
        {
            // 0 berarti menunggu selamanya, dan permintaan yang menggantung di
            // dalam SolidWorks menyisakan proses anak selamanya.
            var argumen = MikosAI.Core.Agents.AntigravityCommand.BuatArgumen(
                new MikosAI.Core.Agents.AgentRequest { Prompt = "x" }, ProbePenuh());

            var i = argumen.ToList().IndexOf("--print-timeout");
            Assert.True(i > 0);
            Assert.EndsWith("s", argumen[i + 1]);
        }

        // --- Pendaftaran MCP -------------------------------------------------

        [Fact]
        public void daftar_mcp_memakai_header_bearer()
        {
            var argumen = MikosAI.Core.Agents.AntigravityCommand.BuatArgumenDaftarMcp(
                new System.Uri("http://127.0.0.1:45678/mcp"), "token-rahasia");

            Assert.Equal(new[]
            {
                "mcp", "add",
                "--header", "Authorization: Bearer token-rahasia",
                "mikosai-solidworks", "http://127.0.0.1:45678/mcp",
            }, argumen);
        }

        [Fact]
        public void flag_mendahului_nama_server()
        {
            // agy menolak flag yang ditaruh setelah nama server.
            var argumen = MikosAI.Core.Agents.AntigravityCommand.BuatArgumenDaftarMcp(
                new System.Uri("http://127.0.0.1:1/mcp"), "t").ToList();

            Assert.True(argumen.IndexOf("--header") < argumen.IndexOf("mikosai-solidworks"));
        }

        [Fact]
        public void endpoint_non_loopback_ditolak()
        {
            // Jaring pengaman yang sama seperti di AgentWorkspace.
            Assert.Throws<System.ArgumentException>(() =>
                MikosAI.Core.Agents.AntigravityCommand.BuatArgumenDaftarMcp(
                    new System.Uri("http://192.168.1.10:8765/mcp"), "t"));
        }

        [Fact]
        public void token_kosong_ditolak()
        {
            Assert.Throws<System.ArgumentException>(() =>
                MikosAI.Core.Agents.AntigravityCommand.BuatArgumenDaftarMcp(
                    new System.Uri("http://127.0.0.1:1/mcp"), "  "));
        }
    }
}
