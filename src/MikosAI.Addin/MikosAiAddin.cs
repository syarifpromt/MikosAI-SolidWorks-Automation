using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
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
    /// SLDWORKS.exe di lingkungan kalian, dan apakah thread utamanya bisa
    /// ditangkap.
    ///
    /// Yang BELUM ada di sini, dan itu disengaja: CommandGroup, TaskPane,
    /// WebView2, PropertyManagerPage, dan MCP server. Semuanya menyentuh
    /// permukaan API yang jauh lebih luas, jadi ditambahkan setelah fondasi ini
    /// terbukti hidup. Menambahkannya sekarang hanya memperbanyak kemungkinan
    /// penyebab kalau gagal.
    ///
    /// Jangan percaya LoadAddIn=0. Periksa
    /// %LOCALAPPDATA%\MikosAI\host-status.json.
    /// </summary>
    [ComVisible(true)]
    [Guid(AddinGuid)]
    [ClassInterface(ClassInterfaceType.None)]
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

        private readonly HostDiagnostics _diagnostics = new HostDiagnostics();

        private ISldWorks? _swApp;
        private MainThreadDispatcher? _dispatcher;
        private int _cookie;

        // ====================================================================
        // ISwAddin
        // ====================================================================

        /// <summary>
        /// Dipanggil SolidWorks saat add-in dimuat, pada thread STA utama.
        ///
        /// Seluruh isi method ini dibungkus try/catch per langkah. Alasannya:
        /// exception yang lolos dari ConnectToSW tidak meninggalkan jejak apa
        /// pun - SolidWorks hanya diam-diam tidak memuat add-in. Dengan
        /// menangkap per langkah, kita selalu punya host-status.json yang
        /// menyebut langkah mana yang gagal.
        /// </summary>
        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            _diagnostics.Status = "connecting";
            _diagnostics.AddinCookie = Cookie;
            _cookie = Cookie;

            // --- Simpan referensi aplikasi ----------------------------------
            try
            {
                _swApp = (ISldWorks)ThisSW;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("cast ISldWorks", exception);
                _diagnostics.Status = "failed";
                _diagnostics.Write();
                return false;
            }

            // --- Daftarkan callback -----------------------------------------
            // Tanpa ini, SolidWorks tidak bisa memanggil balik method add-in.
            try
            {
                _swApp.SetAddinCallbackInfo2(0, this, Cookie);
                _diagnostics.CallbackRegistered = true;
            }
            catch (Exception exception)
            {
                // Dicatat tapi tidak membatalkan connect: kita masih ingin tahu
                // apakah sisanya jalan.
                _diagnostics.AddError("SetAddinCallbackInfo2", exception);
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

            // --- Baca revisi untuk bukti ------------------------------------
            try
            {
                _diagnostics.SolidWorksRevision = _swApp.RevisionNumber();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("RevisionNumber", exception);
            }

            try
            {
                _diagnostics.OpenDocumentCount = _swApp.GetDocumentCount();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("GetDocumentCount", exception);
            }

            _diagnostics.Status = "connected";
            _diagnostics.Write();

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
                if (_swApp is not null)
                {
                    Marshal.ReleaseComObject(_swApp);
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("release ISldWorks", exception);
            }
            finally
            {
                _swApp = null;
            }

            _diagnostics.Status = "disconnected";
            _diagnostics.OpenDocumentCount = -1;
            _diagnostics.Write();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            return true;
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

            // Daftar add-in yang dipindai SolidWorks.
            using (var addinKey = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\SolidWorks\Addins\{guidKey}"))
            {
                if (addinKey is null)
                {
                    throw new InvalidOperationException(
                        "Tidak bisa menulis HKLM\\SOFTWARE\\SolidWorks\\Addins. " +
                        "Registrasi Machine butuh hak administrator.");
                }

                // Nilai default 0 berarti tersedia tapi tidak dimuat otomatis;
                // pemuatan otomatis diatur lewat kunci AddInsStartup per pengguna.
                addinKey.SetValue(null, 0, RegistryValueKind.DWord);
                addinKey.SetValue("Title", AddinTitle, RegistryValueKind.String);
                addinKey.SetValue("Description", AddinDescription, RegistryValueKind.String);
            }

            // Preferensi pemuatan saat startup, per pengguna.
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
