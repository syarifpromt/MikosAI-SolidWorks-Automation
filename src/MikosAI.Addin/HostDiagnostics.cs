using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MikosAI.Addin
{
    /// <summary>
    /// Menulis <c>%LOCALAPPDATA%\MikosAI\host-status.json</c>.
    ///
    /// Ini SATU-SATUNYA cara yang sah untuk menyatakan add-in benar-benar hidup.
    /// <c>ISldWorks.LoadAddIn()</c> mengembalikan 0 TIDAK dihitung lulus:
    /// SolidWorks bisa memuat DLL dan melaporkan sukses tanpa pernah
    /// menginstansiasi <c>ISwAddin</c>.
    ///
    /// NOL DEPENDENSI EKSTERNAL, DAN ITU DISENGAJA.
    ///
    /// JSON di sini dibangun dengan tangan, bukan lewat System.Text.Json.
    /// Alasannya ditemukan dengan cara yang mahal: kelas ini dibuat di field
    /// initializer MikosAiAddin, jadi memuatnya adalah prasyarat untuk
    /// menginstansiasi add-in. Kalau ia butuh assembly eksternal dan assembly
    /// itu tidak bisa diselesaikan, konstruktor gagal, SolidWorks diam-diam
    /// tidak memuat add-in, dan TIDAK ADA diagnostik apa pun yang tertulis -
    /// tepat ketika diagnostik paling dibutuhkan.
    ///
    /// Aturannya: yang melaporkan kegagalan tidak boleh bergantung pada hal-hal
    /// yang bisa gagal.
    /// </summary>
    internal sealed class HostDiagnostics
    {
        private readonly object _lock = new object();
        private readonly List<string> _errors = new List<string>();
        private readonly string _path;
        private readonly bool _bisaTulis;

        internal HostDiagnostics()
        {
            var direktori = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MikosAI");

            _path = Path.Combine(direktori, "host-status.json");

            try
            {
                Directory.CreateDirectory(direktori);
                _bisaTulis = true;
            }
            catch (Exception)
            {
                // Jangan melempar dari konstruktor - itu akan menggagalkan
                // instansiasi add-in, yaitu hal yang sedang kita coba diagnosis.
                _bisaTulis = false;
            }
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

        internal int FileNewEvents { get; set; }

        internal int ActiveDocChangeEvents { get; set; }

        /// <summary>-1 berarti tidak bisa dibaca - BUKAN nol.</summary>
        internal int OpenDocumentCount { get; set; } = -1;

        internal int MainThreadId { get; set; }

        internal bool DispatcherReady { get; set; }

        /// <summary>
        /// Langkah terakhir yang DIMULAI. Bukan yang selesai.
        ///
        /// Kalau SolidWorks mati, nilai ini adalah petunjuk terkuat yang kita
        /// punya: ia menunjuk tepat ke apa yang sedang berjalan saat proses
        /// berhenti. ConnectToSW yang selesai bersih tidak memberi tahu apa pun
        /// soal apa yang terjadi beberapa detik kemudian.
        /// </summary>
        internal string? LastActivity { get; private set; }

        internal string? FeatureFlags { get; set; }

        /// <summary>
        /// Menandai langkah baru dan LANGSUNG menulis berkas.
        ///
        /// Penulisannya disengaja langsung, bukan ditunda: kalau proses mati
        /// setelah ini, yang tersimpan di disk adalah langkah yang sedang
        /// berjalan saat itu.
        /// </summary>
        internal void Jejak(string langkah)
        {
            LastActivity = langkah;
            Write();
        }

        /// <summary>
        /// Apakah objek add-in bisa di-cast ke IDispatch. Wajib true, karena
        /// SolidWorks memanggil callback berdasarkan nama method.
        /// </summary>
        internal bool AddinExposesIDispatch { get; set; }

        /// <summary>Endpoint server MCP, atau null kalau tidak berjalan.</summary>
        internal string? McpEndpoint { get; set; }

        /// <summary>Jumlah tool yang diekspos. 0 itu sah - lihat capabilities.yaml.</summary>
        internal int McpToolCount { get; set; }

        internal string? McpWorkspace { get; set; }

        /// <summary>
        /// Salinan error yang tercatat, untuk ditampilkan di panel UI.
        /// Salinan, bukan daftar aslinya - panel tidak boleh bisa mengubahnya.
        /// </summary>
        internal IReadOnlyList<string> Errors
        {
            get
            {
                lock (_lock)
                {
                    return _errors.ToArray();
                }
            }
        }

        /// <summary>Commit git yang dicap saat build. Dipakai deteksi DLL usang.</summary>
        internal string? BuildCommit => MetadataAssembly("BuildCommit");

        internal string? BuildTime => MetadataAssembly("BuildTime");

        internal void AddError(string message)
        {
            lock (_lock)
            {
                _errors.Add(message);
            }
        }

        /// <summary>
        /// Mencatat exception beserta tipenya. Tipe exception sering jadi
        /// petunjuk langsung - mis. <c>FileNotFoundException</c> pada assembly
        /// berarti masalah resolusi dependensi, dan
        /// <c>InvalidCastException</c> pada PropertyManagerPage berarti
        /// handler-nya tidak menyediakan IDispatch.
        /// </summary>
        internal void AddError(string context, Exception exception)
        {
            var tambahan = string.Empty;

            if (exception is System.Runtime.InteropServices.COMException com)
            {
                tambahan = $" HRESULT=0x{com.HResult:X8}";
            }

            AddError($"{context}: {exception.GetType().Name}{tambahan}: {exception.Message}");
        }

        /// <summary>
        /// Menulis berkas diagnostik. Tidak pernah melempar exception - kalau
        /// penulisan diagnostik menjatuhkan ConnectToSW, kita kehilangan justru
        /// informasi yang sedang dicari.
        /// </summary>
        internal void Write()
        {
            if (!_bisaTulis)
            {
                return;
            }

            try
            {
                string[] errors;
                lock (_lock)
                {
                    errors = _errors.ToArray();
                }

                var sb = new StringBuilder(2048);
                sb.Append("{\n");

                Field(sb, "schemaVersion", "1.0");
                Field(sb, "writtenAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                Field(sb, "status", Status);
                FieldBool(sb, "callbackRegistered", CallbackRegistered);
                FieldBool(sb, "commandGroup", CommandGroup);
                FieldBool(sb, "taskPane", TaskPane);
                FieldBool(sb, "mcpServer", McpServer);
                Field(sb, "mcpEndpoint", McpEndpoint);
                FieldInt(sb, "mcpToolCount", McpToolCount);
                Field(sb, "mcpWorkspace", McpWorkspace);
                Field(sb, "solidworksRevision", SolidWorksRevision);
                FieldInt(sb, "addinCookie", AddinCookie);
                FieldBool(sb, "dispatcherReady", DispatcherReady);
                Field(sb, "lastActivity", LastActivity);
                Field(sb, "featureFlags", FeatureFlags);
                FieldBool(sb, "addinExposesIDispatch", AddinExposesIDispatch);
                FieldInt(sb, "mainThreadId", MainThreadId);
                FieldInt(sb, "fileNewEvents", FileNewEvents);
                FieldInt(sb, "activeDocChangeEvents", ActiveDocChangeEvents);
                FieldInt(sb, "openDocumentCount", OpenDocumentCount);
                Field(sb, "buildCommit", BuildCommit);
                Field(sb, "buildTime", BuildTime);
                Field(sb, "addinAssembly", LokasiAssemblyAman());
                Field(sb, "addinFolder", AssemblyResolver.FolderAddin);
                FieldBool(sb, "assemblyResolverInstalled", AssemblyResolver.Terpasang);
                FieldInt(sb, "processId", ProcessIdAman());
                Array_(sb, "assemblyResolveLog", AssemblyResolver.Jejak_);
                Array_(sb, "errors", errors, terakhir: true);

                sb.Append("}\n");

                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception)
            {
                // Tidak ada tempat lain untuk melapor.
            }
        }

        // --- Pembangun JSON minimal -----------------------------------------
        //
        // Cukup untuk bentuk yang kita tulis: objek datar dengan nilai string,
        // bool, int, dan array string. Bukan serializer umum, dan tidak perlu
        // jadi serializer umum.

        private static void Field(StringBuilder sb, string nama, string? nilai)
        {
            sb.Append("  \"").Append(nama).Append("\": ");
            if (nilai is null)
            {
                sb.Append("null");
            }
            else
            {
                Quote(sb, nilai);
            }
            sb.Append(",\n");
        }

        private static void FieldBool(StringBuilder sb, string nama, bool nilai)
            => sb.Append("  \"").Append(nama).Append("\": ")
                 .Append(nilai ? "true" : "false").Append(",\n");

        private static void FieldInt(StringBuilder sb, string nama, int nilai)
            => sb.Append("  \"").Append(nama).Append("\": ")
                 .Append(nilai.ToString(CultureInfo.InvariantCulture)).Append(",\n");

        private static void Array_(StringBuilder sb, string nama, IReadOnlyList<string> nilai, bool terakhir = false)
        {
            sb.Append("  \"").Append(nama).Append("\": [");

            for (var i = 0; i < nilai.Count; i++)
            {
                sb.Append("\n    ");
                Quote(sb, nilai[i]);
                if (i < nilai.Count - 1)
                {
                    sb.Append(',');
                }
            }

            if (nilai.Count > 0)
            {
                sb.Append("\n  ");
            }

            sb.Append(']');
            sb.Append(terakhir ? "\n" : ",\n");
        }

        private static void Quote(StringBuilder sb, string nilai)
        {
            sb.Append('"');

            foreach (var c in nilai)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            sb.Append('"');
        }

        /// <summary>
        /// Membaca nilai AssemblyMetadata yang dicap saat build. Dipakai probe
        /// untuk mendeteksi DLL usang - lihat target CapCommitGit di csproj.
        /// </summary>
        private static string? MetadataAssembly(string kunci)
        {
            try
            {
                var atribut = typeof(HostDiagnostics).Assembly
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), inherit: false);

                foreach (var item in atribut)
                {
                    if (item is System.Reflection.AssemblyMetadataAttribute meta
                        && string.Equals(meta.Key, kunci, StringComparison.Ordinal))
                    {
                        return meta.Value;
                    }
                }
            }
            catch (Exception)
            {
                // Metadata tidak wajib ada.
            }

            return null;
        }

        private static string LokasiAssemblyAman()
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

        private static int ProcessIdAman()
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
