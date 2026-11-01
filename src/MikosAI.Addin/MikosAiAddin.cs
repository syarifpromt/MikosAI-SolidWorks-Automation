using System;
using System.Runtime.InteropServices;
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
