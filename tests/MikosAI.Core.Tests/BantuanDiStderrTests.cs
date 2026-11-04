using MikosAI.Core.Agents;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// `agy --help` menulis ke STDERR, dengan exit code 0.
    ///
    /// BAGAIMANA INI DITEMUKAN.
    ///
    /// Panel menampilkan jawaban kosong. Setelah stderr berhenti dibuang pada
    /// exit code 0, agy akhirnya bicara sendiri:
    ///
    ///   "a tool required the "mcp" permission that headless mode cannot
    ///    prompt for, so it was auto-denied"
    ///
    /// Padahal aturan izinnya SUDAH ditulis ke berkas, dan replika eksekusinya
    /// di luar SolidWorks berhasil. Bedanya: replika menuliskan --settings
    /// tanpa syarat, sedangkan provider hanya mengirimnya kalau probe
    /// menemukan flag itu di --help.
    ///
    /// Probe membaca stdout. Diukur pada agy 1.5.0: --help memberi 0 karakter
    /// di stdout dan 3323 karakter di stderr, exit code 0. Jadi probe melihat
    /// CLI tanpa flag sama sekali, --settings tidak pernah dikirim, dan setiap
    /// tool MCP ditolak otomatis.
    ///
    /// Kesalahannya bukan "lupa membaca stderr". Kesalahannya adalah membiarkan
    /// hasil deteksi yang MUSTAHIL - CLI tanpa flag apa pun - lewat sebagai
    /// keadaan normal. Lihat <see cref="ProviderProbe.DeteksiFlagGagal"/>.
    /// </summary>
    public class BantuanDiStderrTests
    {
        private const string BantuanAgy =
            "Usage of agy.exe:\n"
            + "  --dangerously-skip-permissions  Auto-approve all tool permission requests\n"
            + "  --output-format                 Output format for print mode (default text)\n"
            + "  -p                              Short alias for --print\n"
            + "  --print-timeout                 Optional time limit for print mode\n"
            + "  --settings                      Settings for this session only\n";

        /// <summary>
        /// Bentuk persis yang diukur: stdout kosong, bantuan di stderr.
        /// </summary>
        [Fact]
        public void flag_terdeteksi_meski_bantuan_hanya_di_stderr()
        {
            var flags = ProviderOutcome.ParseFlags(string.Empty, BantuanAgy);

            Assert.Contains("--settings", flags);
            Assert.Contains("--output-format", flags);
            Assert.Contains("--print-timeout", flags);
        }

        /// <summary>
        /// Dan bantuan di stdout harus tetap bekerja - ada build CLI yang
        /// memakai stdout, dan perbaikan ini tidak boleh menukar satu kasus
        /// dengan kasus lain.
        /// </summary>
        [Fact]
        public void bantuan_di_stdout_tetap_bekerja()
        {
            var flags = ProviderOutcome.ParseFlags(BantuanAgy, string.Empty);

            Assert.Contains("--settings", flags);
        }

        [Fact]
        public void bantuan_terpecah_di_kedua_stream_digabung()
        {
            var flags = ProviderOutcome.ParseFlags("  --output-format text\n", "  --settings <path>\n");

            Assert.Contains("--output-format", flags);
            Assert.Contains("--settings", flags);
        }

        [Fact]
        public void kedua_stream_kosong_tetap_kosong()
        {
            Assert.Empty(ProviderOutcome.ParseFlags(null, null));
            Assert.Empty(ProviderOutcome.ParseFlags(string.Empty, "   "));
        }

        /// <summary>
        /// CLI tanpa flag apa pun tidak ada. Kalau deteksi menghasilkan itu,
        /// yang gagal adalah deteksinya - dan itu harus bisa dibedakan dari
        /// "CLI ini memang tidak mendukung --settings".
        ///
        /// Tanpa pembedaan ini, kegagalan deteksi akan kembali muncul sebagai
        /// jawaban kosong, yaitu gejala yang sama yang baru saja kami lacak
        /// berhari-hari.
        /// </summary>
        [Fact]
        public void probe_tanpa_flag_sama_sekali_dinyatakan_gagal_mendeteksi()
        {
            var kosong = new ProviderProbe { Version = "1.5.0" };

            Assert.True(kosong.DeteksiFlagGagal);
            Assert.False(kosong.Supports("--settings"));
        }

        [Fact]
        public void probe_dengan_flag_tidak_dianggap_gagal_mendeteksi()
        {
            var terisi = new ProviderProbe
            {
                Version = "1.5.0",
                SupportedFlags = ProviderOutcome.ParseFlags(string.Empty, BantuanAgy),
            };

            Assert.False(terisi.DeteksiFlagGagal);
            Assert.True(terisi.Supports("--settings"));
        }
    }
}
