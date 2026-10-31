using System;

namespace MikosAI.Addin
{
    /// <summary>
    /// Pembungkus late-bound di atas <c>ISldWorks</c>.
    ///
    /// KENAPA LATE-BOUND, BUKAN PIA BERTIPE.
    ///
    /// Kode add-in ini tidak bisa dikompilasi di mesin tanpa SolidWorks, jadi
    /// setiap tanda tangan API yang salah baru ketahuan saat seseorang
    /// mencobanya di mesin bermesin CAD - umpan balik yang lambat dan
    /// menjengkelkan, tepat di langkah yang ingin kita buat simpel.
    ///
    /// Dengan late-binding lewat <c>dynamic</c> (SolidWorks mendukung IDispatch),
    /// permukaan PIA saat kompilasi menyusut jadi SATU interface:
    /// <c>ISwAddin</c>. Semua yang lain diselesaikan saat runtime, dan setiap
    /// pemanggilan di sini dibungkus try/catch yang mencatat kegagalannya ke
    /// host-status.json.
    ///
    /// Harganya: tidak ada pemeriksaan tipe saat kompilasi untuk panggilan API.
    /// Itu diterima karena pemeriksaan tipe LOKAL pun tidak pernah kita dapat -
    /// kami tidak punya PIA untuk dicek. Yang kita tukar adalah "error compiler
    /// di mesin mereka" menjadi "error terstruktur di host-status.json", dan
    /// yang kedua jauh lebih mudah dilaporkan dan diperbaiki.
    ///
    /// ATURAN: setiap method di kelas ini HANYA boleh dipanggil dari thread
    /// utama SolidWorks, lewat MainThreadDispatcher. Lihat CONTRIBUTING.md
    /// aturan 1.
    /// </summary>
    internal sealed class SolidWorksApi
    {
        private readonly dynamic _app;
        private readonly HostDiagnostics _diagnostics;

        internal SolidWorksApi(object application, HostDiagnostics diagnostics)
        {
            _app = application ?? throw new ArgumentNullException(nameof(application));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>Objek COM mentah, untuk dilepas saat disconnect.</summary>
        internal object Raw => _app;

        /// <summary>
        /// Mendaftarkan callback add-in.
        ///
        /// Mencoba <c>SetAddinCallbackInfo2</c> lebih dulu, lalu jatuh ke
        /// <c>SetAddinCallbackInfo</c>. Keduanya ada di versi SolidWorks yang
        /// berbeda, dan kita tidak ingin satu nama yang tidak tersedia
        /// menggagalkan seluruh connect.
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
            }

            try
            {
                _app.SetAddinCallbackInfo(0, addin, cookie);
                _diagnostics.AddError("info", new InvalidOperationException(
                    "SetAddinCallbackInfo2 gagal, memakai SetAddinCallbackInfo versi lama."));
                return true;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("SetAddinCallbackInfo", exception);
                return false;
            }
        }

        /// <summary>Nomor revisi SolidWorks, mis. "34.1.1" untuk SW2026 SP1.1.</summary>
        internal string? TryGetRevision()
        {
            try
            {
                return (string?)_app.RevisionNumber();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("RevisionNumber", exception);
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
                return (int)_app.GetDocumentCount();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("GetDocumentCount", exception);
                return -1;
            }
        }

        /// <summary>
        /// Versi SolidWorks dalam bentuk yang enak dibaca manusia.
        /// Opsional - kalau gagal, bukan masalah.
        /// </summary>
        internal string? TryGetVersionLabel()
        {
            try
            {
                // Tanda tangan: GetBuildNumbers2(out string, out string, out string)
                // bervariasi antarversi, jadi dicoba lewat properti yang lebih
                // sederhana dulu.
                return (string?)_app.RevisionNumber();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
