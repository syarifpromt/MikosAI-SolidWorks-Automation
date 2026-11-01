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
    // ========================================================================
    // Stub PIA SolidWorks KHUSUS COMPILE-CHECK.
    //
    // BUKAN DOKUMENTASI API. Acuannya tetap PIA asli.
    //
    // Tanda tangan di bawah TIDAK DITEBAK - semuanya dibaca lewat refleksi dari
    // SolidWorks.Interop.sldworks.dll asli (SolidWorks 2025 SP01.1), lalu
    // dipangkas ke member yang benar-benar dipanggil SolidWorksReader.
    //
    // Itu penting: stub yang salah akan membuat compile-check lulus lalu build
    // nyata gagal. Kalau itu terjadi, build nyata yang benar - perbaiki stub,
    // jangan kode.
    //
    // Gunanya: seluruh kode add-in bisa diperiksa tipenya di mesin TANPA
    // SolidWorks. Tanpa ini, lapisan penerjemahan API tidak pernah
    // tersentuh compiler sampai ada yang mencobanya di mesin bermesin CAD.
    //
    // Hanya mendeklarasikan member yang dipakai. Menambah member baru di
    // SolidWorksReader berarti menambahkannya di sini juga - dan itu memang
    // disengaja, supaya permukaan API yang kita pakai tetap kecil dan terlihat.
    // ========================================================================

    public interface ISldWorks
    {
        object ActiveDoc { get; }

        bool SetAddinCallbackInfo2(int Reserved, object AddinObj, int Cookie);

        string RevisionNumber();

        int GetDocumentCount();

        ITaskpaneView CreateTaskpaneView2(string Bitmap, string ToolTip);
    }

    public interface ITaskpaneView
    {
        bool DisplayWindowFromHandlex64(long Handle);

        bool DeleteView();
    }

    public interface IModelDoc2
    {
        string GetTitle();

        string GetPathName();

        int GetType();

        bool GetSaveFlag();

        ConfigurationManager ConfigurationManager { get; }

        ModelDocExtension Extension { get; }

        object FirstFeature();

        object SelectionManager { get; }
    }

    public interface ModelDocExtension
    {
        int NeedsRebuild2 { get; }

        object GetMassProperties2(int Accuracy, out int Status, bool UseSelected);
    }

    public interface ConfigurationManager
    {
        Configuration ActiveConfiguration { get; }
    }

    public interface Configuration
    {
        string Name { get; }
    }

    public interface IFeature
    {
        string Name { get; }

        string GetTypeName2();

        bool IsSuppressed();

        int GetErrorCode2(out bool IsWarning);

        object GetNextFeature();

        object GetSpecificFeature2();
    }

    public interface IPartDoc
    {
        object GetBodies2(int BodyType, bool BVisibleOnly);

        object GetPartBox(bool NoConversion);
    }

    public interface ISketch
    {
        int GetConstrainedStatus();

        object GetSketchSegments();
    }

    public interface ISelectionMgr
    {
        int GetSelectedObjectCount2(int Mark);

        int GetSelectedObjectType3(int Index, int Mark);

        object GetSelectedObject6(int Index, int Mark);
    }

    public interface IFace2
    {
        object Normal { get; }

        object GetBox();
    }

    public interface IEdge
    {
        object GetCurveParams2();
    }
}

#endif
