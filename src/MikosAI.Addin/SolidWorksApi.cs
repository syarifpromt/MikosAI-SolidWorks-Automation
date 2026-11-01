using System;
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

        private SolidWorksApi(ISldWorks app, HostDiagnostics diagnostics)
        {
            _app = app;
            _diagnostics = diagnostics;
        }

        /// <summary>
        /// Membungkus objek aplikasi. Mengembalikan null kalau cast gagal -
        /// kegagalan cast dicatat ke diagnostik, bukan dilempar, supaya
        /// ConnectToSW tetap bisa menulis laporan.
        /// </summary>
        internal static SolidWorksApi? Buat(object application, HostDiagnostics diagnostics)
        {
            if (application is null)
            {
                diagnostics.AddError("ConnectToSW menerima ThisSW null.");
                return null;
            }

            try
            {
                return new SolidWorksApi((ISldWorks)application, diagnostics);
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
            => new SolidWorksReader(_app, diagnostics);

        /// <summary>
        /// Mendaftarkan callback add-in. Tanpa ini, SolidWorks tidak bisa
        /// memanggil balik method add-in.
        ///
        /// Nilai kembaliannya sengaja tidak dipakai, supaya tanda tangan stub
        /// compile-check tidak perlu cocok persis pada tipe kembalian.
        /// </summary>
        internal bool TryRegisterCallback(object addin, int cookie)
        {
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
        /// Jumlah dokumen terbuka. -1 berarti tidak bisa dibaca - BUKAN nol.
        /// Membedakan keduanya penting: gerbang penerimaan memeriksa
        /// GetDocumentCount()==0 setelah dokumen ditutup, dan "tidak terbaca"
        /// tidak boleh lolos sebagai "sudah bersih".
        /// </summary>
        internal int TryGetDocumentCount()
        {
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
