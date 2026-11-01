using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MikosAI.Core.Agents;

namespace MikosAI.Addin
{
    /// <summary>
    /// Adapter Antigravity CLI.
    ///
    /// Menjalankan `agy` sebagai subprocess dengan PIPE BIASA, bukan ConPTY.
    ///
    /// Rancangan awal mewajibkan ConPTY karena `agy -p` dilaporkan diam total
    /// ketika stdout bukan terminal. Diuji langsung terhadap 1.4.11 pada
    /// 2026-11-01: bug itu TIDAK ADA, keluaran lewat pipe utuh. Lihat
    /// docs/integrasi-antigravity.md bagian 1.
    ///
    /// Deteksi `provider_silent` tetap ada sebagai jaring pengaman - kalau bug
    /// itu kembali di rilis berikutnya, kita mengenalinya dalam hitungan detik.
    /// </summary>
    internal sealed class AntigravityProvider : IAgentProvider
    {
        private readonly string _executable;
        private readonly HostDiagnostics _diagnostics;

        internal AntigravityProvider(string executable, HostDiagnostics diagnostics)
        {
            _executable = executable ?? throw new ArgumentNullException(nameof(executable));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public string Id => "antigravity";

        public string DisplayName => "Antigravity CLI";

        // ====================================================================
        // Probe
        // ====================================================================

        public async Task<ProviderProbe> ProbeAsync(CancellationToken cancellationToken = default)
        {
            var probe = new ProviderProbe { ExecutablePath = _executable };

            try
            {
                var versi = await JalankanAsync(
                    AntigravityCommand.BuatArgumenVersi(),
                    workingDirectory: null,
                    onDelta: null,
                    timeout: TimeSpan.FromSeconds(20),
                    cancellationToken).ConfigureAwait(false);

                if (versi.ExitCode != 0 || string.IsNullOrWhiteSpace(versi.Output))
                {
                    probe.State = ProviderState.NotInstalled;
                    probe.Message = "agy --version gagal.";
                    return probe;
                }

                probe.Version = versi.Output.Trim().Split('\n')[0].Trim();
                probe.State = ProviderState.Installed;

                var bantuan = await JalankanAsync(
                    AntigravityCommand.BuatArgumenBantuan(),
                    workingDirectory: null,
                    onDelta: null,
                    timeout: TimeSpan.FromSeconds(20),
                    cancellationToken).ConfigureAwait(false);

                probe.SupportedFlags = ProviderOutcome.ParseFlags(bantuan.Output);

                // Autentikasi TIDAK diperiksa dengan permintaan nyata - itu akan
                // membakar kuota setiap kali SolidWorks dibuka.
                //
                // Keberadaan oauth_creds.json adalah petunjuk kuat, tapi bukan
                // bukti bahwa kredensialnya masih berlaku. Jadi keadaannya
                // AuthUnverified: boleh mencoba, TIDAK boleh ditampilkan sebagai
                // terhubung. Permintaan pertama yang akan membuktikannya.
                probe.State = AdaKredensial() ? ProviderState.AuthUnverified : ProviderState.Installed;

                return probe;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("probe Antigravity", exception);

                probe.State = ProviderState.NotInstalled;
                probe.Message = exception.Message;
                return probe;
            }
        }

        private static bool AdaKredensial()
        {
            try
            {
                var berkas = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".gemini",
                    "oauth_creds.json");

                return File.Exists(berkas);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ====================================================================
        // Perintah dan hasil
        // ====================================================================

        public ProviderCommand BuildCommand(AgentRequest request, ProviderProbe probe)
            => new ProviderCommand
            {
                Executable = _executable,
                Arguments = AntigravityCommand.BuatArgumen(request, probe),
                WorkingDirectory = request.WorkingDirectory,
                RequiresPseudoConsole = false,
                OutputFormat = AntigravityCommand.PilihFormat(request, probe),
            };

        public AgentResult ParseResult(string stdout, int exitCode, ProviderProbe probe)
            => ProviderOutcome.Shape(stdout, exitCode);

        // ====================================================================
        // Eksekusi
        // ====================================================================

        internal sealed class HasilProses
        {
            internal int ExitCode { get; set; }
            internal string Output { get; set; } = string.Empty;
            internal bool Timeout { get; set; }
        }

        /// <summary>
        /// Menjalankan satu permintaan ke agy, dengan progres bertahap.
        /// </summary>
        internal async Task<AgentResult> KirimAsync(
            AgentRequest request,
            ProviderProbe probe,
            Action<string>? onDelta,
            CancellationToken cancellationToken = default)
        {
            var perintah = BuildCommand(request, probe);

            var hasil = await JalankanAsync(
                perintah.Arguments,
                perintah.WorkingDirectory,
                onDelta,
                TimeSpan.FromSeconds(AntigravityCommand.BatasWaktuDetik + 15),
                cancellationToken).ConfigureAwait(false);

            if (hasil.Timeout)
            {
                return new AgentResult
                {
                    Failure = new AgentFailure
                    {
                        Code = "provider_timeout",
                        Message = $"Provider tidak selesai dalam {AntigravityCommand.BatasWaktuDetik} detik.",
                        NextAction = "Coba permintaan yang lebih sederhana.",
                    },
                };
            }

            return ParseResult(hasil.Output, hasil.ExitCode, probe);
        }

        /// <summary>
        /// Menjalankan satu subcommand agy yang tidak menghasilkan jawaban AI,
        /// seperti <c>mcp add</c> atau <c>mcp remove</c>.
        ///
        /// Mengembalikan true kalau exit code 0. Keluarannya dicatat ke
        /// diagnostik kalau gagal - perintah ini jarang gagal, dan kalau gagal
        /// penyebabnya perlu terlihat.
        /// </summary>
        internal async Task<bool> JalankanPerintahAsync(
            IReadOnlyList<string> argumen,
            CancellationToken cancellationToken = default)
        {
            var hasil = await JalankanAsync(
                argumen,
                workingDirectory: null,
                onDelta: null,
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);

            if (hasil.ExitCode != 0)
            {
                var keluaran = hasil.Output.Trim();
                _diagnostics.AddError(
                    $"agy {string.Join(" ", argumen)} gagal (exit {hasil.ExitCode})"
                    + (keluaran.Length > 0 ? ": " + keluaran : string.Empty));
            }

            return hasil.ExitCode == 0 && !hasil.Timeout;
        }

        /// <summary>
        /// Menjalankan proses dan mengumpulkan keluarannya.
        ///
        /// stdout dibaca BARIS DEMI BARIS secara asinkron, bukan ditunggu sampai
        /// selesai. Dua alasan: potongan teks bertahap bisa langsung sampai ke
        /// chat, dan pipe yang penuh tidak membuat proses anak menggantung.
        ///
        /// stderr dibaca terpisah dan digabung di akhir, supaya pesan error
        /// tidak mengotori jalur parsing JSON.
        /// </summary>
        private async Task<HasilProses> JalankanAsync(
            IReadOnlyList<string> argumen,
            string? workingDirectory,
            Action<string>? onDelta,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var info = new ProcessStartInfo
            {
                FileName = _executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var item in argumen)
            {
                info.Arguments += Kutip(item) + " ";
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            {
                info.WorkingDirectory = workingDirectory!;
            }

            using var proses = new Process { StartInfo = info, EnableRaisingEvents = true };

            var keluaran = new StringBuilder();
            var galat = new StringBuilder();

            proses.Start();

            // Tutup stdin segera. agy dalam print mode tidak membacanya, dan
            // membiarkannya terbuka bisa membuat proses menunggu masukan yang
            // tidak akan pernah datang.
            try
            {
                proses.StandardInput.Close();
            }
            catch (Exception)
            {
                // Sebagian proses menolak ini - tidak masalah.
            }

            var bacaKeluaran = Task.Run(async () =>
            {
                string? baris;
                while ((baris = await proses.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
                {
                    keluaran.Append(baris).Append('\n');

                    if (onDelta is null)
                    {
                        continue;
                    }

                    var potongan = ProviderOutcome.AmbilPotonganTeks(baris);
                    if (!string.IsNullOrEmpty(potongan))
                    {
                        try
                        {
                            onDelta(potongan!);
                        }
                        catch (Exception exception)
                        {
                            _diagnostics.AddError("mengirim potongan teks ke UI", exception);
                        }
                    }
                }
            });

            var bacaGalat = Task.Run(async () =>
            {
                var teks = await proses.StandardError.ReadToEndAsync().ConfigureAwait(false);
                galat.Append(teks);
            });

            var selesai = await TungguAsync(proses, timeout, cancellationToken).ConfigureAwait(false);

            if (!selesai)
            {
                MatikanProses(proses);
                return new HasilProses { Timeout = true, Output = keluaran.ToString() };
            }

            // Pembacaan stream bisa tertinggal sedikit dari keluarnya proses.
            await Task.WhenAny(
                Task.WhenAll(bacaKeluaran, bacaGalat),
                Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false);

            var gabungan = keluaran.ToString();
            var pesanGalat = galat.ToString().Trim();

            // stderr hanya digabung kalau prosesnya gagal. Kalau sukses, stderr
            // biasanya hanya catatan progres dan akan merusak parsing JSON.
            if (proses.ExitCode != 0 && pesanGalat.Length > 0)
            {
                gabungan += "\n" + pesanGalat;
            }

            return new HasilProses
            {
                ExitCode = proses.ExitCode,
                Output = gabungan,
            };
        }

        private static async Task<bool> TungguAsync(
            Process proses,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var selesai = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Keluar(object? s, EventArgs e) => selesai.TrySetResult(true);

            proses.Exited += Keluar;

            try
            {
                if (proses.HasExited)
                {
                    return true;
                }

                var menang = await Task.WhenAny(
                    selesai.Task,
                    Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);

                return menang == selesai.Task;
            }
            finally
            {
                proses.Exited -= Keluar;
            }
        }

        private void MatikanProses(Process proses)
        {
            try
            {
                if (!proses.HasExited)
                {
                    proses.Kill();
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("mematikan proses provider", exception);
            }
        }

        /// <summary>
        /// Mengutip satu argumen untuk baris perintah Windows.
        ///
        /// Penting: prompt pengguna bisa memuat spasi, tanda kutip, dan
        /// backslash. Mengirimnya tanpa dikutip akan membuat agy menerima
        /// potongan yang salah - atau lebih buruk, memperlakukan sebagian
        /// sebagai flag.
        /// </summary>
        internal static string Kutip(string argumen)
        {
            if (argumen.Length > 0 && argumen.IndexOfAny(new[] { ' ', '\t', '"', '\n' }) < 0)
            {
                return argumen;
            }

            var sb = new StringBuilder("\"");
            var backslash = 0;

            foreach (var c in argumen)
            {
                if (c == '\\')
                {
                    backslash++;
                    continue;
                }

                if (c == '"')
                {
                    // Backslash sebelum kutip harus digandakan, lalu kutipnya
                    // di-escape.
                    sb.Append('\\', backslash * 2 + 1).Append('"');
                    backslash = 0;
                    continue;
                }

                sb.Append('\\', backslash);
                backslash = 0;
                sb.Append(c);
            }

            // Backslash di akhir harus digandakan, supaya tidak meng-escape
            // kutip penutup.
            sb.Append('\\', backslash * 2).Append('"');

            return sb.ToString();
        }
    }
}
