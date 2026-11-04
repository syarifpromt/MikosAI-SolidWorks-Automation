using System;
using MikosAI.Core.Threading;
using SolidWorks.Interop.sldworks;

namespace MikosAI.Addin
{
    /// <summary>
    /// Pembungkus bertipe di atas <c>ISldWorks</c>.
    ///
    /// KENAPA BERTIPE, BUKAN `dynamic`.
    ///
    /// Versi pertama kelas ini memakai <c>dynamic</c> dengan alasan yang
    /// terdengar bagus: kode add-in jadi bisa dikompilasi tanpa PIA, sehingga
    /// tanda tangan API yang salah menjadi error terstruktur di
    /// host-status.json alih-alih error compiler di mesin orang lain.
    ///
    /// Itu TERBUKTI SALAH di mesin nyata. Setiap panggilan gagal dengan
    /// HRESULT yang sama:
    ///
    ///   0x8002802B TYPE_E_ELEMENTNOTFOUND
    ///
    /// yang berarti nama member tidak ditemukan lewat IDispatch -
    /// SetAddinCallbackInfo2, RevisionNumber, GetDocumentCount, semuanya.
    /// Interface SolidWorks dideskripsikan oleh type library dan dipanggil lewat
    /// vtable; ia tidak bisa dicari namanya lewat IDispatch seperti yang
    /// diasumsikan DLR.
    ///
    /// Jadi pemanggilan harus early-bound. Keuntungan lintas-versi tetap
    /// dipertahankan dengan cara lain: PIA direferensi dengan
    /// EmbedInteropTypes, sehingga definisi interface DITANAM ke assembly kita
    /// dan type equivalence membuatnya menyatu dengan apa pun yang dimuat
    /// SolidWorks. Tidak ada dependensi assembly saat runtime, dan tidak ada
    /// pengikatan versi yang keras.
    ///
    /// Setiap pemanggilan tetap dibungkus try/catch yang mencatat kegagalannya
    /// ke host-status.json, karena perilaku runtime SolidWorks tetap tidak bisa
    /// diverifikasi dari sini.
    ///
    /// ATURAN: setiap method di kelas ini HANYA boleh dipanggil dari thread
    /// utama SolidWorks, lewat MainThreadDispatcher. Lihat CONTRIBUTING.md
    /// aturan 1.
    /// </summary>
    internal sealed class SolidWorksApi
    {
        private readonly ISldWorks _app;
        private readonly HostDiagnostics _diagnostics;
        private readonly IMainThreadDispatcher _dispatcher;

        private SolidWorksApi(ISldWorks app, HostDiagnostics diagnostics, IMainThreadDispatcher dispatcher)
        {
            _app = app;
            _diagnostics = diagnostics;
            _dispatcher = dispatcher;
        }

        /// <summary>
        /// Menegakkan aturan nomor 1 CONTRIBUTING.md lewat KODE, bukan review.
        ///
        /// Aturannya pernah dilanggar - panel memanggil pembacaan model dari
        /// thread latar, dan SolidWorks MATI dengan Fault Module kosong. Review
        /// yang seharusnya menangkapnya adalah saya sendiri.
        ///
        /// Dengan assert ini, pelanggaran yang sama menjadi exception yang
        /// tercatat di host-status.json beserta nama method-nya - bukan crash
        /// yang menjatuhkan pekerjaan pengguna.
        /// </summary>
        private void PastikanThreadUtama(string namaMethod)
        {
            if (_dispatcher.IsOnMainThread)
            {
                return;
            }

            var pelanggaran = new MikosAI.Core.Threading.MainThreadViolationException(
                $"SolidWorksApi.{namaMethod}");

            _diagnostics.AddError("pelanggaran thread utama", pelanggaran);

            throw pelanggaran;
        }

        /// <summary>
        /// Membungkus objek aplikasi. Mengembalikan null kalau cast gagal -
        /// kegagalan cast dicatat ke diagnostik, bukan dilempar, supaya
        /// ConnectToSW tetap bisa menulis laporan.
        /// </summary>
        internal static SolidWorksApi? Buat(
            object application,
            HostDiagnostics diagnostics,
            IMainThreadDispatcher dispatcher)
        {
            if (application is null)
            {
                diagnostics.AddError("ConnectToSW menerima ThisSW null.");
                return null;
            }

            try
            {
                return new SolidWorksApi((ISldWorks)application, diagnostics, dispatcher);
            }
            catch (Exception exception)
            {
                diagnostics.AddError("cast ke ISldWorks", exception);
                return null;
            }
        }

