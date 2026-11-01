using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MikosAI.Core.Agents
{
    /// <summary>
    /// Aturan yang menentukan apakah keluaran provider bisa dipercaya, dan
    /// bagaimana membentuknya jadi <see cref="AgentResult"/>.
    ///
    /// Semuanya di Core supaya bisa diuji tanpa memasang CLI apa pun. Adapter
    /// provider di add-in hanya menjalankan proses dan menyerahkan keluarannya
    /// ke sini.
    /// </summary>
    public static class ProviderOutcome
    {
        /// <summary>
        /// Mendeteksi provider yang DIAM.
        ///
        /// ATURAN PALING PENTING DI KELAS INI: exit code 0 dengan stdout kosong
        /// BUKAN sukses.
        ///
        /// `agy -p` dilaporkan diam total ketika stdout bukan terminal - tanpa
        /// output, tanpa error, exit 0. Itu persis kondisi subprocess kita, dan
        /// mode kegagalannya yang terburuk: kelihatan seperti berhasil.
        ///
        /// Kalau ini tidak dideteksi, kita akan menyimpulkan "AI menjawab string
        /// kosong" padahal yang terjadi adalah "integrasi rusak" - dan
        /// mendiagnosisnya bisa makan berhari-hari.
        /// </summary>
        public static AgentFailure? DetectSilent(int exitCode, string? stdout)
        {
            if (exitCode != 0 || !string.IsNullOrWhiteSpace(stdout))
            {
                return null;
            }

            return new AgentFailure
            {
                Code = "provider_silent",
                Message = "Provider keluar dengan kode 0 tanpa mengeluarkan apa pun. "
                          + "Ini kegagalan integrasi, BUKAN jawaban AI yang kosong.",
                NextAction = "Pastikan provider dijalankan lewat pseudo-terminal (ConPTY), "
                             + "bukan pipe biasa, dan pastikan sudah login.",
            };
        }

        // Pembersih keluaran pseudo-terminal.
        private static readonly Regex KodeAnsi =
            new Regex(@"\x1B\[[0-9;?]*[a-zA-Z]|\x1B\][^\x07\x1B]*(\x07|\x1B\\)", RegexOptions.Compiled);

        private static readonly Regex Spinner =
            new Regex(@"[⠀-⣿─-╿■-◿⠁⠂⠄⡀⢀⠠⠐⠈|/\\-]{2,}", RegexOptions.Compiled);

        /// <summary>
        /// Membersihkan keluaran pseudo-terminal.
        ///
        /// Keluaran lewat ConPTY memuat escape sequence ANSI, repaint
        /// carriage-return, glyph spinner, dan box-drawing. Semuanya harus
        /// dibuang sebelum keluarannya bisa di-parse sebagai JSON atau
        /// ditampilkan di chat.
        /// </summary>
        public static string Bersihkan(string? mentah)
        {
            if (string.IsNullOrEmpty(mentah))
            {
                return string.Empty;
            }

            var tanpaAnsi = KodeAnsi.Replace(mentah!, string.Empty);

            // Repaint carriage-return: hanya segmen terakhir di setiap baris
            // yang relevan, sisanya sudah ditimpa di terminal.
            var hasil = new StringBuilder();
            foreach (var baris in tanpaAnsi.Split('\n'))
            {
                var segmen = baris.Split('\r');
                var terakhir = segmen[segmen.Length - 1];

                var dibersihkan = Spinner.Replace(terakhir, string.Empty).TrimEnd();

                if (dibersihkan.Length > 0)
                {
                    hasil.Append(dibersihkan).Append('\n');
                }
            }

            return hasil.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// Membentuk <see cref="AgentResult"/> dari keluaran provider.
        ///
        /// Mencoba berurutan:
        ///   1. JSON envelope lengkap dengan field protokol terpadu
        ///   2. Objek JSON apa pun - ambil field yang dikenali
        ///   3. Teks biasa sebagai summary
        ///
        /// Nama field untuk isi jawaban BELUM terkonfirmasi di dokumentasi resmi
        /// Antigravity, jadi beberapa kandidat dicoba. Lihat
        /// docs/integrasi-antigravity.md bagian 2.
        /// </summary>
        public static AgentResult Shape(string? stdout, int exitCode)
        {
            var diam = DetectSilent(exitCode, stdout);
            if (diam is not null)
            {
                return new AgentResult { Failure = diam };
            }

            var bersih = Bersihkan(stdout);

            if (exitCode != 0)
            {
                return new AgentResult
                {
                    Summary = bersih.Length > 0 ? bersih : null,
                    Failure = new AgentFailure
                    {
                        Code = "provider_failed",
                        Message = $"Provider keluar dengan kode {exitCode}.",
                        NextAction = TebakTindakan(bersih),
                    },
                };
            }

            var objek = CariObjekJson(bersih);
            if (objek is null)
            {
                // Teks biasa. Sah - tidak semua versi mendukung keluaran JSON.
                return new AgentResult { Summary = bersih };
            }

            return new AgentResult
            {
                Summary = AmbilTeks(objek, "summary", "response", "text", "message", "output"),
                Plan = objek["plan"]?.DeepClone(),
                ChangedFiles = AmbilDaftar(objek, "changedFiles"),
                Verification = AmbilDaftar(objek, "verification"),
                Risks = AmbilDaftar(objek, "risks"),
                NextSteps = AmbilDaftar(objek, "nextSteps"),
            };
        }

        /// <summary>
        /// Menebak tindakan perbaikan dari pesan error provider.
        /// Mengembalikan null kalau tidak yakin - lebih baik diam daripada
        /// mengarahkan ke jalan yang salah.
        /// </summary>
        internal static string? TebakTindakan(string pesan)
        {
            var kecil = pesan.ToLowerInvariant();

            if (kecil.Contains("auth") || kecil.Contains("login") || kecil.Contains("sign in"))
            {
                return "Jalankan provider sekali di terminal dan selesaikan login.";
            }

            if (kecil.Contains("rate limit") || kecil.Contains("quota") || kecil.Contains("429"))
            {
                return "Kuota provider habis. Tunggu, atau jalankan ulang DesignPlan terakhir "
                       + "tanpa memanggil AI.";
            }

            if (kecil.Contains("flags provided but not defined") || kecil.Contains("unknown flag"))
            {
                return "Versi provider tidak mengenali flag yang dipakai. "
                       + "Jalankan ulang pemeriksaan provider untuk membaca ulang --help.";
            }

            return null;
        }

        /// <summary>
        /// Mencari objek JSON di dalam keluaran.
        ///
        /// Keluaran provider sering bercampur baris log dan JSON. Dicari dari
        /// baris terakhir ke atas, karena envelope hasil biasanya di akhir.
        /// Untuk stream-json (JSONL), baris JSON terakhir yang relevan.
        /// </summary>
        internal static JsonObject? CariObjekJson(string teks)
        {
            if (string.IsNullOrWhiteSpace(teks))
            {
                return null;
            }

            // Coba seluruh teks dulu - kasus paling umum untuk --output-format json.
            var utuh = CobaParse(teks);
            if (utuh is not null)
            {
                return utuh;
            }

            var baris = teks.Split('\n')
                .Select(b => b.Trim())
                .Where(b => b.StartsWith("{", StringComparison.Ordinal))
                .ToList();

            for (var i = baris.Count - 1; i >= 0; i--)
            {
                var objek = CobaParse(baris[i]);

                // Baris JSONL yang hanya berisi peristiwa progres bukan hasil
                // akhir; yang dicari adalah yang memuat field protokol.
                if (objek is not null && Berguna(objek))
                {
                    return objek;
                }
            }

            // Tidak ada yang memuat field protokol - ambil baris JSON terakhir
            // apa pun isinya, supaya informasinya tidak hilang sama sekali.
            for (var i = baris.Count - 1; i >= 0; i--)
            {
                var objek = CobaParse(baris[i]);
                if (objek is not null)
                {
                    return objek;
                }
            }

            return null;
        }

        private static bool Berguna(JsonObject objek)
            => objek.ContainsKey("summary")
               || objek.ContainsKey("response")
               || objek.ContainsKey("plan")
               || objek.ContainsKey("structured_output");

        private static JsonObject? CobaParse(string teks)
        {
            try
            {
                return JsonNode.Parse(teks) as JsonObject;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string? AmbilTeks(JsonObject objek, params string[] kandidat)
        {
            foreach (var nama in kandidat)
            {
                if (objek.TryGetPropertyValue(nama, out var nilai)
                    && nilai is JsonValue value
                    && value.TryGetValue<string>(out var teks)
                    && !string.IsNullOrWhiteSpace(teks))
                {
                    return teks;
                }
            }

            // structured_output dipakai kalau provider dipaksa mengikuti skema.
            if (objek.TryGetPropertyValue("structured_output", out var terstruktur)
                && terstruktur is JsonObject dalam)
            {
                return AmbilTeks(dalam, kandidat);
            }

            return null;
        }

        private static IReadOnlyList<string> AmbilDaftar(JsonObject objek, string nama)
        {
            if (!objek.TryGetPropertyValue(nama, out var nilai) || nilai is not JsonArray array)
            {
                return Array.Empty<string>();
            }

            var hasil = new List<string>();

            foreach (var item in array)
            {
                if (item is null)
                {
                    continue;
                }

                if (item is JsonValue value && value.TryGetValue<string>(out var teks))
                {
                    hasil.Add(teks);
                }
                else
                {
                    // Objek seperti {command, status} diringkas jadi satu baris,
                    // supaya tetap terbaca di chat.
                    hasil.Add(item.ToJsonString());
                }
            }

            return hasil;
        }

        /// <summary>
        /// Mem-parse keluaran <c>--help</c> jadi daftar flag yang benar-benar ada.
        ///
        /// Dipakai supaya kita tidak mengirim flag yang tidak dikenali versi
        /// terpasang. Lihat docs/integrasi-antigravity.md bagian 4.
        /// </summary>
        public static IReadOnlyList<string> ParseFlags(string? keluaranHelp)
        {
            if (string.IsNullOrWhiteSpace(keluaranHelp))
            {
                return Array.Empty<string>();
            }

            var cocok = Regex.Matches(keluaranHelp!, @"(?<!\w)(--[a-zA-Z][a-zA-Z0-9-]*|-[a-zA-Z])(?![\w-])");

            var hasil = new List<string>();
            var terlihat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in cocok)
            {
                var flag = m.Groups[1].Value;
                if (terlihat.Add(flag))
                {
                    hasil.Add(flag);
                }
            }

            return hasil;
        }
    }
}
