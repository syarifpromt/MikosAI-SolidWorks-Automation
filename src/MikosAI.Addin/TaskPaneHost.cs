using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MikosAI.Addin
{
    /// <summary>
    /// Kontrol WinForms yang menjadi isi TaskPane SolidWorks, dengan WebView2 di
    /// dalamnya.
    ///
    /// KENAPA WEBVIEW2, BUKAN WINFORMS BIASA.
    ///
    /// UI-nya halaman web, jadi tidak ada form designer yang perlu dibuka - dan
    /// itu penting karena kami tidak memakai Visual Studio. Iterasi UI jadi
    /// sekadar mengedit HTML lalu reload.
    ///
    /// Semua inisialisasi dibungkus try/catch dan dilaporkan ke host-status.json.
    /// WebView2 bisa gagal karena runtime-nya tidak terpasang, dan itu tidak
    /// boleh menjatuhkan SolidWorks.
    /// </summary>
    internal sealed class TaskPaneHost : UserControl
    {
        private readonly HostDiagnostics _diagnostics;
        private readonly Func<JsonObject> _ambilStatus;
        private readonly Func<string, System.Threading.Tasks.Task>? _tanganiChat;

        private WebView2? _webView;
        private Label? _pesanGagal;

        internal TaskPaneHost(
            HostDiagnostics diagnostics,
            Func<JsonObject> ambilStatus,
            Func<string, System.Threading.Tasks.Task>? tanganiChat = null)
        {
            _diagnostics = diagnostics;
            _ambilStatus = ambilStatus;
            _tanganiChat = tanganiChat;

            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(24, 24, 27);
            Padding = new Padding(0);

            try
            {
                _webView = new WebView2 { Dock = DockStyle.Fill };
                Controls.Add(_webView);

                // Inisialisasi WebView2 asinkron. Tidak di-await: ConnectToSW
                // harus kembali cepat, dan menunggu di thread utama SolidWorks
                // bisa membekukan UI-nya.
                MulaiWebView();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("membuat kontrol WebView2", exception);
                TampilkanPesanGagal(
                    "WebView2 tidak bisa dibuat.\r\n\r\n" +
                    "Pasang WebView2 Evergreen Runtime dari Microsoft, lalu buka ulang SolidWorks.");
            }
        }

        /// <summary>Halaman UI berhasil dimuat.</summary>
        internal bool SiapPakai { get; private set; }

        private async void MulaiWebView()
        {
            try
            {
                // Folder data WebView2 HARUS writable. Folder instalasi SolidWorks
                // tidak, jadi dipakai LOCALAPPDATA.
                var folderData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MikosAI",
                    "webview2");

                Directory.CreateDirectory(folderData);

                var lingkungan = await CoreWebView2Environment
                    .CreateAsync(browserExecutableFolder: null, userDataFolder: folderData)
                    .ConfigureAwait(true);

                await _webView!.EnsureCoreWebView2Async(lingkungan).ConfigureAwait(true);

                var core = _webView.CoreWebView2;

                // Panel internal: tidak perlu menu konteks, tidak perlu devtools
                // di pemakaian normal.
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = true;

                core.WebMessageReceived += PesanDariHalaman;

                var halaman = Path.Combine(
                    Path.GetDirectoryName(typeof(TaskPaneHost).Assembly.Location) ?? string.Empty,
                    "web",
                    "index.html");

                if (!File.Exists(halaman))
                {
                    _diagnostics.AddError($"halaman UI tidak ditemukan: {halaman}");
                    TampilkanPesanGagal("Halaman UI tidak ditemukan di folder add-in.");
                    return;
                }

                core.Navigate(new Uri(halaman).AbsoluteUri);
                SiapPakai = true;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("inisialisasi WebView2", exception);
                TampilkanPesanGagal(
                    "WebView2 gagal diinisialisasi.\r\n\r\n" + exception.Message);
            }
        }

        /// <summary>
        /// Perintah dari halaman. Hanya ada satu untuk sekarang: minta status.
        ///
        /// Halaman TIDAK diberi akses ke objek host apa pun. Satu-satunya jalur
        /// adalah pesan JSON, dan setiap perintah harus dikenali secara eksplisit
        /// di sini.
        /// </summary>
        private void PesanDariHalaman(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var mentah = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(mentah))
                {
                    return;
                }

                var pesan = JsonNode.Parse(mentah!)?.AsObject();
                var perintah = pesan?["cmd"]?.GetValue<string>();

                switch (perintah)
                {
                    case "status":
                        KirimStatus();
                        break;

                    case "bukaFolder":
                        BukaFolderWorkspace(pesan?["path"]?.GetValue<string>());
                        break;

                    case "chat":
                        var prompt = pesan?["prompt"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(prompt) && _tanganiChat is not null)
                        {
                            // Tidak di-await: penanganan chat bisa lama, dan
                            // callback WebView2 tidak boleh diblokir.
                            _ = _tanganiChat(prompt!);
                        }
                        break;

                    default:
                        // Perintah tidak dikenal diabaikan, tidak dilempar.
                        break;
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menangani pesan dari halaman UI", exception);
            }
        }

        /// <summary>Mengirim status terkini ke halaman.</summary>
        internal void KirimStatus()
        {
            try
            {
                if (_webView?.CoreWebView2 is null)
                {
                    return;
                }

                var status = _ambilStatus();
                _webView.CoreWebView2.PostWebMessageAsJson(status.ToJsonString());
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("mengirim status ke halaman UI", exception);
            }
        }

        /// <summary>
        /// Mengirim satu pesan ke percakapan di panel.
        ///
        /// Memakai kanal pesan yang sama dengan status; dibedakan lewat field
        /// jenis. Aman dipanggil dari thread mana pun - WebView2 menuntut
        /// pemanggilan di thread UI, jadi di-marshal lewat BeginInvoke.
        /// </summary>
        internal void KirimPesanChat(string kelas, string teks)
        {
            try
            {
                if (_webView?.CoreWebView2 is null)
                {
                    return;
                }

                var pesan = new JsonObject
                {
                    ["jenis"] = "chat",
                    ["kelas"] = kelas,
                    ["teks"] = teks,
                };

                void Kirim() => _webView!.CoreWebView2.PostWebMessageAsJson(pesan.ToJsonString());

                if (InvokeRequired)
                {
                    BeginInvoke((Action)Kirim);
                }
                else
                {
                    Kirim();
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("mengirim pesan chat ke panel UI", exception);
            }
        }

        /// <summary>Membuka gelembung jawaban baru yang akan diisi bertahap.</summary>
        internal void MulaiPesanChat(string kelas) => KirimKeHalaman("chat-mulai", kelas, string.Empty);

        /// <summary>Menambahkan potongan teks ke gelembung yang sedang terbuka.</summary>
        internal void TambahPotonganChat(string teks) => KirimKeHalaman("chat-potongan", string.Empty, teks);

        /// <summary>Menutup gelembung yang sedang terbuka.</summary>
        internal void SelesaikanPesanChat() => KirimKeHalaman("chat-selesai", string.Empty, string.Empty);

        private void KirimKeHalaman(string jenis, string kelas, string teks)
        {
            try
            {
                if (_webView?.CoreWebView2 is null)
                {
                    return;
                }

                var pesan = new JsonObject
                {
                    ["jenis"] = jenis,
                    ["kelas"] = kelas,
                    ["teks"] = teks,
                };

                void Kirim() => _webView!.CoreWebView2.PostWebMessageAsJson(pesan.ToJsonString());

                if (InvokeRequired)
                {
                    BeginInvoke((Action)Kirim);
                }
                else
                {
                    Kirim();
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError($"mengirim {jenis} ke panel UI", exception);
            }
        }

        private void BukaFolderWorkspace(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    return;
                }

                // Hanya folder MikosAI sendiri yang boleh dibuka - halaman tidak
                // boleh bisa menyuruh kami membuka sembarang lokasi.
                var akarKami = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MikosAI");

                if (!path!.StartsWith(akarKami, StringComparison.OrdinalIgnoreCase))
                {
                    _diagnostics.AddError($"permintaan membuka folder di luar MikosAI ditolak: {path}");
                    return;
                }

                System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("membuka folder workspace", exception);
            }
        }

        private void TampilkanPesanGagal(string teks)
        {
            try
            {
                if (_pesanGagal is not null)
                {
                    _pesanGagal.Text = teks;
                    return;
                }

                _pesanGagal = new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = Color.FromArgb(244, 244, 245),
                    BackColor = Color.FromArgb(24, 24, 27),
                    Padding = new Padding(16),
                    Text = teks,
                };

                Controls.Add(_pesanGagal);
                _pesanGagal.BringToFront();
            }
            catch (Exception)
            {
                // Sudah tidak ada cara lain untuk melapor ke UI.
            }
        }

        /// <summary>
        /// Membuat bitmap 16x16 untuk ikon TaskPane.
        ///
        /// CreateTaskpaneView2 menuntut path bitmap. Dibuat saat runtime supaya
        /// tidak ada berkas biner di repo, dan supaya tidak ada path yang bisa
        /// salah.
        /// </summary>
        internal static string? BuatBitmapIkon(HostDiagnostics diagnostics)
        {
            try
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MikosAI");

                Directory.CreateDirectory(folder);

                var path = Path.Combine(folder, "taskpane-icon.bmp");

                using (var bitmap = new Bitmap(16, 16))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.FromArgb(24, 24, 27));
                        using var kuas = new SolidBrush(Color.FromArgb(99, 102, 241));
                        g.FillEllipse(kuas, 3, 3, 10, 10);
                    }

                    bitmap.Save(path, ImageFormat.Bmp);
                }

                return path;
            }
            catch (Exception exception)
            {
                diagnostics.AddError("membuat bitmap ikon TaskPane", exception);
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (_webView is not null)
                    {
                        if (_webView.CoreWebView2 is not null)
                        {
                            _webView.CoreWebView2.WebMessageReceived -= PesanDariHalaman;
                        }

                        _webView.Dispose();
                    }
                }
                catch (Exception)
                {
                    // Tidak ada yang bisa dilakukan saat menutup.
                }
                finally
                {
                    _webView = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
