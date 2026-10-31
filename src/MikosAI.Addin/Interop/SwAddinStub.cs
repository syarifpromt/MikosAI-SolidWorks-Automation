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
    /// belum memasang CAD. Tanpa ini, satu-satunya cara tahu kode add-in
    /// kompilabel adalah mencobanya di mesin bermesin SolidWorks, dan umpan
    /// baliknya lambat.
    ///
    /// Build NYATA (-Action Build) memakai swpublished.dll asli dari instalasi
    /// SolidWorks. Stub ini TIDAK PERNAH ikut ke build nyata, jadi tidak ada
    /// risiko IID atau tanda tangan yang salah sampai ke runtime.
    ///
    /// Tanda tangan di bawah sengaja dibuat seminimal mungkin - hanya dua method
    /// yang benar-benar kita implementasikan. Stub ini bukan dokumentasi API
    /// SolidWorks; acuannya tetap PIA asli.
    /// </summary>
    public interface ISwAddin
    {
        bool ConnectToSW(object ThisSW, int Cookie);

        bool DisconnectFromSW();
    }
}

#endif