        /// <summary>Objek COM mentah, untuk dilepas saat disconnect.</summary>
        internal object Raw => _app;

        /// <summary>
        /// Membuat pembaca model. Dipisah supaya SolidWorksApi tetap hanya
        /// berisi operasi siklus hidup add-in, bukan pembacaan model.
        /// </summary>
        internal SolidWorksReader BuatPembaca(HostDiagnostics diagnostics)
            => new SolidWorksReader(_app, diagnostics, _dispatcher);

        /// <summary>
        /// Mendaftarkan callback add-in. Tanpa ini, SolidWorks tidak bisa
        /// memanggil balik method add-in.
        ///
        /// Nilai kembaliannya sengaja tidak dipakai, supaya tanda tangan stub
        /// compile-check tidak perlu cocok persis pada tipe kembalian.
        /// </summary>
        internal bool TryRegisterCallback(object addin, int cookie)
        {
            PastikanThreadUtama(nameof(TryRegisterCallback));

            try
            {
                _app.SetAddinCallbackInfo2(0, addin, cookie);
                return true;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("SetAddinCallbackInfo2", exception);
                return false;
            }
        }

        /// <summary>Nomor revisi SolidWorks, mis. "33.1.1" untuk SW2025 SP1.1.</summary>
        internal string? TryGetRevision()
        {
            PastikanThreadUtama(nameof(TryGetRevision));

            try
            {
                return _app.RevisionNumber();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("RevisionNumber", exception);
                return null;
            }
        }

        /// <summary>
        /// Membuat TaskPane dan menempelkan kontrol WinForms ke dalamnya.
        ///
        /// Memakai DisplayWindowFromHandlex64, varian 64-bit. SolidWorks modern
        /// adalah proses 64-bit, dan varian 32-bit akan memotong handle.
        ///
        /// Mengembalikan null kalau gagal; penyebabnya tercatat di diagnostik.
        /// </summary>
        internal ITaskpaneView? TryCreateTaskPane(string bitmapPath, string tooltip, IntPtr handle)
        {
            PastikanThreadUtama(nameof(TryCreateTaskPane));

            ITaskpaneView? view = null;

            try
            {
                view = _app.CreateTaskpaneView2(bitmapPath, tooltip);
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("CreateTaskpaneView2", exception);
                return null;
            }

            if (view is null)
            {
                _diagnostics.AddError("CreateTaskpaneView2 mengembalikan null.");
                return null;
            }

            try
            {
                if (!view.DisplayWindowFromHandlex64(handle.ToInt64()))
                {
                    _diagnostics.AddError("DisplayWindowFromHandlex64 mengembalikan false.");
                    return null;
                }

                return view;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("DisplayWindowFromHandlex64", exception);
                return null;
            }
        }

        /// <summary>
        /// CommandManager untuk cookie add-in ini.
        ///
        /// Lewat sini, bukan lewat <see cref="Raw"/>, supaya assert thread
        /// utama ikut berlaku. CommandManager adalah objek COM SolidWorks biasa:
        /// menyentuhnya dari thread latar adalah pelanggaran yang sama seperti
        /// pembacaan model, dan pelanggaran itu pernah menjatuhkan SolidWorks
        /// tanpa exception.
        ///
        /// Tanda tangan direfleksikan dari PIA asli:
        ///   CommandManager GetCommandManager(Int32 Cookie)
        ///
        /// null berarti gagal; sebabnya tercatat di diagnostik.
        /// </summary>
        internal ICommandManager? TryGetCommandManager(int cookie)
        {
            PastikanThreadUtama(nameof(TryGetCommandManager));

            try
            {
                var manager = _app.GetCommandManager(cookie);

                if (manager is null)
                {
                    _diagnostics.AddError("GetCommandManager mengembalikan null.");
                }

                return manager;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("GetCommandManager", exception);
                return null;
            }
        }

        /// <summary>
        /// Jumlah dokumen terbuka. -1 berarti tidak bisa dibaca - BUKAN nol.
        /// Membedakan keduanya penting: gerbang penerimaan memeriksa
        /// GetDocumentCount()==0 setelah dokumen ditutup, dan "tidak terbaca"
        /// tidak boleh lolos sebagai "sudah bersih".
        /// </summary>
        internal int TryGetDocumentCount()
        {
            PastikanThreadUtama(nameof(TryGetDocumentCount));

            try
            {
                return _app.GetDocumentCount();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("GetDocumentCount", exception);
                return -1;
            }
        }
    }
}
