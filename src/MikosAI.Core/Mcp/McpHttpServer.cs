using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MikosAI.Core.Mcp
{
    /// <summary>
    /// Server MCP di atas HTTP loopback.
    ///
    /// Di-host DI DALAM add-in, artinya di dalam proses SolidWorks. Tool handler
    /// memanggil ToolExecutor lewat pemanggilan fungsi biasa, bukan lewat
    /// protokol tambahan - itu alasan utama topologi ini dipilih. Lihat
    /// docs/adr/0002.
    ///
    /// Keamanan:
    ///
    ///   - HANYA bind 127.0.0.1. Tidak pernah 0.0.0.0, tanpa pengecualian.
    ///   - Port ephemeral, ditentukan OS. Dimungkinkan karena AgentWorkspace
    ///     menulis konfigurasi MCP setiap sesi.
    ///   - Bearer token per sesi, wajib di setiap request.
    ///   - Token juga diterima lewat query string, karena belum terkonfirmasi
    ///     apakah Antigravity mengirim custom header dari entry serverUrl.
    /// </summary>
    public sealed class McpHttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly McpRequestHandler _handler;
        private readonly string _token;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();

        private Task? _acceptLoop;
        private bool _disposed;

        /// <summary>Batas ukuran body, supaya request raksasa tidak menghabiskan memori proses SolidWorks.</summary>
        private const int MaxBodyBytes = 4 * 1024 * 1024;

        public McpHttpServer(McpRequestHandler handler, string sessionToken, int port = 0)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));

            if (string.IsNullOrWhiteSpace(sessionToken))
            {
                throw new ArgumentException("Token sesi tidak boleh kosong.", nameof(sessionToken));
            }

            _token = sessionToken;

            if (port == 0)
            {
                port = FindFreeLoopbackPort();
            }

            Port = port;
            Endpoint = new Uri($"http://127.0.0.1:{port}/mcp");

            _listener = new HttpListener();

            // Loopback saja. Mengubah ini berarti mengekspos tool CAD ke jaringan.
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/mcp/");
        }

        public int Port { get; }

        /// <summary>URL yang ditulis ke mcp_config.json.</summary>
        public Uri Endpoint { get; }

        public bool IsRunning => _listener.IsListening;

        public void Start()
        {
            _listener.Start();
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_shutdown.Token));
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    // Listener ditutup. Keluar dengan tenang.
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                // Tidak di-await: satu request yang lambat tidak boleh memblokir
                // request lain. Thread ini BUKAN thread utama SolidWorks -
                // ToolExecutor yang akan marshal ke sana lewat dispatcher.
                _ = Task.Run(() => HandleContextAsync(context, cancellationToken), cancellationToken);
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                // Pertahanan berlapis: walaupun prefix sudah loopback-only,
                // tolak eksplisit apa pun yang bukan loopback.
                if (context.Request.RemoteEndPoint is null
                    || !IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
                {
                    await WriteAsync(context, 403, @"{""error"":""hanya loopback""}").ConfigureAwait(false);
                    return;
                }

                if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(context, 405, @"{""error"":""gunakan POST""}").ConfigureAwait(false);
                    return;
                }

                if (!IsAuthorized(context.Request))
                {
                    await WriteAsync(context, 401, @"{""error"":""token tidak valid""}").ConfigureAwait(false);
                    return;
                }

                if (context.Request.ContentLength64 > MaxBodyBytes)
                {
                    await WriteAsync(context, 413, @"{""error"":""body terlalu besar""}").ConfigureAwait(false);
                    return;
                }

                string body;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                var response = await _handler.HandleAsync(body, cancellationToken).ConfigureAwait(false);

                if (response is null)
                {
                    // Notifikasi JSON-RPC: tidak ada respons.
                    await WriteAsync(context, 202, string.Empty).ConfigureAwait(false);
                    return;
                }

                await WriteAsync(context, 200, response.ToJsonString()).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
            catch (Exception)
            {
                try
                {
                    await WriteAsync(context, 500, @"{""error"":""kesalahan internal""}").ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Koneksi sudah mati.
                }
            }
        }

        /// <summary>
        /// Memeriksa token. Menerima header Authorization: Bearer, atau query
        /// string ?token=.
        ///
        /// Query string bukan pilihan ideal - ia bisa muncul di log. Tapi untuk
        /// server loopback-only, dan karena belum terkonfirmasi apakah entry
        /// serverUrl Antigravity meneruskan custom header (item 5 di
        /// docs/integrasi-antigravity.md), ini fallback yang dapat diterima.
        /// </summary>
        internal bool IsAuthorized(HttpListenerRequest request)
        {
            var header = request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(header))
            {
                const string prefix = "Bearer ";
                if (header!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && FixedTimeEquals(header.Substring(prefix.Length).Trim(), _token))
                {
                    return true;
                }
            }

            var queryToken = request.QueryString["token"];
            return !string.IsNullOrEmpty(queryToken) && FixedTimeEquals(queryToken!, _token);
        }

        /// <summary>
        /// Perbandingan dengan waktu tetap, supaya panjang kecocokan tidak bisa
        /// dibaca dari waktu respons.
        /// </summary>
        private static bool FixedTimeEquals(string left, string right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        private static async Task WriteAsync(HttpListenerContext context, int statusCode, string body)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            if (body.Length > 0)
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            else
            {
                context.Response.ContentLength64 = 0;
            }

            context.Response.Close();
        }

        /// <summary>
        /// Meminta port bebas dari OS. Dipakai karena AgentWorkspace menulis
        /// konfigurasi MCP setiap sesi, jadi tidak ada berkas statis yang perlu
        /// mencocokkan angka port.
        /// </summary>
        internal static int FindFreeLoopbackPort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            try
            {
                probe.Start();
                return ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _shutdown.Cancel();
            }
            catch (Exception)
            {
                // Abaikan.
            }

            try
            {
                if (_listener.IsListening)
                {
                    _listener.Stop();
                }
                _listener.Close();
            }
            catch (Exception)
            {
                // Abaikan.
            }

            try
            {
                _acceptLoop?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                // Abaikan.
            }

            _shutdown.Dispose();
        }
    }
}
