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
            AssemblyResolver.Pasang();
        }

        private readonly HostDiagnostics _diagnostics = new HostDiagnostics();

        private SolidWorksApi? _sw;
        private MainThreadDispatcher? _dispatcher;
        private McpHost? _mcp;
        private SolidWorksReader? _pembaca;
        private TaskPaneHost? _panel;
        private ProviderHost? _provider;
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

            // --- Simpan referensi aplikasi ----------------------------------
            _sw = SolidWorksApi.Buat(ThisSW, _diagnostics);

            if (_sw is null)
            {
                _diagnostics.Status = "failed";
                try { _diagnostics.Write(); } catch (Exception) { }
                return false;
            }

            // --- Tangkap thread utama ---------------------------------------
            // Dikerjakan DI SINI dan tidak di tempat lain. Ini satu-satunya
            // titik yang dijamin berjalan di thread utama SolidWorks.
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
                // Executor nyata: setiap pembacaan dijalankan lewat dispatcher
                // ke thread utama SolidWorks.
                var executor = new SolidWorksToolExecutor(
                    _dispatcher ?? throw new InvalidOperationException("dispatcher belum siap"),
                    () => _pembaca);

                _mcp = new McpHost(_diagnostics, executor);
                _diagnostics.McpServer = _mcp.Mulai();
                _diagnostics.McpEndpoint = _mcp.Endpoint?.ToString();
                _diagnostics.McpToolCount = _mcp.ToolCount;
                _diagnostics.McpWorkspace = _mcp.WorkspaceDirectory;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("McpHost", exception);
            }

            // --- Periksa provider AI ----------------------------------------
            try
            {
                _provider = new ProviderHost(_diagnostics);

                // Tidak di-await: ConnectToSW harus kembali cepat, dan panel
                // memperbarui dirinya saat status berikutnya diminta.
                _ = _provider.PeriksaAsync();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("ProviderHost", exception);
            }

            // --- Bangun TaskPane --------------------------------------------
            try
            {
                _diagnostics.TaskPane = BangunTaskPane();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("BangunTaskPane", exception);
            }

            _diagnostics.Status = "connected";

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
            // TaskPane dilepas lebih dulu: ia memegang handle jendela yang
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

            return _taskPaneView is not null;
        }

        /// <summary>
        /// Status yang dikirim ke panel UI.
        ///
        /// Dibangun ulang setiap permintaan, bukan di-cache - panel menampilkan
        /// keadaan sekarang, dan jumlah dokumen bisa berubah kapan saja.
        /// </summary>
        private JsonObject StatusUntukPanel()
        {
            var dokumen = -1;
            try
            {
                dokumen = _sw?.TryGetDocumentCount() ?? -1;
            }
            catch (Exception)
            {
                // Biarkan -1: tidak terbaca, bukan nol.
            }

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
                ["buildCommit"] = _diagnostics.BuildCommit,
                ["buildTime"] = _diagnostics.BuildTime,
                ["errors"] = galat,
            };
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
            try
            {
                if (_provider is null)
                {
                    _panel?.KirimPesanChat("galat", "Provider AI belum siap.");
                    return;
                }

                var hasil = await _provider
                    .JalankanAsync(prompt, _mcp?.WorkspaceDirectory ?? string.Empty)
                    .ConfigureAwait(false);

                if (hasil.IsSuccessful)
                {
                    _panel?.KirimPesanChat("ai", hasil.Summary ?? "(tidak ada jawaban)");
                    return;
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
