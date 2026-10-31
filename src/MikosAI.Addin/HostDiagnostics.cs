using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MikosAI.Addin
{
    /// <summary>
    /// Menulis <c>%LOCALAPPDATA%\MikosAI\host-status.json</c>.
    ///
    /// Ini SATU-SATUNYA cara yang sah untuk menyatakan add-in benar-benar hidup.
    /// <c>ISldWorks.LoadAddIn()</c> mengembalikan 0 TIDAK dihitung lulus:
    /// SolidWorks bisa memuat DLL dan melaporkan sukses tanpa pernah
    /// menginstansiasi <c>ISwAddin</c> - biasanya karena TLB belum terdaftar.
    ///
    /// Berkas ini juga pengganti debugger. Step-debug add-in net48 tanpa Visual
    /// Studio terbatas (risiko R12), dan bahkan dengan VS pun menyakitkan karena
    /// SolidWorks mengunci DLL yang dimuat. Jadi diagnostik ditulis lengkap
    /// dengan sengaja.
    /// </summary>
    internal sealed class HostDiagnostics
    {
        private readonly object _lock = new object();
        private readonly List<string> _errors = new List<string>();
        private readonly string _path;

        internal HostDiagnostics()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MikosAI");

            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "host-status.json");
        }

        internal string Path_ => _path;

        // --- Yang dilaporkan ke script probe --------------------------------

        /// <summary>connecting / connected / disconnected / failed</summary>
        internal string Status { get; set; } = "connecting";

        internal bool CallbackRegistered { get; set; }

        internal bool CommandGroup { get; set; }

        internal bool TaskPane { get; set; }

        internal bool McpServer { get; set; }

        internal string? SolidWorksRevision { get; set; }

        internal int AddinCookie { get; set; }

        /// <summary>Hitungan event, dipakai gerbang penerimaan Fase 0 poin 4.</summary>
        internal int FileNewEvents { get; set; }

        internal int ActiveDocChangeEvents { get; set; }

        internal int OpenDocumentCount { get; set; } = -1;

        /// <summary>Thread utama SolidWorks yang berhasil ditangkap dispatcher.</summary>
        internal int MainThreadId { get; set; }

        internal bool DispatcherReady { get; set; }

        internal void AddError(string message)
        {
            lock (_lock)
            {
                _errors.Add(message);
            }
        }

        /// <summary>
        /// Mencatat exception beserta tipenya. Tipe exception sering jadi
        /// petunjuk langsung - mis. InvalidCastException pada PropertyManagerPage
        /// berarti handler-nya tidak menyediakan IDispatch.
        /// </summary>
        internal void AddError(string context, Exception exception)
        {
            var hresult = exception is System.Runtime.InteropServices.COMException com
                ? $" HRESULT=0x{com.HResult:X8}"
                : string.Empty;

            AddError($"{context}: {exception.GetType().Name}{hresult}: {exception.Message}");
        }

        /// <summary>
        /// Menulis berkas diagnostik. Tidak pernah melempar exception - kalau
        /// penulisan diagnostik menjatuhkan ConnectToSW, kita kehilangan justru
        /// informasi yang sedang dicari.
        /// </summary>
        internal void Write()
        {
            try
            {
                string[] errors;
                lock (_lock)
                {
                    errors = _errors.ToArray();
                }

                var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["schemaVersion"] = "1.0",
                    ["writtenAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["status"] = Status,
                    ["callbackRegistered"] = CallbackRegistered,
                    ["commandGroup"] = CommandGroup,
                    ["taskPane"] = TaskPane,
                    ["mcpServer"] = McpServer,
                    ["solidworksRevision"] = SolidWorksRevision,
                    ["addinCookie"] = AddinCookie,
                    ["dispatcherReady"] = DispatcherReady,
                    ["mainThreadId"] = MainThreadId,
                    ["fileNewEvents"] = FileNewEvents,
                    ["activeDocChangeEvents"] = ActiveDocChangeEvents,
                    ["openDocumentCount"] = OpenDocumentCount,
                    ["addinAssembly"] = SafeAssemblyLocation(),
                    ["processId"] = SafeProcessId(),
                    ["errors"] = errors,
                };

                var json = JsonSerializer.Serialize(
                    payload,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception)
            {
                // Tidak ada tempat lain untuk melapor. Jangan sampai menjatuhkan
                // ConnectToSW.
            }
        }

        private static string SafeAssemblyLocation()
        {
            try
            {
                return typeof(HostDiagnostics).Assembly.Location;
            }
            catch (Exception)
            {
                return "(tidak diketahui)";
            }
        }

        private static int SafeProcessId()
        {
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                return process.Id;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
