using System;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using SolidWorks.Interop.swpublished;

namespace MikosAI.Addin
{
    /// <summary>
    /// Add-in MikosAI.
    ///
    /// TAHAP 1 - SENGAJA MINIMAL.
    ///
    /// Tujuan satu-satunya versi ini: menjawab pertanyaan yang paling berisiko
    /// dan belum terjawab - apakah add-in benar-benar diinstansiasi oleh
    /// SLDWORKS.exe, dan apakah thread utamanya bisa ditangkap.
    ///
    /// Yang BELUM ada, dan itu disengaja: CommandGroup, TaskPane, WebView2,
    /// PropertyManagerPage, MCP server. Semuanya menyentuh permukaan API yang
    /// jauh lebih luas, jadi ditambahkan setelah fondasi ini terbukti hidup.
    /// Menambahkannya sekarang hanya memperbanyak kemungkinan penyebab kalau
    /// gagal.
    ///
    /// Satu-satunya tipe PIA yang dipakai saat kompilasi adalah ISwAddin.
    /// Semua panggilan SolidWorks lain late-bound lewat SolidWorksApi, supaya
    /// tanda tangan API yang berbeda antarversi jadi error terstruktur di
    /// host-status.json, bukan error compiler di mesin pengguna.
    ///
    /// Jangan percaya LoadAddIn=0. Periksa
    /// %LOCALAPPDATA%\MikosAI\host-status.json.
    /// </summary>
    [ComVisible(true)]
    [Guid(AddinGuid)]
    //
    // AutoDispatch, BUKAN None. Ini bukan pilihan gaya.
    //
    // Dengan ClassInterfaceType.None, kelas ini hanya mengekspos interface yang
    // diimplementasikan secara eksplisit - ISwAddin. Itu cukup untuk SolidWorks
    // memanggil ConnectToSW, dan memang berhasil. TAPI objek ini juga diserahkan
    // ke SolidWorks lewat SetAddinCallbackInfo2 sebagai target callback, dan
    // SolidWorks memanggil callback BERDASARKAN NAMA METHOD - jadi ia butuh
    // IDispatch.
    //
    // Tanpa IDispatch, SetAddinCallbackInfo2 gagal dengan:
    //   InvalidCastException: Specified cast is not valid.
    //
    // yang terjadi di dalam SolidWorks saat ia meng-cast objek kita ke IDispatch.
    //
    // AutoDispatch membangkitkan dispinterface saat runtime sehingga pemanggilan
    // late-bound berdasarkan nama bisa jalan. AutoDual juga memberi IDispatch
    // tapi sekaligus membekukan layout vtable, yang rapuh antarversi - jadi
    // tidak dipakai.
    //
    // Ini kelas jebakan yang sama dengan yang didokumentasikan untuk
    // PropertyManagerPage handler: handler yang hanya menyediakan IUnknown
    // tanpa IDispatch membuat SolidWorks melempar InvalidCastException.
    //
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    [ProgId("MikosAI.Addin.MikosAiAddin")]
    public sealed class MikosAiAddin : ISwAddin
    {
        /// <summary>
        /// GUID add-in. HARUS STABIL setelah rilis pertama - layout
        /// CommandManager dan preferensi pengguna terikat padanya.
        /// </summary>
        internal const string AddinGuid = "7F2A6C14-9D3B-4E58-B1A7-5C0E9D4F2B63";

        private const string AddinTitle = "MikosAI";
        private const string AddinDescription = "Otomasi SolidWorks berbantuan AI, dioperasikan dari dalam SolidWorks.";

        /// <summary>
        /// Memasang resolver dependensi SEBELUM apa pun yang lain.
        ///
        /// Static constructor berjalan sebelum instance pertama dibuat, jadi
        /// sebelum field initializer di bawah dieksekusi. Urutan itu WAJIB.
        ///
        /// Add-in kita didaftarkan dengan /codebase, dan dependensi dari
        /// assembly yang dimuat lewat codebase dicari relatif ke app base HOST -
        /// folder SLDWORKS.exe, bukan folder kita. Tanpa resolver ini, memuat
        /// tipe apa pun yang menyentuh MikosAI.Core akan gagal, SolidWorks
        /// diam-diam tidak menginstansiasi add-in, dan tidak ada satu pun pesan
        /// error.
        ///
        /// JANGAN pindahkan pemasangan ini ke constructor instance.
        /// </summary>
        static MikosAiAddin()
        {
            // Penangkap crash lebih dulu: kalau resolver sendiri yang meledak,
            // kita tetap ingin tahu.
            PenangkapCrash.Pasang();
            AssemblyResolver.Pasang();
        }

        private readonly HostDiagnostics _diagnostics = new HostDiagnostics();

