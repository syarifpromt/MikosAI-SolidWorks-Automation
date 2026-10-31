#if MIKOSAI_PIA_STUB

namespace SolidWorks.Interop.swpublished
{
    /// <summary>
    /// Stub <c>ISwAddin</c> KHUSUS COMPILE-CHECK.
    ///
    /// Hanya ikut dikompilasi kalau konstanta MIKOSAI_PIA_STUB aktif, yang
    /// hanya terjadi lewat:
    ///
    ///   powershell -File scripts\addin.ps1 -Action CompileCheck
    ///
    /// Gunanya: memverifikasi seluruh kode add-in benar secara tipe di mesin
    /// yang TIDAK punya SolidWorks - termasuk CI dan mesin pengembang yang
    /// belum memasang CAD.
    ///
    /// Build NYATA (-Action Build) memakai PIA asli dari instalasi SolidWorks.
    /// Stub ini TIDAK PERNAH ikut ke build nyata.
    /// </summary>
    public interface ISwAddin
    {
        bool ConnectToSW(object ThisSW, int Cookie);

        bool DisconnectFromSW();
    }
}

namespace SolidWorks.Interop.sldworks
{
    /// <summary>
    /// Stub <c>ISldWorks</c> KHUSUS COMPILE-CHECK.
    ///
    /// HANYA memuat member yang benar-benar kita panggil. Ini bukan dokumentasi
    /// API SolidWorks dan tidak boleh dijadikan acuan - acuannya tetap PIA asli.
    ///
    /// Nilai kembalian setiap member di sini sengaja TIDAK dipakai di call site
    /// kecuali yang benar-benar perlu (RevisionNumber, GetDocumentCount). Itu
    /// membuat ketidakcocokan tanda tangan pada tipe kembalian yang lain tidak
    /// berakibat apa pun saat build nyata.
    ///
    /// Kalau ada tanda tangan di sini yang ternyata berbeda dari PIA asli, build
    /// nyata di mesin bermesin SolidWorks yang akan menangkapnya - dan itu
    /// memang pemeriksaan terakhirnya.
    /// </summary>
    public interface ISldWorks
    {
        bool SetAddinCallbackInfo2(int Reserved, object AddinObj, int Cookie);

        string RevisionNumber();

        int GetDocumentCount();
    }
}

#endif
