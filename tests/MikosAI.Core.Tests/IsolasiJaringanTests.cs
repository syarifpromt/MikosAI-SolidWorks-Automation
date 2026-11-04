using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MikosAI.Core.Mcp;
using MikosAI.Core.Testing;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Server MCP hidup DI DALAM SLDWORKS.exe dan mengekspos tool yang membaca
    /// model pengguna. Kalau ia bisa dihubungi dari jaringan, itu bukan cacat
    /// kecil.
    ///
    /// KENAPA INI BISA DIUJI DARI SATU MESIN SAJA.
    ///
    /// Roadmap menuliskan gerbang ini sebagai "diverifikasi dari mesin lain di
    /// jaringan", dan itu terdengar seperti satu-satunya cara. Bukan: kalau
    /// listener-nya bind 0.0.0.0, ia akan menerima koneksi ke IP LAN mesin ini
    /// SENDIRI. Jadi menembak IP sendiri sudah membedakan 127.0.0.1 dari
    /// 0.0.0.0, tanpa mesin kedua.
    ///
    /// Yang tidak bisa diuji begini hanyalah firewall dan NAT di depannya - dan
    /// itu memang bukan properti kode kita.
    /// </summary>
    public class IsolasiJaringanTests
    {
        private static McpRequestHandler Handler()
            => new McpRequestHandler(
                new List<McpToolDescriptor>
                {
                    new McpToolDescriptor { Name = "get_active_document" },
                },
                new FakeToolExecutor().Returns("get_active_document"));

        [Fact]
        public async Task server_bisa_dihubungi_di_loopback()
        {
            var token = AgentWorkspace.NewSessionToken();

            using (var server = new McpHttpServer(Handler(), token))
            {
                server.Start();

                var (status, _) = await Kirim(server.Endpoint.ToString(), token);

                Assert.Equal(200, status);
            }
        }

        /// <summary>
        /// Gerbang yang sebenarnya: server TIDAK boleh menerima koneksi di
        /// alamat non-loopback mesin ini.
        /// </summary>
        [Fact]
        public void server_tidak_bisa_dihubungi_di_alamat_non_loopback()
        {
            var alamat = AlamatNonLoopback();

            var token = AgentWorkspace.NewSessionToken();

            using (var server = new McpHttpServer(Handler(), token))
            {
                server.Start();

                var port = server.Endpoint.Port;

                // Kalau tidak ada alamat non-loopback sama sekali, test ini
                // tidak bisa membuktikan apa pun - dan itu dikatakan terang,
                // bukan dilewati diam-diam sebagai "lulus".
                Assert.True(
                    alamat.Count > 0,
                    "Mesin ini tidak punya alamat IPv4 non-loopback, jadi isolasi jaringan " +
                    "TIDAK terverifikasi di sini. Jalankan di mesin yang terhubung jaringan.");

                var tersambung = new List<string>();

                foreach (var ip in alamat)
                {
                    if (BisaTersambung(ip, port))
                    {
                        tersambung.Add($"{ip}:{port}");
                    }
                }

                Assert.True(
                    tersambung.Count == 0,
                    "Server menerima koneksi di alamat non-loopback: " +
                    string.Join(", ", tersambung) +
                    ". Itu berarti ia bind 0.0.0.0, dan tool CAD terekspos ke jaringan.");
            }
        }

        /// <summary>
        /// MEMBUKTIKAN TEST DI ATAS TIDAK HAMPA.
        ///
        /// Test negatif yang selalu lulus mencurigakan: ia lulus kalau
        /// server-nya benar, tapi juga lulus kalau DETEKTORNYA tidak berfungsi -
        /// dan dari luar keduanya terlihat sama.
        ///
        /// Di sini sebuah listener sengaja dibind ke 0.0.0.0, lalu detektor yang
        /// sama disuruh memeriksanya. Ia HARUS menemukannya. Kalau tidak,
        /// pernyataan "server kita hanya loopback" tidak berarti apa pun.
        ///
        /// TcpListener pada IPAddress.Any tidak butuh hak administrator,
        /// berbeda dari prefix HttpListener - itu sebabnya pembuktiannya lewat
        /// socket mentah.
        /// </summary>
        [Fact]
        public void detektornya_memang_menemukan_listener_yang_bind_semua_alamat()
        {
            var alamat = AlamatNonLoopback();

            Assert.True(
                alamat.Count > 0,
                "Mesin ini tidak punya alamat IPv4 non-loopback, jadi detektornya tidak bisa dibuktikan.");

            var listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();

            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;

                var ditemukan = alamat.Where(ip => BisaTersambung(ip, port)).ToList();

                Assert.True(
                    ditemukan.Count > 0,
                    "Listener dibind ke 0.0.0.0 tapi detektornya tidak menemukannya di satu pun " +
                    $"dari {alamat.Count} alamat non-loopback. Detektornya rusak, dan test isolasi " +
                    "jaringan di atas tidak membuktikan apa pun.");
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task tanpa_header_authorization_ditolak_401()
        {
            var token = AgentWorkspace.NewSessionToken();

            using (var server = new McpHttpServer(Handler(), token))
            {
                server.Start();

                var (status, _) = await Kirim(server.Endpoint.ToString(), token: null);

                Assert.Equal(401, status);
            }
        }

        [Fact]
        public async Task token_salah_ditolak_401()
        {
            var token = AgentWorkspace.NewSessionToken();

            using (var server = new McpHttpServer(Handler(), token))
            {
                server.Start();

                var (status, _) = await Kirim(server.Endpoint.ToString(), "token-yang-jelas-salah");

                Assert.Equal(401, status);
            }
        }

        /// <summary>
        /// Token per sesi harus BERBEDA setiap kali.
        ///
        /// Token yang bisa diramalkan sama saja dengan tidak ada token, dan
        /// kesalahan semacam itu tidak terlihat dari luar sampai terlambat.
        /// </summary>
        [Fact]
        public void token_sesi_tidak_pernah_berulang()
        {
            var kumpulan = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < 200; i++)
            {
                var token = AgentWorkspace.NewSessionToken();

                Assert.False(string.IsNullOrWhiteSpace(token));

                // Cukup panjang untuk tidak bisa ditebak dengan brute force.
                Assert.True(token.Length >= 32, $"Token terlalu pendek: {token.Length} karakter.");

                Assert.True(kumpulan.Add(token), "Token sesi berulang: " + token);
            }
        }

        // -------------------------------------------------------------------
        // AgentWorkspace: jaring pengaman loopback dan isolasi berkas
        // -------------------------------------------------------------------

        /// <summary>
        /// Menulis URL non-loopback ke konfigurasi AI berarti mengekspos tool CAD
        /// ke jaringan. Jaring pengamannya sudah ada di kode; sampai sekarang
        /// tidak ada yang membuktikannya bekerja.
        /// </summary>
        [Theory]
        [InlineData("http://192.168.1.108:51234/mcp")]
        [InlineData("http://0.0.0.0:51234/mcp")]
        [InlineData("http://example.com/mcp")]
        public void url_non_loopback_ditolak_sebelum_ditulis(string url)
        {
            var folder = FolderSementara();

            try
            {
                var workspace = new AgentWorkspace(folder);

                Assert.Throws<ArgumentException>(
                    () => workspace.Prepare(new Uri(url), AgentWorkspace.NewSessionToken()));

                // Dan tidak ada berkas yang tertinggal separuh jalan.
                Assert.False(File.Exists(workspace.SessionFilePath));
            }
            finally
            {
                Bersihkan(folder);
            }
        }

        /// <summary>
        /// Prepare tidak menyentuh config global agy.
        ///
        /// LINGKUPNYA SEMPIT, DAN ITU HARUS DIKATAKAN TERANG.
        ///
        /// Test ini hanya menguji <c>AgentWorkspace.Prepare</c>. Ia TIDAK
        /// membuktikan "add-in tidak mengubah config global", dan untuk
        /// sementara gerbang di ROADMAP memang ditulis seluas itu lalu
        /// tercentang keliru atas dasar test ini.
        ///
        /// Kenyataannya add-in MEMANG mengubah config global, dengan sengaja:
        /// pendaftaran MCP dilakukan lewat `agy mcp add`, yang menulis ke
        /// ~/.gemini/config/mcp_config.json. Itu keputusan rancangan, bukan
        /// kecelakaan - lihat docs/integrasi-antigravity.md bagian 3.
        ///
        /// Jadi yang dijaga di sini cuma satu hal: Prepare sendiri tidak ikut
        /// menyentuhnya. Test hijau hanya membuktikan apa yang diuji.
        /// </summary>
        [Fact]
        public void Prepare_tidak_menyentuh_config_global_agy()
        {
            var global = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".gemini", "config", "mcp_config.json");

            var sebelum = Sidik(global);

            var folder = FolderSementara();

            try
            {
                var workspace = new AgentWorkspace(folder);
                workspace.Prepare(new Uri("http://127.0.0.1:51234/mcp"), AgentWorkspace.NewSessionToken());

                // Yang ditulis ADA, supaya test ini tidak lulus karena Prepare
                // tidak melakukan apa pun.
                Assert.True(File.Exists(workspace.SessionFilePath));

                // Dan ia di dalam workspace-nya sendiri.
                Assert.StartsWith(folder, workspace.SessionFilePath, StringComparison.OrdinalIgnoreCase);

                Assert.Equal(sebelum, Sidik(global));
            }
            finally
            {
                Bersihkan(folder);
            }
        }

        // -------------------------------------------------------------------
        // Alat bantu
        // -------------------------------------------------------------------

        private static async Task<(int Status, string Body)> Kirim(string url, string? token)
        {
            using (var klien = new System.Net.Http.HttpClient())
            {
                klien.Timeout = TimeSpan.FromSeconds(10);

                var isi = new System.Net.Http.StringContent(
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\",\"params\":{}}",
                    Encoding.UTF8,
                    "application/json");

                var permintaan = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Post, url) { Content = isi };

                if (token is not null)
                {
                    permintaan.Headers.Add("Authorization", "Bearer " + token);
                }

                var jawaban = await klien.SendAsync(permintaan).ConfigureAwait(false);
                var teks = await jawaban.Content.ReadAsStringAsync().ConfigureAwait(false);

                return ((int)jawaban.StatusCode, teks);
            }
        }

        private static List<IPAddress> AlamatNonLoopback()
        {
            var hasil = new List<IPAddress>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var info in nic.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(info.Address))
                    {
                        hasil.Add(info.Address);
                    }
                }
            }

            return hasil;
        }

        /// <summary>
        /// TCP connect mentah, bukan HTTP.
        ///
        /// Yang diuji adalah apakah ADA yang mendengarkan di alamat itu. Request
        /// HTTP akan mencampurkan kegagalan koneksi dengan kegagalan protokol,
        /// dan keduanya harus dibedakan.
        /// </summary>
        private static bool BisaTersambung(IPAddress alamat, int port)
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    var hasil = socket.BeginConnect(alamat, port, null, null);

                    if (!hasil.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                    {
                        return false;
                    }

                    socket.EndConnect(hasil);
                    return socket.Connected;
                }
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private static string? Sidik(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
            }
        }

        private static string FolderSementara()
        {
            var folder = Path.Combine(Path.GetTempPath(), "mikosai-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static void Bersihkan(string folder)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Folder sementara yang tertinggal tidak merugikan siapa pun.
            }
        }
    }
}