        private SolidWorksApi? _sw;
        private MainThreadDispatcher? _dispatcher;
        private McpHost? _mcp;
        private SolidWorksReader? _pembaca;
        private BlockPilotHost? _blockPilot;
        private MikosAI.Core.Plan.ParametricBlockFlow? _blockFlow;
        private int _chatInProgress;
        private TaskPaneHost? _panel;
        private CommandGroupHost? _tombol;
        private ProviderHost? _provider;
        private System.Windows.Forms.Timer? _timerSegar;

        /// <summary>
        /// Jumlah dokumen terbuka, disegarkan timer di thread utama.
        /// -1 berarti belum/tidak terbaca - BUKAN nol.
        /// </summary>
        private int _jumlahDokumenCache = -1;
        private SolidWorks.Interop.sldworks.ITaskpaneView? _taskPaneView;

        // ====================================================================
        // ISwAddin
        // ====================================================================

        /// <summary>
        /// Dipanggil SolidWorks saat add-in dimuat, pada thread STA utama.
        ///
        /// Setiap langkah dibungkus try/catch sendiri. Alasannya: exception yang
        /// lolos dari ConnectToSW tidak meninggalkan jejak apa pun - SolidWorks
        /// hanya diam-diam tidak memuat add-in. Dengan menangkap per langkah,
        /// kita selalu punya host-status.json yang menyebut langkah mana yang
        /// gagal.
        /// </summary>
        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            _diagnostics.Status = "connecting";
            _diagnostics.AddinCookie = Cookie;
            _diagnostics.FeatureFlags = Fitur.Ringkasan();
            _diagnostics.Jejak("connect:mulai");

            // --- Tangkap thread utama ---------------------------------------
            // Dikerjakan DI SINI dan tidak di tempat lain. Ini satu-satunya
            // titik yang dijamin berjalan di thread utama SolidWorks.
            //
            // HARUS mendahului SolidWorksApi: pembungkus API memakai dispatcher
            // untuk menegakkan aturan thread, jadi tanpa dispatcher ia tidak
            // bisa melindungi apa pun.
            try
            {
                _dispatcher = new MainThreadDispatcher();
                _diagnostics.DispatcherReady = true;
                _diagnostics.MainThreadId = _dispatcher.MainThreadId;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("MainThreadDispatcher", exception);
            }

            if (_dispatcher is null)
            {
                // Tanpa dispatcher, setiap panggilan API berisiko menjatuhkan
                // SolidWorks. Lebih baik menolak dimuat.
                _diagnostics.AddError(
                    "Dispatcher thread utama tidak bisa dibuat - add-in menolak dimuat "
                    + "daripada memanggil API SolidWorks tanpa perlindungan thread.");
                _diagnostics.Status = "failed";
                try { _diagnostics.Write(); } catch (Exception) { }
                return false;
            }

            // --- Simpan referensi aplikasi ----------------------------------
            _diagnostics.Jejak("connect:bungkus-api");
            _sw = SolidWorksApi.Buat(ThisSW, _diagnostics, _dispatcher);

            if (_sw is null)
            {
                _diagnostics.Status = "failed";
                try { _diagnostics.Write(); } catch (Exception) { }
                return false;
            }

            // --- Periksa sendiri apakah kita mengekspos IDispatch -----------
            // SolidWorks memanggil callback berdasarkan nama, jadi IDispatch
            // wajib. Diuji di sini supaya kegagalan SetAddinCallbackInfo2
            // langsung bisa dijelaskan, bukan jadi InvalidCastException misterius
            // yang terjadi di dalam SolidWorks.
            _diagnostics.AddinExposesIDispatch = UjiIDispatch();

            // --- Daftarkan callback -----------------------------------------
            try
            {
                _diagnostics.CallbackRegistered = _sw.TryRegisterCallback(this, Cookie);
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("TryRegisterCallback", exception);
            }

            // --- Bukti bahwa API benar-benar bisa dipanggil -----------------
            try
            {
                _diagnostics.SolidWorksRevision = _sw.TryGetRevision();
                _diagnostics.OpenDocumentCount = _sw.TryGetDocumentCount();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("baca properti aplikasi", exception);
            }

            // --- Pembaca model ----------------------------------------------
            try
            {
                _pembaca = _sw.BuatPembaca(_diagnostics);
                _blockPilot = new BlockPilotHost(() => (_sw ?? throw new InvalidOperationException("SolidWorks tidak terhubung"))
                    .BuatPenulisBalok(_diagnostics,_pembaca ?? throw new InvalidOperationException("Reader tidak tersedia")));
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("membuat SolidWorksReader", exception);
            }

