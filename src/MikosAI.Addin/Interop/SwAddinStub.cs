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
        string GetUserPreferenceStringValue(int UserPreference);
        bool GetUserPreferenceToggle(int UserPreference);
        void SetUserPreferenceToggle(int UserPreference, bool Value);
        object NewDocument(string TemplateName, int PaperSize, double Width, double Height);
        void CloseDoc(string Name);
        object GetOpenDocumentByName(string Name);
        ModelDoc2 OpenDoc6(string FileName, int Type, int Options, string Configuration, ref int Errors, ref int Warnings);
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
        SketchManager SketchManager { get; }
        FeatureManager FeatureManager { get; }
        object GetActiveSketch2();
        void ClearSelection2(bool All);
        object AddHorizontalDimension2(double X, double Y, double Z);
        object AddVerticalDimension2(double X, double Y, double Z);
        void SketchAddConstraints(string Constraint);
        bool EditRebuild3();
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
        bool SelectByID2(string Name, string Type, double X, double Y, double Z, bool Append, int Mark, Callout? Callout, int SelectOption);
        bool SaveAs(string Name, int Version, int Options, object? ExportData, ref int Errors, ref int Warnings);
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
        string Name { get; set; }

        // Direfleksikan: Object GetDefinition()
        // Dipakai membaca ULANG fitur Hole Wizard dari model - bukan dari apa
        // yang kita kira sudah ditulis.
        object GetDefinition();

        bool Select2(bool Append, int Mark);
        object GetFirstSubFeature();
        object GetNextSubFeature();

        // Direfleksikan: Object GetParents() / Object GetChildren()
        //
        // Keduanya mengembalikan Object yang berisi array of Feature. Bentuk
        // JAMAK inilah alasan parentSketches dan dependents berbentuk array -
        // bukan antisipasi loft/sweep. Bentuk tunggal akan MEMBUANG data yang
        // sudah dikembalikan SolidWorks.
        object GetParents();

        object GetChildren();

        // Direfleksikan:
        //   Object GetFirstDisplayDimension()
        //   Object GetNextDisplayDimension(Object DispIn)
        //
        // GetNextDisplayDimension MENERIMA dimensi sebelumnya - ia bukan
        // iterator berstatus. Stub ini sempat mendeklarasikannya tanpa
        // parameter, sehingga compile-check LULUS untuk kode yang ditolak build
        // nyata dengan CS7036.
        //
        // Sebabnya skrip refleksi kami sendiri: ia mencetak "()" secara
        // hardcoded, jadi parameternya tidak pernah terlihat. Itu kesalahan
        // kedua yang lahir dari FORMAT keluaran refleksi, bukan dari
        // refleksinya - yang pertama `out` versus `ref` di CreateCommandGroup2.
        //
        // Dimensi dicacah dari node FITUR, bukan dari ISketch - ISketch memang
        // tidak mengeksposnya.
        object GetFirstDisplayDimension();

        object GetNextDisplayDimension(object DispIn);

        string GetTypeName2();

        bool IsSuppressed();

        int GetErrorCode2(out bool IsWarning);

        object GetNextFeature();

        object GetSpecificFeature2();
    }

    // Direfleksikan dari PIA SolidWorks 2025 SP01.1 pada 2026-11-07.
    public interface IBody2
    {
        bool GetExtremePoint(double X, double Y, double Z,
            out double Outx, out double Outy, out double Outz);
    }

    public interface IPartDoc
    {
        object GetBodies2(int BodyType, bool BVisibleOnly);

        object GetPartBox(bool NoConversion);

        // Tanda tangan direfleksikan dari PIA sungguhan:
        //   String GetMaterialPropertyName2(String ConfigName, out String& Database)
        string GetMaterialPropertyName2(string ConfigName, out string Database);
    }

    public interface IDisplayDimension
    {
        Dimension GetDimension2(int Index);
        // Direfleksikan: Object GetDimension()
        object GetDimension();

        // SENGAJA DISERTAKAN WALAU TIDAK DIPAKAI.
        //
        // IsReferenceDim() bertipe Boolean dan TIDAK bisa menyatakan "tidak
        // tahu", sementara swDimensionDrivenState_e punya
        // swDimensionDrivenUnknown = 0 yang nyata. Kami memakai
        // IDimension.DrivenState justru karena itu.
        //
        // Dibiarkan di stub supaya pilihan itu terlihat: kalau nanti ada yang
        // tergoda memakainya sebagai fallback, ia akan menemukan catatan ini
        // lebih dulu.
        bool IsReferenceDim();
    }

    public interface IDimension
    {
        string FullName { get; }
        double SystemValue { get; }
        int SetSystemValue3(double NewValue, int WhichConfigurations, object? ConfigNames);
        // Direfleksikan: Int32 get_DrivenState()
        //   swDimensionDrivenUnknown = 0
        //   swDimensionDriven        = 1
        //   swDimensionDriving       = 2
        int DrivenState { get; }
    }

    public interface IMathTransform
    {
        // Direfleksikan: Object Inverse()
        //
        // Mengembalikan Object, BUKAN MathTransform. Tanpa cast eksplisit,
        // seluruh ekspresi menjadi dynamic dan build nyata gagal dengan CS0656
        // yang sebabnya tidak kelihatan dari pesannya.
        object Inverse();

        // Direfleksikan: Object get_ArrayData()
        object ArrayData { get; }
    }

    public interface ISketch
    {
        // Direfleksikan: MathTransform ModelToSketchTransform
        //
        // Keberadaannya sendiri bukti bahwa entitas sketsa TIDAK hidup di
        // koordinat model - kalau hidup, transform ini tidak ada gunanya.
        IMathTransform ModelToSketchTransform { get; }

        int GetConstrainedStatus();

        object GetSketchSegments();
        object GetSketchPoints2();

        SketchRelationManager RelationManager { get; }
    }

    // Tanda tangan direfleksikan dari PIA SolidWorks 2025 SP01.1.
    public interface Sketch : ISketch { }
    public interface ISketchSegment
    {
        bool Select4(bool Append, SelectData? Data);
        int GetType();
        object GetID();
        bool ConstructionGeometry { get; }
        Sketch GetSketch();
    }
    public interface ISketchLine
    {
        object GetStartPoint2();
        object GetEndPoint2();
    }
    public interface ISketchPoint
    {
        bool Select4(bool Append, SelectData? Data);
        int Type { get; }
        object GetID();
        double X { get; }
        double Y { get; }
        double Z { get; }
        Sketch GetSketch();
    }
    public interface ISketchRelation
    {
        object GetDefinitionEntities2();
        int GetRelationType();
        object GetEntities();
        object GetEntitiesType();
    }
    public interface ISketchRelationManager
    {
        object GetRelations(int Filter);
        int GetRelationsCount(int Filter);
    }
    public interface SketchRelationManager : ISketchRelationManager { }

    // Tanda tangan jalur tulis direfleksikan dari PIA asli SW2025 SP01.1.
    public interface Callout { }
    public interface SelectData { }
    public interface Dimension : IDimension { }
    public interface ModelDoc2 : IModelDoc2 { }
    public interface SketchManager
    {
        void InsertSketch(bool UpdateEditRebuild);
        object CreateCornerRectangle(double X1,double Y1,double Z1,double X2,double Y2,double Z2);
    }
    public interface FeatureManager : IFeatureManager { }
    public interface IFeatureManager
    {
        // Direfleksikan:
        //   Object  CreateDefinition(Int32 Type)
        //   Feature CreateFeature(Object FeatureData)
        //
        // Jalur ini dipilih menggantikan HoleWizard5, yang punya dua belas
        // parameter Double bernama Value1..Value12 dengan arti yang bergantung
        // tipe lubang.
        object CreateDefinition(int Type);

        Feature CreateFeature(object FeatureData);

        Feature FeatureExtrusion2(bool Sd,bool Flip,bool Dir,int T1,int T2,double D1,double D2,
            bool Dchk1,bool Dchk2,bool Ddir1,bool Ddir2,double Dang1,double Dang2,
            bool OffsetReverse1,bool OffsetReverse2,bool TranslateSurface1,bool TranslateSurface2,
            bool Merge,bool UseFeatScope,bool UseAutoSelect,int T0,double StartOffset,bool FlipStartOffset);
    }
    public interface Feature : IFeature { }

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

    public interface IWizardHoleFeatureData2
    {
        // Mengisi SELURUH dimensi dari basis data Hole Wizard, jadi dua belas
        // Value1..Value12 milik HoleWizard5 tidak perlu disentuh sama sekali.
        void InitializeHole(int GenericHoleType, int StdIndex, int FastnerType, string SSize, int EndType);

        // Akses wajib dibuka sebelum menyentuh properti, dan DILEPAS apa pun
        // hasilnya - akses yang menggantung membuat operasi berikutnya gagal
        // dengan sebab yang tidak nyambung.
        bool AccessSelections(object TopDoc, object Component);

        void ReleaseSelectionAccess();

        // String, BUKAN indeks. Itu yang membuat verifikasi bisa membandingkan
        // "ISO" dan "M6" apa adanya alih-alih menebak apakah indeks 135 memang
        // yang dimaksud.
        string Standard { get; set; }

        string FastenerType { get; set; }

        string FastenerSize { get; set; }

        double CounterBoreDiameter { get; set; }

        double CounterBoreDepth { get; set; }

        double ThruHoleDiameter { get; set; }

        int GetSketchPointCount();

        object GetSketchPoints();
    }
}

