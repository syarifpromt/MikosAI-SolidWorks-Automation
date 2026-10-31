using System;
using System.Threading;
using System.Threading.Tasks;

namespace MikosAI.Core.Threading
{
    /// <summary>
    /// Satu-satunya pintu ke API SolidWorks.
    ///
    /// API SolidWorks TIDAK thread-safe dan harus dipanggil dari thread STA utama
    /// yang diberikan SolidWorks. Dalam arsitektur kita ada tiga sumber panggilan
    /// konkuren:
    ///
    ///   - thread HttpListener dari McpHttpServer
    ///   - thread pembaca pipe dari ProviderHost
    ///   - callback WebView2 dari UI
    ///
    /// Ketiganya tidak boleh menyentuh ISldWorks langsung. Pelanggaran aturan ini
    /// menghasilkan crash acak yang tidak bisa direproduksi, dan karena add-in
    /// kita in-process, crash itu menjatuhkan SolidWorks - bukan hanya kode kita.
    ///
    /// Abstraksi ini tinggal di Core (tanpa dependensi SolidWorks) supaya seluruh
    /// logika di atasnya bisa diuji dengan implementasi palsu.
    ///
    /// Implementasi nyata ada di MikosAI.Addin dan memakai Control.BeginInvoke
    /// pada Control WinForms yang dibuat saat ConnectToSW.
    /// </summary>
    public interface IMainThreadDispatcher
    {
        /// <summary>
        /// true kalau pemanggil sudah berada di thread utama SolidWorks.
        /// Dipakai untuk assertion dan diagnosis, bukan untuk melewati dispatcher.
        /// </summary>
        bool IsOnMainThread { get; }

        /// <summary>Menjalankan pekerjaan di thread utama dan mengembalikan hasilnya.</summary>
        Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken = default);

        /// <summary>Menjalankan pekerjaan di thread utama tanpa hasil.</summary>
        Task InvokeAsync(Action work, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Dilempar kalau API SolidWorks disentuh dari thread yang salah.
    ///
    /// Gagal keras itu disengaja: lebih baik satu exception yang jelas saat
    /// development daripada crash acak di tangan pengguna.
    /// </summary>
    public sealed class MainThreadViolationException : InvalidOperationException
    {
        public MainThreadViolationException(string memberName)
            : base($"'{memberName}' memanggil API SolidWorks dari luar thread utama. " +
                   "Bungkus pemanggilannya dengan IMainThreadDispatcher.InvokeAsync. " +
                   "Lihat CONTRIBUTING.md aturan 1.")
        {
        }
    }
}