            // --- Nyalakan server MCP ----------------------------------------
            // Di dalam proses SolidWorks, jadi tool handler memanggil executor
            // lewat pemanggilan fungsi biasa. Lihat docs/adr/0002.
            try
            {
                if (!Fitur.PakaiMcp)
                {
                    _diagnostics.AddError("info: server MCP dimatikan lewat saklar fitur.");
                }
                else
                {
                    _diagnostics.Jejak("connect:mcp-mulai");

                    // Executor nyata: setiap pembacaan dijalankan lewat dispatcher
                    // ke thread utama SolidWorks.
                    var root=McpHost.CariAkarRepo() ?? throw new InvalidOperationException("Akar aplikasi tidak ditemukan.");
                    var blockCatalog=MikosAI.Core.Tools.ToolCatalog.Load(System.IO.Path.Combine(root,"schemas","tools.json"),
                        MikosAI.Core.Capabilities.CapabilityLedger.Load(System.IO.Path.Combine(root,"capabilities.yaml")));
                    _blockFlow=new MikosAI.Core.Plan.ParametricBlockFlow(blockCatalog,
                        (w,h,d,holes,path)=>new NativeBlockSession(_dispatcher!,
                            ()=>_sw!.BuatPenulisBalok(_diagnostics,_pembaca!),()=>_pembaca,w,h,d,holes,path,
                            blockCatalog.RequiresReview(MikosAI.Core.Plan.ParametricBlockFlow.ToolName),_diagnostics),
                        id=>System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),"MikosAI","Models","block-"+id+".SLDPRT"));
                    var executor = new SolidWorksToolExecutor(
                        _dispatcher ?? throw new InvalidOperationException("dispatcher belum siap"),
                        () => _pembaca,_blockFlow);

                    _mcp = new McpHost(_diagnostics, executor);
                    _diagnostics.McpServer = _mcp.Mulai();
                    _diagnostics.McpEndpoint = _mcp.Endpoint?.ToString();
                    _diagnostics.McpToolCount = _mcp.ToolCount;
                    _diagnostics.McpWorkspace = _mcp.WorkspaceDirectory;
                    _diagnostics.Jejak("connect:mcp-selesai");
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("McpHost", exception);
            }