namespace SolidWorks.Interop.swconst
{
    // ========================================================================
    // Enum swconst KHUSUS COMPILE-CHECK.
    //
    // Nilainya DIREFLEKSIKAN dari SolidWorks.Interop.swconst.dll asli, bukan
    // diingat. Kode produk memakai SIMBOLNYA, bukan angkanya, supaya build
    // nyata gagal kalau SolidWorks menghapus atau mengganti nama anggotanya -
    // alih-alih menghasilkan indeks yang menunjuk standar lain diam-diam.
    //
    // Hanya anggota yang dipakai yang dideklarasikan. Daftar aslinya jauh lebih
    // panjang: swWzdHoleStandardFastenerTypes_e saja punya 404 nilai.
    // ========================================================================

    public enum swFeatureNameID_e
    {
        swFmHoleWzd = 25,
    }

    public enum swWzdGeneralHoleTypes_e
    {
        swWzdCounterBore = 0,
        swWzdHole = 2,
    }

    public enum swWzdHoleStandards_e
    {
        swStandardAnsiMetric = 1,
        swStandardDIN = 4,
        swStandardISO = 8,
        swStandardJIS = 9,
    }

    public enum swWzdHoleStandardFastenerTypes_e
    {
        swStandardAnsiMetricHexBolt = 29,
        swStandardDINHexBolt = 63,
        swStandardISOHexBolt = 135,
        swStandardJISHexBolt = 152,
    }

    public enum swEndConditions_e
    {
        swEndCondThroughAll = 1,
    }
}

#endif
