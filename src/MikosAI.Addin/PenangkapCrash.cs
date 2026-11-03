using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MikosAI.Addin
{
    /// <summary>
    /// Menangkap exception yang lolos ke atas dan menuliskannya ke berkas.
    ///
    /// KENAPA ADA.
    ///
    /// Log SolidWorks dari sesi yang crash memuat:
    ///
    ///     &lt;SWEXH&gt;
    ///     &lt;UNHANDLED&gt;
    ///     &lt;/SWEXH&gt;
    ///
    /// Artinya yang menjatuhkan SolidWorks adalah exception .NET yang tidak
    /// tertangani - BUKAN access violation di kode native. Itu kabar baik:
    /// exception bisa ditangkap, dan isinya bisa dicatat.
    ///
    /// Sebelum ini, lima dugaan berturut-turut dibuat dari gejala saja, dan
    /// lima-limanya salah. host-status.json selalu menunjukkan errors kosong,
    /// karena exception-nya tidak pernah lewat try/catch mana pun milik kami.
    ///
    /// Kelas ini menutup celah itu. Ia hanya memakai tipe dari mscorlib, dan
    /// menulis ke berkasnya sendiri - supaya tetap bekerja walaupun segalanya
    /// yang lain sudah rusak.
    ///
    /// Berkasnya: %LOCALAPPDATA%\MikosAI\crash.log
    /// </summary>
    internal static class PenangkapCrash
    {
        private static readonly object Kunci = new object();
        private static bool _terpasang;
        private static string? _berkas;

        internal static string? Berkas => _berkas;

        internal static void Pasang()
        {
            lock (Kunci)
            {
                if (_terpasang)
                {
                    return;
                }

                _terpasang = true;

                try
                {
                    var folder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MikosAI");

                    Directory.CreateDirectory(folder);
                    _berkas = Path.Combine(folder, "crash.log");

                    AppDomain.CurrentDomain.UnhandledException += DomainTidakTertangani;
                    TaskScheduler.UnobservedTaskException += TaskTidakTerpantau;

                    // Exception di message loop WinForms. SolidWorks bukan
                    // aplikasi WinForms, jadi ini mungkin tidak pernah terpicu -
                    // tapi berlangganan tidak merugikan, dan kalau terpicu
                    // informasinya persis yang kami cari.
                    System.Windows.Forms.Application.ThreadException += ThreadUi;

                    Catat("pasang", "Penangkap crash terpasang.");
                }
                catch (Exception)
                {
                    // Tidak ada tempat lain untuk melapor.
                }
            }
        }

        internal static void Lepas()
        {
            lock (Kunci)
            {
                if (!_terpasang)
                {
                    return;
                }

                _terpasang = false;

                try
                {
                    AppDomain.CurrentDomain.UnhandledException -= DomainTidakTertangani;
                    TaskScheduler.UnobservedTaskException -= TaskTidakTerpantau;
                    System.Windows.Forms.Application.ThreadException -= ThreadUi;
                }
                catch (Exception)
                {
                    // Abaikan saat menutup.
                }
            }
        }

        private static void DomainTidakTertangani(object sender, UnhandledExceptionEventArgs e)
        {
            // Proses akan mati setelah handler ini. Satu-satunya hal berguna
            // yang bisa dilakukan adalah menulis isinya ke disk SEKARANG.
            Catat(
                e.IsTerminating ? "FATAL (proses berhenti)" : "tidak tertangani",
                Uraikan(e.ExceptionObject as Exception));
        }

        private static void TaskTidakTerpantau(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Catat("task tidak terpantau", Uraikan(e.Exception));

            // Ditandai terpantau supaya tidak ikut menjatuhkan proses pada
            // konfigurasi yang memperlakukannya sebagai fatal.
            try
            {
                e.SetObserved();
            }
            catch (Exception)
            {
                // Abaikan.
            }
        }

        private static void ThreadUi(object sender, System.Threading.ThreadExceptionEventArgs e)
            => Catat("thread UI", Uraikan(e.Exception));

        /// <summary>
        /// Menguraikan exception beserta seluruh inner exception dan stack
        /// trace-nya. Inner exception sering yang sebenarnya menjelaskan.
        /// </summary>
        private static string Uraikan(Exception? exception)
        {
            if (exception is null)
            {
                return "(objek exception tidak tersedia)";
            }

            var sb = new StringBuilder();
            var tingkat = 0;

            for (var e = exception; e is not null && tingkat < 8; e = e.InnerException, tingkat++)
            {
                var awalan = tingkat == 0 ? string.Empty : new string(' ', tingkat * 2) + "-> ";

                sb.Append(awalan)
                  .Append(e.GetType().FullName)
                  .Append(": ")
                  .AppendLine(e.Message);

                if (e is System.Runtime.InteropServices.COMException com)
                {
                    sb.Append(awalan).AppendFormat("HRESULT=0x{0:X8}", com.HResult).AppendLine();
                }

                if (!string.IsNullOrEmpty(e.StackTrace))
                {
                    sb.AppendLine(e.StackTrace);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Menulis satu entri. Append, tidak pernah menimpa - urutan kejadian
        /// sebelum crash adalah bagian dari buktinya.
        /// </summary>
        internal static void Catat(string jenis, string isi)
        {
            try
            {
                if (_berkas is null)
                {
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("================================================================");
                sb.Append('[').Append(DateTimeOffset.Now.ToString("O")).Append("] ").AppendLine(jenis);
                sb.Append("thread: ").Append(System.Threading.Thread.CurrentThread.ManagedThreadId).AppendLine();
                sb.AppendLine("----------------------------------------------------------------");
                sb.AppendLine(isi);

                File.AppendAllText(_berkas, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // Kalau menulis log crash pun gagal, tidak ada lagi yang bisa
                // dilakukan.
            }
        }
    }
}