            // --- Periksa provider AI ----------------------------------------
            try
            {
                if (!Fitur.PakaiProvider)
                {
                    _diagnostics.AddError("info: provider AI dimatikan lewat saklar fitur.");
                }
                else
                {
                    _diagnostics.Jejak("connect:provider-mulai");
                    _provider = new ProviderHost(_diagnostics);

                    // Tidak di-await: ConnectToSW harus kembali cepat.
                    // Memeriksa provider lalu mendaftarkan server MCP kita,
                    // berurutan - pendaftaran butuh adapter yang sudah
                    // terdeteksi.
                    _ = PeriksaDanDaftarkanAsync();
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("ProviderHost", exception);
            }

            // --- Bangun TaskPane --------------------------------------------
            try
            {
                if (!Fitur.PakaiTaskPane)
                {
                    _diagnostics.AddError("info: TaskPane dan WebView2 dimatikan lewat saklar fitur.");
                }
                else
                {
                    _diagnostics.Jejak("connect:taskpane-mulai");
                    _diagnostics.TaskPane = BangunTaskPane();
                    _diagnostics.Jejak("connect:taskpane-selesai");
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("BangunTaskPane", exception);
            }

            // --- Tombol toolbar ---------------------------------------------
            // SETELAH TaskPane, dan itu wajib: callback tombol memanggil
            // _taskPaneView, jadi membangunnya lebih dulu berarti membuat
            // tombol yang bisa diklik sebelum ada yang bisa ditampilkan.
            try
            {
                if (!Fitur.PakaiCommandGroup)
                {
                    _diagnostics.AddError("info: tombol toolbar dimatikan lewat saklar fitur.");
                }
                else if (_taskPaneView is null)
                {
                    // Bukan "info": tombolnya memang hilang, dan pengguna akan
                    // mencarinya. Disebutkan beserta sebabnya.
                    _diagnostics.AddError(
                        "tombol toolbar tidak dibangun karena TaskPane gagal dibuat - "
                        + "tidak ada panel yang bisa ditampilkannya.");
                }
                else
                {
                    _diagnostics.Jejak("connect:commandgroup-mulai");
                    _tombol = new CommandGroupHost(_diagnostics);
                    _diagnostics.CommandGroup = _tombol.Bangun(
                        _sw.TryGetCommandManager(Cookie), AddinTitle);
                    _diagnostics.Jejak("connect:commandgroup-selesai");
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("membangun CommandGroup", exception);
            }

            _diagnostics.Status = "connected";
            _diagnostics.Jejak("connect:selesai");

            // Pengaman terluar. Kegagalan di sini - mis. JIT tidak bisa
            // menyelesaikan assembly - akan lolos keluar dari ConnectToSW,
            // dan SolidWorks akan membuang add-in TANPA jejak apa pun. Itu
            // mode kegagalan terburuk yang mungkin, jadi tidak boleh terjadi.
            try
            {
                _diagnostics.Write();
            }
            catch (Exception)
            {
                // Diagnostik tidak bisa ditulis. Add-in tetap terhubung, jadi
                // lebih baik lanjut daripada menolak dimuat.
            }

            return true;
        }

        /// <summary>
        /// Dipanggil SolidWorks saat add-in dilepas.
        ///
        /// Melepas referensi COM penting di sini: referensi yang menggantung
        /// membuat SolidWorks tidak bisa keluar dengan bersih, dan proses yang
        /// tertinggal akan mengunci DLL kita sehingga rebuild berikutnya gagal.
        /// </summary>
        public bool DisconnectFromSW()
        {
            // Tombol dilepas PALING dulu: callback-nya menyentuh _taskPaneView,
            // jadi ia harus berhenti bisa dipanggil sebelum view itu dihapus.
            // Urutan sebaliknya membuka jendela sempit di mana klik pengguna
            // mendarat di view yang sudah mati.
            try
            {
                _tombol?.Dispose();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("melepas CommandGroup", exception);
            }
            finally
            {
                _tombol = null;
                _diagnostics.CommandGroup = false;
            }

            // TaskPane dilepas berikutnya: ia memegang handle jendela yang
            // dimiliki SolidWorks, dan melepasnya setelah COM di-release bisa
            // membuat SolidWorks mengakses jendela yang sudah mati.
            try
            {
                _taskPaneView?.DeleteView();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("DeleteView TaskPane", exception);
            }
            finally
            {
                _taskPaneView = null;
            }

            try
            {
                if (_timerSegar is not null)
                {
                    _timerSegar.Stop();
                    _timerSegar.Tick -= SegarkanCache;
                    _timerSegar.Dispose();
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menghentikan timer penyegar", exception);
            }
            finally
            {
                _timerSegar = null;
            }

            try
            {
                _panel?.Dispose();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("dispose panel", exception);
            }
            finally
            {
                _panel = null;
                _diagnostics.TaskPane = false;
            }

            try
            {
                // Lepas pendaftaran sebelum server MCP dimatikan, supaya tidak
                // ada entri basi yang menunjuk port mati.
                //
                // DisconnectFromSW berjalan di thread utama SolidWorks, dan
                // operasi ini menjalankan subprocess. Dijalankan di thread pool
                // dengan batas waktu pendek: memblokir thread utama saat
                // SolidWorks menutup bisa membuatnya menggantung, dan
                // GetAwaiter().GetResult() di atas SynchronizationContext
                // WinForms adalah resep deadlock.
                //
                // Kalau lewat batas waktu, entri basi dibiarkan - entri itu akan
                // tertimpa saat sesi berikutnya mendaftar ulang.
                var provider = _provider;
                if (provider is not null)
                {
                    System.Threading.Tasks.Task
                        .Run(() => provider.LepasMcpAsync())
                        .Wait(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("melepas pendaftaran MCP", exception);
            }

            try
            {
                PenangkapCrash.Lepas();
            }
            catch (Exception)
            {
                // Abaikan saat menutup.
            }

            try
            {
                _provider?.Dispose();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("dispose ProviderHost", exception);
            }
            finally
            {
                _provider = null;
            }

            try
            {
                _mcp?.Dispose();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("dispose McpHost", exception);
            }
            finally
            {
                _mcp = null;
                _pembaca = null;
                _diagnostics.McpServer = false;
                _diagnostics.McpEndpoint = null;
            }

            try
            {
                _dispatcher?.Dispose();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("dispose dispatcher", exception);
            }
            finally
            {
                _dispatcher = null;
                _diagnostics.DispatcherReady = false;
            }

            try
            {
                if (_sw is not null && Marshal.IsComObject(_sw.Raw))
                {
                    Marshal.ReleaseComObject(_sw.Raw);
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("release ISldWorks", exception);
            }
            finally
            {
                _sw = null;
            }

            _diagnostics.Status = "disconnected";
            _diagnostics.OpenDocumentCount = -1;
            _diagnostics.Write();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            return true;
        }

        /// <summary>
        /// Membuat TaskPane dan menempelkan panel WebView2 ke dalamnya.
        ///
        /// Dijalankan di thread utama SolidWorks, karena ConnectToSW berjalan di
        /// sana - dan kontrol WinForms harus dibuat di thread yang akan
        /// memompa pesannya.
        /// </summary>
        private bool BangunTaskPane()
        {
            if (_sw is null)
            {
                return false;
            }

            var bitmap = TaskPaneHost.BuatBitmapIkon(_diagnostics);
            if (bitmap is null)
            {
                return false;
            }

            _panel = new TaskPaneHost(_diagnostics, StatusUntukPanel, TanganiChat);

            // Handle harus sudah ada sebelum diserahkan ke SolidWorks.
            var handle = _panel.Handle;

            _taskPaneView = _sw.TryCreateTaskPane(bitmap, AddinTitle, handle);

            if (_taskPaneView is null)
            {
                return false;
            }

            // WebView2 baru dibuat SEKARANG - setelah SolidWorks memasang
            // kontrolnya. Membuatnya lebih dulu berarti mengikatnya ke handle
            // yang bisa dibuat ulang saat reparent, dan itu menjatuhkan
            // SolidWorks. Lihat TaskPaneHost.Pasang().
            _panel.Pasang();

            // Timer WinForms: tick-nya tiba sebagai WM_TIMER di thread utama,
            // bukan dari dalam callback WebView2. Inilah satu-satunya tempat
            // yang aman untuk membaca model demi panel.
            try
            {
                _timerSegar = new System.Windows.Forms.Timer { Interval = 4000 };
                _timerSegar.Tick += SegarkanCache;
                _timerSegar.Start();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menyalakan timer penyegar", exception);
            }

            return true;
        }

        /// <summary>
        /// Status yang dikirim ke panel UI.
        ///
        /// Dibangun ulang setiap permintaan, bukan di-cache - panel menampilkan
        /// keadaan sekarang, dan jumlah dokumen bisa berubah kapan saja.
        /// </summary>
        private JsonObject StatusUntukPanel()
        {
            // SENGAJA memakai nilai CACHE, bukan memanggil API SolidWorks.
            //
            // Method ini dipanggil dari handler pesan WebView2 - yaitu dari
            // DALAM message pump milik WebView2. Memanggil COM SolidWorks dari
            // sana berarti masuk ke COM lewat message loop bersarang, dan
            // SolidWorks tidak tahan terhadap re-entrancy seperti itu: ia mati
            // di kode native, tanpa exception, dengan Fault Module kosong.
            //
            // Cache-nya disegarkan oleh timer WinForms, yang tiba sebagai pesan
            // WM_TIMER biasa di thread utama - bukan panggilan bersarang.
            var dokumen = _jumlahDokumenCache;

            var galat = new JsonArray();
            foreach (var pesan in _diagnostics.Errors)
            {
                galat.Add(pesan);
            }

            return new JsonObject
            {
                ["status"] = _diagnostics.Status,
                ["solidworksRevision"] = _diagnostics.SolidWorksRevision,
                ["openDocumentCount"] = dokumen,
                ["dispatcherReady"] = _diagnostics.DispatcherReady,
                ["mainThreadId"] = _diagnostics.MainThreadId,
                ["mcpRunning"] = _mcp?.IsRunning == true,
                ["mcpEndpoint"] = _mcp?.Endpoint?.ToString(),
                ["mcpToolCount"] = _mcp?.ToolCount ?? 0,
                ["mcpWorkspace"] = _mcp?.WorkspaceDirectory,
                ["provider"] = RingkasanProvider(),
                ["blockAuthoringReady"] = _blockFlow!=null && _diagnostics.Status=="connected",
                ["chatBusy"] = System.Threading.Volatile.Read(ref _chatInProgress)!=0,
                ["buildCommit"] = _diagnostics.BuildCommit,
                ["buildTime"] = _diagnostics.BuildTime,
                ["errors"] = galat,
            };
        }

        /// <summary>
        /// Memeriksa provider, lalu mendaftarkan server MCP kita ke
        /// konfigurasinya.
        ///
        /// Berjalan di latar supaya ConnectToSW kembali cepat. Kegagalan di
        /// sini tidak boleh menggagalkan pemuatan add-in - add-in tetap berguna
        /// walaupun AI belum tersambung.
        /// </summary>
        private async System.Threading.Tasks.Task PeriksaDanDaftarkanAsync()
        {
            try
            {
                if (_provider is null)
                {
                    return;
                }

                _diagnostics.Jejak("latar:probe-agy-mulai");
                await _provider.PeriksaAsync().ConfigureAwait(false);
                _diagnostics.Jejak("latar:probe-agy-selesai");

                var endpoint = _mcp?.Endpoint;
                var token = _mcp?.SessionToken;

                if (endpoint is not null && !string.IsNullOrEmpty(token))
                {
                    _diagnostics.Jejak("latar:daftar-mcp-mulai");
                    await _provider.DaftarkanMcpAsync(endpoint, token!).ConfigureAwait(false);
                    _diagnostics.Jejak("latar:daftar-mcp-selesai");
                }

                _diagnostics.Jejak("latar:kirim-status");
                _panel?.KirimStatus();
                _diagnostics.Jejak("latar:selesai");
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("memeriksa dan mendaftarkan provider", exception);
            }
        }

        /// <summary>
        /// Menyegarkan nilai yang dibaca dari model, di thread utama lewat
        /// WM_TIMER.
        ///
        /// Pembacaan model TIDAK BOLEH terjadi di jalur lain - lihat catatan di
        /// StatusUntukPanel.
        /// </summary>
        private void SegarkanCache(object? pengirim, EventArgs e)
        {
            // Request manual golden test diproses melalui dispatcher yang sama;
            // tidak dipicu oleh tool read-only dan tidak diekspos kepada AI.
            if (_dispatcher != null && _blockPilot != null)
            {
                var task=_dispatcher.InvokeAsync(() => _blockPilot.Poll());
                if (task.IsFaulted) _diagnostics.AddError("pilot balok",task.Exception!);
            }
            try
            {
                _jumlahDokumenCache = _sw?.TryGetDocumentCount() ?? -1;
            }
            catch (Exception)
            {
                _jumlahDokumenCache = -1;
            }

            try
            {
                _panel?.KirimStatus();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menyegarkan panel", exception);
            }
        }

        private JsonObject? RingkasanProvider()
        {
            if (_provider is null)
            {
                return null;
            }

            var objek = new JsonObject();

            foreach (var pasangan in _provider.Ringkasan())
            {
                // Bool dipertahankan sebagai bool - panel memakainya untuk
                // mengaktifkan kotak chat, dan string "False" itu truthy di JS.
                objek[pasangan.Key] = pasangan.Value switch
                {
                    null => null,
                    bool b => JsonValue.Create(b),
                    _ => JsonValue.Create(pasangan.Value.ToString()),
                };
            }

            return objek;
        }

        /// <summary>
        /// Menangani satu pesan chat dari panel.
        ///
        /// Selama adapter provider belum terpasang, ini menjawab dengan
        /// kegagalan yang MENJELASKAN keadaannya - bukan error generik, dan
        /// bukan pura-pura bekerja.
        /// </summary>
        private async System.Threading.Tasks.Task TanganiChat(string prompt)
        {
            if(System.Threading.Interlocked.Exchange(ref _chatInProgress,1)!=0)
            {
                _panel?.KirimPesanChat("ai","Permintaan sebelumnya masih berjalan. Tunggu hasilnya sebelum mengirim perintah baru.");
                return;
            }
            try
            {
                var block=MikosAI.Core.Plan.BlockChatRequest.Parse(prompt);
                if(block.Handled)
                {
                    if(!block.Valid) { _panel?.KirimPesanChat("ai",block.Message); return; }
                    if(_blockFlow is null) { _panel?.KirimPesanChat("galat","Jalur balok belum siap. Periksa status add-in."); return; }
                    var benda=block.Holes is null ? "balok" : "plat";
                    var keteranganLubang=block.Holes is null ? string.Empty
                        : $" dengan {block.Holes.Positions.Count} lubang {block.Holes.FastenerSize} {block.Holes.HoleType} {block.Holes.Standard}";
                    _panel?.KirimPesanChat("ai",$"Membuat part baru: {benda} {block.Width} × {block.Height} × {block.Depth} mm{keteranganLubang}, lalu memverifikasi hasil simpan–buka ulang...");
                    var argumen=new JsonObject{["width"]=block.Width,["height"]=block.Height,["depth"]=block.Depth,["operationId"]=Guid.NewGuid().ToString("N")};
                    if(block.Holes is not null)
                    {
                        // Lewat flow yang SAMA dengan tool MCP, bukan jalur
                        // terpisah - supaya aturan yang menolak posisi di luar
                        // permukaan atas atau standar yang belum terbukti hidup
                        // di satu tempat.
                        var posisi=new JsonArray();
                        foreach(var t in block.Holes.Positions) posisi.Add(new JsonObject{["x"]=t.X,["y"]=t.Y,["z"]=t.Z});
                        argumen["holes"]=new JsonObject{["holeType"]=block.Holes.HoleType,["standard"]=block.Holes.Standard,
                            ["fastenerSize"]=block.Holes.FastenerSize,["positions"]=posisi};
                    }
                    var result=await _blockFlow.ExecuteAsync(argumen).ConfigureAwait(false);
                    if(result.Ok && result.Verification?.Verdict==MikosAI.Core.Execution.Verdict.Pass)
                        _panel?.KirimPesanChat("ai",$"{(block.Holes is null ? "Balok" : "Plat")} {block.Width} × {block.Height} × {block.Depth} mm{keteranganLubang} sudah dibuat. "
                            +(block.Holes is null ? "Ukuran, volume, constraint" : "Ukuran, posisi lubang, standar dan ukuran fastener")+$" dan hasil simpan–buka ulang cocok.\nTersimpan: {result.Evidence?.DocumentPath}"
                            +(result.Verification.ManualReviewRequired ? "\nHasil pilot: periksa part ini sebelum menganggap pekerjaan final." : string.Empty));
                    else _panel?.KirimPesanChat("galat",$"Pembuatan {benda} berhenti: {result.Error?.Message ?? result.Verification?.Verdict.ToString()}. Part yang sempat dibuat dibiarkan untuk diperiksa; tidak diulang otomatis.");
                    return;
                }
                if (_provider is null)
                {
                    _panel?.KirimPesanChat("galat", "Provider AI belum siap.");
                    return;
                }

                // Gelembung jawaban dibuat lebih dulu, lalu diisi bertahap
                // dari text_delta. Chat jadi terasa hidup alih-alih diam lalu
                // tiba-tiba selesai.
                var adaPotongan = false;

                void Potongan(string teks)
                {
                    if (!adaPotongan)
                    {
                        adaPotongan = true;
                        _panel?.MulaiPesanChat("ai");
                    }

                    _panel?.TambahPotonganChat(teks);
                }

                var hasil = await _provider
                    .JalankanAsync(prompt, _mcp?.WorkspaceDirectory ?? string.Empty, Potongan)
                    .ConfigureAwait(false);

                if (hasil.IsSuccessful)
                {
                    if (adaPotongan)
                    {
                        // Sudah tampil bertahap; tinggal ditutup.
                        _panel?.SelesaikanPesanChat();
                    }
                    else
                    {
                        _panel?.KirimPesanChat("ai", hasil.Summary ?? "(tidak ada jawaban)");
                    }

                    return;
                }

                // Gagal di tengah aliran: tutup gelembungnya supaya teks parsial
                // tidak menggantung tanpa penjelasan.
                if (adaPotongan)
                {
                    _panel?.SelesaikanPesanChat();
                }

                var pesan = hasil.Failure!.Message;

                if (!string.IsNullOrWhiteSpace(hasil.Failure.NextAction))
                {
                    pesan += Environment.NewLine + Environment.NewLine + hasil.Failure.NextAction;
                }

                _panel?.KirimPesanChat("galat", pesan);
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menangani chat", exception);
                _panel?.KirimPesanChat("galat", "Terjadi kesalahan internal: " + exception.Message);
            }
            finally { System.Threading.Interlocked.Exchange(ref _chatInProgress,0); }
        }

        // ====================================================================
        // Callback tombol toolbar
        //
        // DUA METHOD DI BAWAH DIPANGGIL SOLIDWORKS BERDASARKAN NAMA, lewat
        // IDispatch, memakai string yang diberikan ke AddCommandItem2. Compiler
        // tidak pernah melihat hubungan itu.
        //
        // Konsekuensinya konkret:
        //   - harus public, dan harus instance method di kelas yang diserahkan
        //     ke SetAddinCallbackInfo2 - yaitu kelas INI
        //   - mengganti namanya tanpa mengganti string akan menghasilkan tombol
        //     yang DIAM, bukan error build
        //
        // Karena itu CommandGroupHost memakai nameof(...) untuk kedua string,
        // dan karena itu pula namanya diawali "MikosAi": ruang nama callback
        // ini DATAR dan dibagi dengan seluruh add-in lain di proses yang sama.
        // Nama generik seperti "TogglePanel" adalah tabrakan yang menunggu
        // terjadi, dan tabrakannya akan tampak sebagai tombol add-in lain yang
        // memanggil kode kita.
        //
        // Keduanya TIDAK BOLEH melempar: exception yang lolos ke SolidWorks
        // lewat IDispatch tidak meninggalkan jejak apa pun.
        // ====================================================================

        /// <summary>
        /// Menampilkan atau menyembunyikan panel. Dipanggil dari tombol.
        /// </summary>
        public void MikosAiTogglePanel()
        {
            try
            {
                if (_taskPaneView is null)
                {
                    _diagnostics.AddError("tombol panel diklik, tapi TaskPane tidak ada.");
                    return;
                }

                // Tidak ada API "apakah panel terlihat". IsActiveTab() adalah
                // yang paling dekat dengan kebenaran: ia menjawab apakah TAB
                // KITA yang sedang aktif.
                //
                // Jadi perilakunya: kalau tab kita aktif, sembunyikan; kalau
                // tidak - entah tersembunyi, entah tab add-in lain yang tampil
                // - tampilkan milik kita. Itu yang diharapkan orang dari satu
                // tombol, dan ia tidak pernah berbohong tentang keadaan.
                if (_taskPaneView.IsActiveTab())
                {
                    _taskPaneView.HideView();
                }
                else
                {
                    _taskPaneView.ShowView();
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("callback tombol panel", exception);
            }
        }

        /// <summary>
        /// Keadaan tombol, diminta SolidWorks setiap kali toolbar digambar.
        ///
        /// Nilai kembaliannya mengikuti konvensi SolidWorks:
        /// 0 = mati, 1 = hidup, 2 = tertekan + mati, 3 = tertekan + hidup.
        ///
        /// Dipakai untuk menunjukkan apakah panel sedang tampil, sehingga
        /// tombolnya memberi tahu keadaan alih-alih hanya bisa diklik.
        /// </summary>
        public int MikosAiStatusTombolPanel()
        {
            try
            {
                if (_taskPaneView is null)
                {
                    return 0;
                }

                return _taskPaneView.IsActiveTab() ? 3 : 1;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("callback status tombol panel", exception);

                // Tetap bisa diklik. Tombol yang mati gara-gara pembacaan
                // keadaan gagal akan menutup satu-satunya jalan pengguna ke
                // panel, padahal panelnya sendiri mungkin sehat.
                return 1;
            }
        }

        /// <summary>
        /// Memastikan objek ini bisa di-cast ke IDispatch oleh COM.
        ///
        /// Kalau false, SetAddinCallbackInfo2 PASTI gagal dengan
        /// InvalidCastException, dan penyebabnya adalah atribut ClassInterface -
        /// bukan sesuatu di sisi SolidWorks.
        /// </summary>
        private bool UjiIDispatch()
        {
            var pointer = IntPtr.Zero;
            try
            {
                pointer = Marshal.GetIDispatchForObject(this);
                return pointer != IntPtr.Zero;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError(
                    "objek add-in tidak mengekspos IDispatch - periksa atribut ClassInterface",
                    exception);
                return false;
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    Marshal.Release(pointer);
                }
            }
        }

        // ====================================================================
        // Registrasi COM
        // ====================================================================
        //
        // RegAsm memanggil method ini. Keduanya WAJIB ada, dan harus menulis
        // kunci yang dicari SolidWorks saat memindai add-in.
        //
        // Catatan lapangan: registrasi CurrentUser hanya membuktikan aktivasi
        // CLR/COM. Supaya SolidWorks benar-benar memuat add-in, registrasi
        // Machine (HKLM) diperlukan - dan TLB harus ikut terdaftar, karena tanpa
        // TLB SolidWorks bisa memuat DLL, mengembalikan LoadAddIn=0, lalu tidak
        // pernah menginstansiasi ISwAddin.

        [ComRegisterFunction]
        public static void RegisterFunction(Type type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            var guidKey = $"{{{type.GUID.ToString().ToUpperInvariant()}}}";

            using (var addinKey = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\SolidWorks\Addins\{guidKey}"))
            {
                if (addinKey is null)
                {
                    throw new InvalidOperationException(
                        "Tidak bisa menulis HKLM\\SOFTWARE\\SolidWorks\\Addins. " +
                        "Registrasi Machine butuh hak administrator.");
                }

                // Nilai default 0 berarti tersedia tapi tidak dimuat otomatis;
                // pemuatan otomatis diatur lewat AddInsStartup per pengguna.
                addinKey.SetValue(null, 0, RegistryValueKind.DWord);
                addinKey.SetValue("Title", AddinTitle, RegistryValueKind.String);
                addinKey.SetValue("Description", AddinDescription, RegistryValueKind.String);
            }

            using (var startupKey = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\SolidWorks\AddInsStartup\{guidKey}"))
            {
                startupKey?.SetValue(null, 1, RegistryValueKind.DWord);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            var guidKey = $"{{{type.GUID.ToString().ToUpperInvariant()}}}";

            TryDelete(() => Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\SolidWorks\Addins\{guidKey}", throwOnMissingSubKey: false));
            TryDelete(() => Registry.CurrentUser.DeleteSubKeyTree($@"SOFTWARE\SolidWorks\AddInsStartup\{guidKey}", throwOnMissingSubKey: false));
        }

        private static void TryDelete(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // Unregister harus selalu berusaha menyelesaikan sisanya,
                // walaupun satu kunci gagal dihapus.
            }
        }
    }
}
