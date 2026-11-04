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

        // Direfleksikan: CommandManager GetCommandManager(Int32 Cookie)
        CommandManager GetCommandManager(int Cookie);
    }

    public interface ITaskpaneView
    {
        bool DisplayWindowFromHandlex64(long Handle);

        bool DeleteView();

        bool ShowView();

        bool HideView();

        // Satu-satunya pertanyaan keadaan yang disediakan SolidWorks. TIDAK
        // ada "apakah panel terlihat" - lihat MikosAiAddin.MikosAiTogglePanel.
        bool IsActiveTab();
    }

    // ------------------------------------------------------------------------
    // CommandManager.
    //
    // Pewarisannya DICOCOKKAN dengan PIA asli, bukan disederhanakan:
    //
    //   CommandManager : ICommandManager
    //   CommandGroup   : ICommandGroup
    //
    // Itu penting. GetCommandManager mengembalikan CommandManager sementara
    // kode kita memegang ICommandManager; kalau stub meratakan keduanya menjadi
    // satu tipe, compile-check akan lulus untuk kode yang tidak bisa dibangun
    // terhadap PIA sungguhan. Stub yang terlalu longgar lebih buruk daripada
    // tidak ada stub, karena ia memberi keyakinan yang salah.
    // ------------------------------------------------------------------------

    public interface ICommandManager
    {
        // Direfleksikan:
        //   CommandGroup CreateCommandGroup2(Int32 UserID, String Title,
        //       String ToolTip, String Hint, Int32 Position,
        //       Boolean IgnorePreviousVersion, ref Int32 Errors)
        //
        // `ref`, BUKAN `out`. Parameternya [In][Out] di PIA asli. Stub ini
        // sempat memakai `out`, sehingga compile-check LULUS untuk kode yang
        // kemudian ditolak build nyata dengan CS1620. Itu persis mode kegagalan
        // yang paling berbahaya dari sebuah stub.
        CommandGroup CreateCommandGroup2(
            int UserID,
            string Title,
            string ToolTip,
            string Hint,
            int Position,
            bool IgnorePreviousVersion,
            ref int Errors);

        // Direfleksikan: Int32 RemoveCommandGroup2(Int32 UserID, Boolean RuntimeOnly)
        int RemoveCommandGroup2(int UserID, bool RuntimeOnly);

        // Tab ribbon CommandManager. TANPA ketiga panggilan ini, tombol hanya
        // muncul di menu Tools dan di toolbar klasik yang tersembunyi - yaitu
        // tidak terlihat sama sekali di SolidWorks modern.
        //
        // Direfleksikan:
        //   CommandTab GetCommandTab(Int32 DocumentType, String TabName)
        //   CommandTab AddCommandTab(Int32 DocumentType, String TabName)
        //   Boolean    RemoveCommandTab(CommandTab TabToRemove)
        CommandTab GetCommandTab(int DocumentType, string TabName);

        CommandTab AddCommandTab(int DocumentType, string TabName);

        bool RemoveCommandTab(CommandTab TabToRemove);
    }

    public interface ICommandTab
    {
        // Direfleksikan: CommandTabBox AddCommandTabBox()
        CommandTabBox AddCommandTabBox();
    }

    public interface CommandTab : ICommandTab
    {
    }

    public interface ICommandTabBox
    {
        // Direfleksikan: Boolean AddCommands(Object CommandIDs, Object TextDisplayStyles)
        //
        // object, BUKAN int[]. Keduanya menyeberang COM sebagai VARIANT berisi
        // SAFEARRAY, dan panjangnya harus sama.
        bool AddCommands(object CommandIDs, object TextDisplayStyles);
    }

    public interface CommandTabBox : ICommandTabBox
    {
    }

    public interface CommandManager : ICommandManager
    {
    }

    public interface ICommandGroup
    {
        // Direfleksikan:
        //   Int32 AddCommandItem2(String Name, Int32 Position, String HintString,
        //       String ToolTip, Int32 ImageListIndex, String CallbackFunction,
        //       String EnableMethod, Int32 UserID, Int32 MenuTBOption)
        int AddCommandItem2(
            string Name,
            int Position,
            string HintString,
            string ToolTip,
            int ImageListIndex,
            string CallbackFunction,
            string EnableMethod,
            int UserID,
            int MenuTBOption);

        bool Activate();

        bool HasToolbar { get; set; }

        bool HasMenu { get; set; }

        // object, BUKAN string[]. PIA asli mendeklarasikannya sebagai Object
        // karena ia menyeberang COM sebagai VARIANT berisi SAFEARRAY.
        object IconList { get; set; }

        object MainIconList { get; set; }

        // ID perintah sebenarnya, dicari dengan indeks yang dikembalikan
        // AddCommandItem2. Dua hal yang berbeda: indeks adalah posisi di dalam
        // grup, ID adalah yang dipahami CommandManager.
        //
        // SATU-SATUNYA TEMPAT STUB INI SENGAJA BERBEDA BENTUK DARI PIA ASLI.
        //
        // Di PIA, CommandID adalah properti BERINDEKS BERNAMA:
        //   Int32 get_CommandID(Int32 CommandIndex)
        //
        // C# bisa MEMAKAI bentuk itu dari COM, tapi tidak bisa
        // MENDEKLARASIKANnya - bahasa ini hanya punya indexer tanpa nama.
        // Jadi stub memakai tipe proksi supaya EKSPRESI pemakaiannya tetap
        // identik: `grup.CommandID[indeks]` terkompilasi sama di kedua jalur.
        //
        // Yang dilindungi compile-check di sini adalah bentuk pemakaiannya,
        // bukan bentuk deklarasinya. Perbedaan ini disebutkan alih-alih
        // disembunyikan, karena stub yang berbeda diam-diam adalah cara stub
        // berbohong.
        PetaIdPerintah CommandID { get; }
    }

    /// <summary>
    /// Proksi untuk properti berindeks bernama ICommandGroup.CommandID.
    /// Lihat catatan panjang di sana. HANYA ada di stub.
    /// </summary>
    public interface PetaIdPerintah
    {
        int this[int CommandIndex] { get; }
    }

    public interface CommandGroup : ICommandGroup
    {
    }

    /// <summary>
    /// ADA DI SINI SEMATA-MATA SEBAGAI JEBAKAN, dan itu disengaja.
    ///
    /// PIA sungguhan punya SolidWorks.Interop.sldworks.Environment. Dengan
    /// `using SolidWorks.Interop.sldworks`, setiap pemakaian `Environment`
    /// tanpa kualifikasi menjadi ambigu (CS0104) - dan itu benar-benar terjadi
    /// pada CommandGroupHost, lolos dari compile-check karena stub tidak punya
    /// tipe ini.
    ///
    /// Jadi tipe ini ADA supaya compile-check memakai System.Environment secara
    /// eksplisit, sama seperti build nyata menuntutnya. Stub yang lebih bersih
    /// daripada kenyataan bukan stub yang lebih baik.
    /// </summary>
    public interface Environment
    {
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

        // Tanda tangan direfleksikan dari PIA sungguhan:
        //   String GetMaterialPropertyName2(String ConfigName, out String& Database)
        string GetMaterialPropertyName2(string ConfigName, out string Database);
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
