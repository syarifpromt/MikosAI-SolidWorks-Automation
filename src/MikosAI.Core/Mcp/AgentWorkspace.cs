using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MikosAI.Core.Mcp
{
    /// <summary>
    /// Direktori kerja milik add-in untuk AI CLI.
    ///
    /// Add-in MEMILIKI foldernya sendiri dan tidak pernah menyentuh konfigurasi
    /// pribadi pengguna:
    ///
    ///   %LOCALAPPDATA%\MikosAI\agent-workspace\
    ///   └── .agents\
    ///       └── mcp_config.json      &lt;- ditulis ulang setiap sesi
    ///
    /// `agy` di-spawn dengan cwd diarahkan ke folder ini, sehingga Antigravity
    /// membaca konfigurasi per-proyek dari situ.
    ///
    /// Kenapa bukan ~/.gemini/config/mcp_config.json:
    ///
    ///   - Konfigurasi pribadi pengguna tidak pernah dimodifikasi atau ditimpa
    ///   - Tidak bentrok dengan MCP server lain yang pengguna pakai sendiri
    ///   - Port dan token selalu benar karena ditulis ulang per sesi
    ///   - Uninstall bersih: hapus satu folder
    ///
    /// Dan karena kita menulis konfigurasinya sendiri setiap sesi, PORT BISA
    /// EPHEMERAL - tidak perlu angka tetap. Lihat docs/adr/0002.
    /// </summary>
    public sealed class AgentWorkspace
    {
        private const string ServerName = "mikosai-solidworks";

        private readonly string _root;

        /// <summary>
        /// Membuat workspace. <paramref name="rootOverride"/> hanya untuk test.
        /// </summary>
        public AgentWorkspace(string? rootOverride = null)
        {
            _root = rootOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MikosAI",
                "agent-workspace");
        }

        /// <summary>Direktori kerja yang dipakai sebagai cwd saat men-spawn `agy`.</summary>
        public string WorkingDirectory => _root;

        // McpConfigPath DIHAPUS, bukan dipertahankan untuk kompatibilitas.
        //
        // `.agents/mcp_config.json` dibuat untuk dibaca agy. Terbukti agy
        // MENGABAIKANNYA SEPENUHNYA: sebuah server palsu ditaruh di berkas itu
        // pada satu folder, dan `agy mcp list` dari folder itu tidak
        // menampilkannya. Pendaftaran bersifat global lewat `agy mcp add`,
        // terlihat dari direktori mana pun.
        //
        // Yang tersisa dari berkas itu hanyalah satu salinan bearer token di
        // disk tanpa pembaca - dan secret tanpa pembaca adalah risiko murni,
        // bukan cadangan.
        //
        // Bedakan dari session.json di bawah: ia juga memuat token, tapi PUNYA
        // pembaca (scripts/mcp-roundtrip.ps1) dan ditulis dengan ACL khusus
        // pengguna.
        //
        // Lihat catatan item 6 di docs/integrasi-antigravity.md.

        public string SessionFilePath => Path.Combine(_root, "session.json");

        /// <summary>
        /// Membuat token bearer acak untuk sesi ini.
        ///
        /// 32 byte dari CSPRNG, base64url. Dibuat baru setiap sesi - token lama
        /// tidak pernah dipakai ulang.
        /// </summary>
        public static string NewSessionToken()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);

            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        /// <summary>
        /// Menyiapkan workspace untuk sesi baru: tulis mcp_config.json dan
        /// session.json.
        ///
        /// Dipanggil setiap ConnectToSW, setelah McpHttpServer tahu port-nya.
        /// </summary>
        public void Prepare(Uri serverUri, string sessionToken)
        {
            if (serverUri is null) throw new ArgumentNullException(nameof(serverUri));
            if (string.IsNullOrWhiteSpace(sessionToken)) throw new ArgumentException("Token kosong.", nameof(sessionToken));

            if (!serverUri.IsLoopback)
            {
                // Jaring pengaman: menulis URL non-loopback ke konfigurasi AI
                // berarti mengekspos tool CAD ke jaringan.
                throw new ArgumentException(
                    $"Hanya URL loopback yang boleh ditulis ke konfigurasi MCP. Diterima: {serverUri}",
                    nameof(serverUri));
            }

            Directory.CreateDirectory(_root);

            WriteSessionFile(serverUri, sessionToken);
        }


        /// <summary>
        /// Menulis session.json dengan ACL hanya untuk pengguna saat ini.
        ///
        /// Berkas ini memuat token bearer, jadi aksesnya dipersempit sebisa
        /// mungkin. Kegagalan menerapkan ACL dilaporkan lewat return value, tidak
        /// dengan exception - tapi pemanggil HARUS memperlakukannya sebagai
        /// masalah serius.
        /// </summary>
        private void WriteSessionFile(Uri serverUri, string sessionToken)
        {
            var payload = new JsonObject
            {
                ["schemaVersion"] = "1.0",
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["serverUrl"] = serverUri.ToString(),
                ["token"] = sessionToken,
                ["pid"] = SafeProcessId(),
            };

            File.WriteAllText(
                SessionFilePath,
                payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            RestrictToCurrentUser(SessionFilePath);
        }

        /// <summary>
        /// Mempersempit ACL berkas ke pengguna saat ini saja.
        /// Mengembalikan false kalau gagal - jangan diabaikan.
        /// </summary>
        internal static bool RestrictToCurrentUser(string path)
        {
            try
            {
                var info = new FileInfo(path);
                var security = info.GetAccessControl();

                // Buang pewarisan, lalu beri akses penuh hanya ke pemilik.
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser is null)
                {
                    return false;
                }

                foreach (FileSystemAccessRule existing in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                {
                    security.RemoveAccessRuleAll(existing);
                }

                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));

                info.SetAccessControl(security);
                return true;
            }
            catch (Exception)
            {
                // Bisa gagal di filesystem non-NTFS atau lingkungan terbatas.
                return false;
            }
        }

        /// <summary>
        /// Membersihkan workspace. Dipakai saat uninstall - menghapus satu folder
        /// sudah cukup, karena kita tidak pernah menulis ke tempat lain.
        /// </summary>
        public void Remove()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (Exception)
            {
                // Pembersihan yang gagal tidak boleh menggagalkan shutdown.
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
