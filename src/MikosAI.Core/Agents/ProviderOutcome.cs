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
        public static AgentFailure? DetectSilent(int exitCode, string? stdout, string? stderr = null)
        {
            if (exitCode != 0 || !string.IsNullOrWhiteSpace(stdout))
            {
                return null;
            }

            // stderr IKUT DILAPORKAN, DAN ITU PERBAIKAN YANG MAHAL DIDAPAT.
            //
            // Eksekutor dulu membuang stderr kalau exit code 0 - dengan alasan
            // "stderr pada proses yang sukses biasanya cuma kebisingan". Tapi
            // provider_silent PERSIS berarti exit code 0, jadi di satu-satunya
            // keadaan di mana stderr adalah bukti SATU-SATUNYA, bukti itu
            // dibuang.
            //
            // Dan agy memang memakai jalur itu: penolakan izin ditulis ke stderr
            // dengan exit code 0 - "jetski: no output produced - a tool required
            // the mcp permission...". Pesan yang menjelaskan segalanya, hilang.
            var alasan = (stderr ?? string.Empty).Trim();

            var pesan = "Provider keluar dengan kode 0 tanpa mengeluarkan apa pun. "
                        + "Ini kegagalan integrasi, BUKAN jawaban AI yang kosong.";

            if (alasan.Length > 0)
            {
                pesan += Environment.NewLine + Environment.NewLine + "Pesan dari provider (stderr):"
                         + Environment.NewLine + Potong(alasan, 1200);
            }

            return new AgentFailure
            {
                Code = "provider_silent",
                Message = pesan,

                // NASIHAT LAMA DIHAPUS: ia menyuruh memakai ConPTY, padahal item 2
                // di daftar verifikasi sudah DIJAWAB - pipe biasa cukup di agy
                // 1.4.11+. Mengirim orang memasang pseudo-terminal untuk masalah
                // yang bukan itu adalah biaya yang kita timbulkan sendiri.
                NextAction = alasan.Length > 0
                    ? "Baca pesan stderr di atas - itu alasan sebenarnya."
                    : "stderr juga kosong. Periksa `agy` sudah login (agy -p \"tes\" dari terminal), "
                      + "lalu jalankan scripts\\mcp-roundtrip.ps1 -Agy untuk membandingkan "
                      + "dengan jalur yang terbukti bekerja.",
            };
        }

        /// <summary>Memotong teks panjang supaya pesan error tetap terbaca.</summary>
        private static string Potong(string teks, int maksimum)
            => teks.Length <= maksimum ? teks : teks.Substring(0, maksimum) + " […]";

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

            // CRLF ADALAH TERMINATOR BARIS, BUKAN REPAINT.
            //
            // Normalisasi ini WAJIB mendahului pemecahan repaint di bawah, dan
            // ketiadaannya adalah bug yang menghancurkan hampir seluruh keluaran
            // provider di Windows.
            //
            // Tanpa baris ini: "a\r\n" dipecah pada '\n' menjadi ["a\r", ""],
            // lalu "a\r" dipecah pada '\r' menjadi ["a", ""] - dan yang diambil
            // adalah segmen TERAKHIR, yaitu string kosong. Jadi setiap baris
            // CRLF menyisakan kosong dan dibuang; hanya baris terakhir, yang
            // tidak punya \r di ujung, yang selamat.
            //
            // agy di Windows mengeluarkan CRLF. Artinya jawaban JSON-nya menjadi
            // satu baris penutup "}" - dan parser gagal, lalu seluruh keluaran
            // dilaporkan sebagai teks biasa atau kosong.
            //
            // Bug ini tersembunyi lama karena data uji di repo ini memakai LF.
            // Ia baru terlihat setelah satu berkas test ditulis ulang dengan
            // CRLF - bukan karena bug-nya baru.
            var dinormalkan = tanpaAnsi.Replace("\r\n", "\n");

            // Repaint carriage-return: setelah normalisasi di atas, \r yang
            // tersisa memang repaint sungguhan - terminal menimpa baris yang
            // sama, dan hanya segmen terakhir yang relevan.
            var hasil = new StringBuilder();
            foreach (var baris in dinormalkan.Split('\n'))
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
        public static AgentResult Shape(string? stdout, int exitCode, string? stderr = null)
        {
            var diam = DetectSilent(exitCode, stdout, stderr);
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

            var mentahObjek = CariObjekJson(bersih);
            if (mentahObjek is null)
            {
                // Teks biasa. Sah - mode keluaran default agy adalah text.
                return new AgentResult { Summary = bersih };
            }

            var objek = BukaEnvelope(mentahObjek);

            // IZIN DITOLAK: DIPERIKSA SEBELUM status, KARENA status-nya SUCCESS.
            //
            // Kalau agy menolak tool di lapisan izin, jawabannya begini:
            //
            //   {"status":"SUCCESS","response":"",
            //    "denied_actions":[{"action":"mcp","display_name":"CallMcpTool"}]}
            //
            // Exit code 0, status SUCCESS, stdout TIDAK kosong - jadi
            // DetectSilent tidak menyala dan pemeriksaan status lolos. Tanpa
            // pemeriksaan ini, panel menampilkan jawaban kosong tanpa penjelasan
            // apa pun: persis mode kegagalan yang kelas ini dibuat untuk
            // mencegah, masuk lewat pintu lain.
            var ditolak = objek["denied_actions"] as JsonArray;

            if (ditolak is not null && ditolak.Count > 0)
            {
                var nama = new List<string>();
                foreach (var item in ditolak)
                {
                    var aksi = item is JsonObject o
                        ? AmbilTeks(o, "display_name", "action")
                        : item?.ToString();

                    if (!string.IsNullOrWhiteSpace(aksi))
                    {
                        nama.Add(aksi!);
                    }
                }

                var daftar = nama.Count > 0 ? string.Join(", ", nama) : $"{ditolak.Count} aksi";

                return new AgentResult
                {
                    Summary = AmbilTeks(objek, "response", "summary", "message"),
                    Failure = new AgentFailure
                    {
                        Code = "provider_permission_denied",
                        Message = $"Provider menolak aksi di lapisan izinnya sendiri: {daftar}. "
                                  + "Jawabannya kosong karena toolnya TIDAK PERNAH dijalankan - "
                                  + "ini bukan jawaban AI yang kosong, dan bukan server MCP kita yang gagal.",
                        NextAction = "Mode headless tidak bisa menampilkan prompt izin, jadi aturannya harus "
                                     + "diberikan lewat --settings. Pastikan AgentRequest.BerkasIzin terisi; "
                                     + "lihat AntigravityCommand.IsiBerkasIzin.",
                    },
                };
            }

            // agy melaporkan keberhasilan lewat field status. Nilai selain
            // SUCCESS diperlakukan sebagai kegagalan, dan nilai yang TIDAK
            // DIKENAL juga - default aman, sama seperti di VerificationGate.
            var status = AmbilTeks(objek, "status");
            if (status is not null
                && !string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                return new AgentResult
                {
                    Summary = AmbilTeks(objek, "response", "summary", "message"),
                    Failure = new AgentFailure
                    {
                        Code = "provider_reported_failure",
                        Message = $"Provider melaporkan status '{status}'.",
                        NextAction = TebakTindakan(bersih),
                    },
                };
            }

            return new AgentResult
            {
                Summary = AmbilTeks(objek, "summary", "response", "text", "message", "output")?.TrimEnd('\n'),
                Plan = objek["plan"]?.DeepClone(),
                ChangedFiles = AmbilDaftar(objek, "changedFiles"),
                Verification = AmbilDaftar(objek, "verification"),
                Risks = AmbilDaftar(objek, "risks"),
                NextSteps = AmbilDaftar(objek, "nextSteps"),
            };
        }

        /// <summary>
        /// Mengambil potongan teks bertahap dari satu baris stream-json.
        ///
        /// Dipakai chat untuk menampilkan jawaban sambil mengalir, alih-alih
        /// diam lalu tiba-tiba selesai. Mengembalikan null kalau baris itu bukan
        /// peristiwa teks.
        ///
        /// Bentuknya, dari keluaran nyata agy 1.4.11:
        ///   {"event":"step_update","step_update":{"step_type":"agent_response",
        ///    "text_delta":"Mer"}}
        /// </summary>
        public static string? AmbilPotonganTeks(string? barisJsonl)
        {
            if (string.IsNullOrWhiteSpace(barisJsonl))
            {
                return null;
            }

            var objek = CobaParse(barisJsonl!.Trim());
            if (objek is null)
            {
                return null;
            }

            if (objek["step_update"] is not JsonObject langkah)
            {
                return null;
            }

            // Hanya respons agent yang ditampilkan. Peristiwa user_input adalah
            // gema prompt kita sendiri.
            var jenis = AmbilTeks(langkah, "step_type");
            if (!string.Equals(jenis, "agent_response", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return langkah["text_delta"] is JsonValue nilai
                   && nilai.TryGetValue<string>(out var teks)
                ? teks
                : null;
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

        /// <summary>
        /// Apakah objek ini memuat hasil akhir, bukan sekadar peristiwa progres.
        ///
        /// Bentuk yang dikenali, berdasarkan keluaran NYATA agy 1.4.11:
        ///
        ///   --output-format json
        ///     {"status":"SUCCESS","response":"...","usage":{...}}
        ///
        ///   --output-format stream-json
        ///     {"event":"init","init":{...}}
        ///     {"event":"step_update","step_update":{...,"text_delta":"..."}}
        ///     {"event":"result","result":{"status":"SUCCESS","response":"..."}}
        /// </summary>
        private static bool Berguna(JsonObject objek)
        {
            if (objek.ContainsKey("summary")
                || objek.ContainsKey("response")
                || objek.ContainsKey("plan")
                || objek.ContainsKey("structured_output"))
            {
                return true;
            }

            // Envelope stream-json: hasil akhir ada di bawah "result".
            return objek["event"] is JsonValue ev
                   && ev.TryGetValue<string>(out var nama)
                   && string.Equals(nama, "result", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Membuka envelope <c>{"event":"result","result":{...}}</c> dari
        /// stream-json. Mengembalikan objek yang sama kalau bukan envelope.
        /// </summary>
        private static JsonObject BukaEnvelope(JsonObject objek)
            => objek["result"] is JsonObject dalam ? dalam : objek;

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
        /// <summary>
        /// Membaca flag dari KEDUA stream <c>--help</c>.
        ///
        /// `agy --help` menulis seluruh bantuannya ke STDERR dengan exit code
        /// 0. Diukur pada 1.5.0: stdout 0 karakter, stderr 3323 karakter.
        ///
        /// Membaca stdout saja berarti melihat CLI tanpa flag apa pun, dan
        /// akibatnya <c>--settings</c> tidak pernah dikirim sehingga seluruh
        /// tool MCP ditolak otomatis oleh mode headless - muncul sebagai
        /// jawaban kosong. Lihat tests/BantuanDiStderrTests.cs.
        ///
        /// Keduanya digabung, bukan dipilih, karena stream mana yang dipakai
        /// adalah detail yang berubah antarrilis. Menebak satu berarti menebak
        /// ulang setiap kali agy diperbarui.
        /// </summary>
        public static IReadOnlyList<string> ParseFlags(string? stdout, string? stderr)
        {
            var gabungan = (stdout ?? string.Empty) + "\n" + (stderr ?? string.Empty);
            return ParseFlags(gabungan);
        }

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
