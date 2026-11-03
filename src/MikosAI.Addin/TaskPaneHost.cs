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
        private bool _sudahDipasang;

        // Panel polos: WinForms murni, tanpa satu pun komponen native.
        private TextBox? _teksStatus;
        private TextBox? _teksChat;
        private TextBox? _masukan;
        private Button? _tombolKirim;
        private bool _gelembungTerbuka;

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

            // WebView2 SENGAJA TIDAK dibuat di sini. Lihat Pasang().
        }

        /// <summary>
        /// Membuat dan menginisialisasi WebView2, SETELAH kontrol ini
        /// benar-benar terpasang di TaskPane SolidWorks.
        ///
        /// KENAPA DIPISAH DARI KONSTRUKTOR - INI MENJATUHKAN SOLIDWORKS.
        ///
        /// Versi pertama membuat WebView2 di konstruktor. Urutannya jadi:
        /// kontrol dibuat, handle dipaksa ada, lalu handle itu diserahkan ke
        /// SolidWorks lewat DisplayWindowFromHandlex64 - yang me-REPARENT
        /// kontrol kita ke hierarki jendela SolidWorks.
        ///
        /// Di WinForms, reparent bisa memicu PEMBUATAN ULANG HANDLE. Sementara
        /// itu inisialisasi WebView2 berjalan asinkron dan mengikat dirinya ke
        /// HWND yang lama. WebView2 lalu beroperasi di atas jendela yang sudah
        /// mati, dan matinya di kode native - SolidWorks berhenti dengan Fault
        /// Module kosong.
        ///
        /// Dipersempit dengan saklar fitur: mematikan TaskPane menghentikan
        /// crash, mematikan subsistem lain tidak.
        ///
        /// Sekarang: SolidWorks memasang kontrol lebih dulu, baru WebView2
        /// dibuat - dan itu pun lewat BeginInvoke, supaya SolidWorks selesai
        /// menata jendelanya dalam siklus pesan ini sebelum kita menyentuh
        /// apa pun.
        ///
        /// JANGAN pindahkan pembuatan WebView2 kembali ke konstruktor.
        /// </summary>
        internal void Pasang()
        {
            if (_sudahDipasang)
            {
                return;
            }

            _sudahDipasang = true;

            void Buat()
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated)
                    {
                        _diagnostics.AddError(
                            "panel tidak punya handle saat WebView2 akan dibuat - dilewati.");
                        return;
                    }

                    // Default: panel WinForms biasa. WebView2 opt-in, karena ia
                    // menjatuhkan SolidWorks lewat crash native yang tidak bisa
                    // ditangkap. Lihat Fitur.PakaiWebView2.
                    if (!Fitur.PakaiWebView2)
                    {
                        _diagnostics.Jejak("panel:polos-mulai");
                        BangunPanelPolos();
                        _diagnostics.Jejak("panel:polos-siap");
                        return;
                    }

                    _diagnostics.Jejak("webview:buat-kontrol");

                    _webView = new WebView2 { Dock = DockStyle.Fill };
                    Controls.Add(_webView);

                    // Asinkron, tidak di-await: menunggu di thread utama
                    // SolidWorks akan membekukan UI-nya.
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

            try
            {
                if (IsHandleCreated)
                {
                    // Ditunda ke siklus pesan berikutnya, supaya SolidWorks
                    // selesai menata jendelanya lebih dulu.
                    BeginInvoke((Action)Buat);
                }
                else
                {
                    // Handle belum ada: tunggu sampai dibuat.
                    HandleCreated += (s, e) =>
                    {
                        try
                        {
                            BeginInvoke((Action)Buat);
                        }
                        catch (Exception exception)
                        {
                            _diagnostics.AddError("menjadwalkan WebView2 setelah handle dibuat", exception);
                        }
                    };
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menjadwalkan pembuatan WebView2", exception);
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

                _diagnostics.Jejak("webview:buat-lingkungan");

                var lingkungan = await CoreWebView2Environment
                    .CreateAsync(browserExecutableFolder: null, userDataFolder: folderData)
                    .ConfigureAwait(true);

                _diagnostics.Jejak("webview:ensure-core");
                await _webView!.EnsureCoreWebView2Async(lingkungan).ConfigureAwait(true);
                _diagnostics.Jejak("webview:core-siap");

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

                _diagnostics.Jejak("webview:navigasi");
                core.Navigate(new Uri(halaman).AbsoluteUri);
                SiapPakai = true;
                _diagnostics.Jejak("webview:siap");
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
        /// <summary>
        /// Mengirim status terkini ke halaman.
        ///
        /// WAJIB di-marshal ke thread UI, dan bukan hanya karena WebView2.
        ///
        /// Thread UI panel ini ADALAH thread utama SolidWorks, dan
        /// <c>_ambilStatus()</c> membaca keadaan model. Memanggilnya dari thread
        /// latar berarti menyentuh API SolidWorks dari thread yang salah - dan
        /// itu MENJATUHKAN SolidWorks, bukan sekadar melempar exception.
        ///
        /// Pernah terjadi: pemeriksaan provider di latar memanggil method ini
        /// secara langsung, dan SolidWorks mati dengan Fault Module kosong.
        /// </summary>
        internal void KirimStatus()
        {
            try
            {
                if (_teksStatus is null && _webView?.CoreWebView2 is null)
                {
                    return;
                }

                void Kirim()
                {
                    try
                    {
                        var status = _ambilStatus();

                        if (_teksStatus is not null)
                        {
                            PerbaruiStatusPolos(status);
                            return;
                        }

                        _webView?.CoreWebView2?.PostWebMessageAsJson(status.ToJsonString());
                    }
                    catch (Exception exception)
                    {
                        _diagnostics.AddError("menyusun status untuk panel UI", exception);
                    }
                }

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
                if (_teksChat is not null)
                {
                    TanganiChatPolos("chat", kelas, teks);
                    return;
                }

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
                if (_teksChat is not null)
                {
                    TanganiChatPolos(jenis, kelas, teks);
                    return;
                }

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

                // Badan fungsi ini WAJIB punya try/catch sendiri.
                //
                // BeginInvoke menjalankannya NANTI, di luar try/catch di bawah.
                // Kalau _webView sudah null atau CoreWebView2 sudah dilepas saat
                // itu, exception-nya lolos ke message loop - dan exception yang
                // lolos di sana menjatuhkan SolidWorks.
                void Kirim()
                {
                    try
                    {
                        _webView?.CoreWebView2?.PostWebMessageAsJson(pesan.ToJsonString());
                    }
                    catch (Exception exception)
                    {
                        _diagnostics.AddError($"mengirim {jenis} (tertunda)", exception);
                    }
                }

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

        // ====================================================================
        // Panel polos - WinForms murni
        // ====================================================================

        private static readonly Color WarnaLatar = Color.FromArgb(24, 24, 27);
        private static readonly Color WarnaTeks = Color.FromArgb(240, 240, 245);
        private static readonly Color WarnaRedup = Color.FromArgb(150, 150, 160);

        /// <summary>
        /// Membangun panel tanpa satu pun komponen native.
        ///
        /// Urutan penambahan kontrol PENTING di WinForms: kontrol ber-Dock Fill
        /// harus ditambahkan TERAKHIR, supaya ia mengisi sisa ruang setelah
        /// yang lain mengambil bagiannya.
        /// </summary>
        private void BangunPanelPolos()
        {
            var fontMono = new Font("Consolas", 8.25f);

            // --- Baris masukan, di bawah ---
            var barisMasukan = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                BackColor = WarnaLatar,
                Padding = new Padding(6),
            };

            _tombolKirim = new Button
            {
                Dock = DockStyle.Right,
                Width = 62,
                Text = "Kirim",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(49, 46, 129),
                ForeColor = WarnaTeks,
                Enabled = false,
            };
            _tombolKirim.FlatAppearance.BorderColor = Color.FromArgb(63, 58, 160);
            _tombolKirim.Click += (s, e) => KirimDariPanelPolos();

            _masukan = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                BackColor = Color.FromArgb(18, 18, 21),
                ForeColor = WarnaTeks,
                BorderStyle = BorderStyle.FixedSingle,
                Enabled = false,
            };

            // Enter mengirim, Shift+Enter baris baru.
            _masukan.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    KirimDariPanelPolos();
                }
            };

            barisMasukan.Controls.Add(_masukan);
            barisMasukan.Controls.Add(_tombolKirim);

            // --- Status, di atas ---
            _teksStatus = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 190,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(31, 31, 35),
                ForeColor = WarnaRedup,
                Font = fontMono,
                Text = "MikosAI" + Environment.NewLine + Environment.NewLine + "Menunggu status...",
            };

            // --- Percakapan, mengisi sisanya. Ditambahkan TERAKHIR. ---
            _teksChat = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = WarnaLatar,
                ForeColor = WarnaTeks,
                Font = fontMono,
            };

            Controls.Add(_teksChat);
            Controls.Add(_teksStatus);
            Controls.Add(barisMasukan);

            SiapPakai = true;
        }

        private void KirimDariPanelPolos()
        {
            try
            {
                var teks = _masukan?.Text?.Trim();

                if (string.IsNullOrEmpty(teks) || _tanganiChat is null)
                {
                    return;
                }

                _masukan!.Text = string.Empty;
                TambahBarisChat(Environment.NewLine + "Anda: " + teks);

                // Tidak di-await: penanganan chat bisa lama, dan thread UI
                // adalah thread utama SolidWorks - memblokirnya membekukan CAD.
                _ = _tanganiChat(teks!);
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("mengirim chat dari panel polos", exception);
            }
        }

        /// <summary>
        /// Menambahkan teks ke percakapan. Aman dipanggil dari thread mana pun.
        /// </summary>
        private void TambahBarisChat(string teks)
        {
            if (_teksChat is null)
            {
                return;
            }

            void Tambah()
            {
                try
                {
                    _teksChat.AppendText(teks);
                    _teksChat.SelectionStart = _teksChat.TextLength;
                    _teksChat.ScrollToCaret();
                }
                catch (Exception exception)
                {
                    _diagnostics.AddError("menulis ke percakapan panel polos", exception);
                }
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke((Action)Tambah);
                }
                else
                {
                    Tambah();
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menjadwalkan penulisan percakapan", exception);
            }
        }

        private void PerbaruiStatusPolos(JsonObject status)
        {
            if (_teksStatus is null)
            {
                return;
            }

            try
            {
                _teksStatus.Text = RingkasStatusPolos(status);

                // Kotak chat hanya aktif kalau provider benar-benar siap -
                // aturan yang sama dengan panel WebView2.
                var bolehKirim = status["provider"] is JsonObject provider
                                 && provider["mayAttempt"] is JsonValue nilai
                                 && nilai.TryGetValue<bool>(out var boleh)
                                 && boleh;

                if (_masukan is not null) _masukan.Enabled = bolehKirim;
                if (_tombolKirim is not null) _tombolKirim.Enabled = bolehKirim;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("memperbarui status panel polos", exception);
            }
        }

        private static string RingkasStatusPolos(JsonObject status)
        {
            string Ambil(string kunci) => status[kunci]?.ToJsonString()?.Trim('"') ?? "-";

            var sb = new System.Text.StringBuilder();

            sb.AppendLine("SOLIDWORKS");
            sb.AppendLine("  status    : " + Ambil("status"));
            sb.AppendLine("  revisi    : " + Ambil("solidworksRevision"));
            sb.AppendLine("  dokumen   : " + Ambil("openDocumentCount"));
            sb.AppendLine();

            sb.AppendLine("SERVER MCP");
            sb.AppendLine("  berjalan  : " + Ambil("mcpRunning"));
            sb.AppendLine("  endpoint  : " + Ambil("mcpEndpoint"));
            sb.AppendLine("  tool      : " + Ambil("mcpToolCount"));
            sb.AppendLine();

            if (status["provider"] is JsonObject provider)
            {
                string AmbilP(string kunci) => provider[kunci]?.ToJsonString()?.Trim('"') ?? "-";

                sb.AppendLine("PROVIDER AI");
                sb.AppendLine("  keadaan   : " + AmbilP("description"));
                sb.AppendLine("  versi     : " + AmbilP("version"));
                sb.AppendLine("  tool MCP  : " + AmbilP("mcpRegistered"));

                var tindakan = provider["nextAction"]?.ToJsonString()?.Trim('"');
                if (!string.IsNullOrEmpty(tindakan) && tindakan != "null")
                {
                    sb.AppendLine("  -> " + tindakan);
                }

                sb.AppendLine();
            }

            if (status["errors"] is JsonArray galat && galat.Count > 0)
            {
                sb.AppendLine("ERROR");
                foreach (var item in galat)
                {
                    sb.AppendLine("  " + item?.ToJsonString()?.Trim('"'));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Menerjemahkan pesan chat ke panel polos.
        ///
        /// Jawaban yang mengalir ditulis potongan demi potongan ke kotak yang
        /// sama, tanpa label berulang - hasilnya terbaca seperti satu paragraf
        /// yang tumbuh, bukan deretan baris terpisah.
        /// </summary>
        private void TanganiChatPolos(string jenis, string kelas, string teks)
        {
            switch (jenis)
            {
                case "chat-mulai":
                    _gelembungTerbuka = true;
                    TambahBarisChat(Environment.NewLine + "AI: ");
                    break;

                case "chat-potongan":
                    TambahBarisChat(teks);
                    break;

                case "chat-selesai":
                    _gelembungTerbuka = false;
                    TambahBarisChat(Environment.NewLine);
                    break;

                default:
                    // Pesan utuh. Kalau ada gelembung terbuka, tutup dulu.
                    if (_gelembungTerbuka)
                    {
                        _gelembungTerbuka = false;
                        TambahBarisChat(Environment.NewLine);
                    }

                    var awalan = kelas == "galat" ? "GALAT: " : "AI: ";
                    TambahBarisChat(Environment.NewLine + awalan + teks + Environment.NewLine);
                    break;
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

        /// <summary>
        /// Handle dibuat ulang setelah WebView2 terikat ke yang lama.
        ///
        /// Ini keadaan yang MENJATUHKAN SolidWorks di versi sebelumnya. Tidak
        /// bisa diperbaiki dari sini - WebView2 sudah terikat ke jendela yang
        /// mati. Yang bisa dilakukan: mencatatnya, supaya kalau terjadi lagi
        /// penyebabnya terlihat alih-alih jadi crash tanpa jejak.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (_webView is not null)
            {
                _diagnostics.AddError(
                    "handle panel dibuat ULANG setelah WebView2 terpasang - "
                    + "WebView2 kini terikat ke jendela yang sudah tidak berlaku.");
                _diagnostics.Jejak("webview:handle-dibuat-ulang");
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
