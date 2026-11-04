using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SolidWorks.Interop.sldworks;

namespace MikosAI.Addin
{
    /// <summary>
    /// Tombol toolbar MikosAI: menampilkan dan menyembunyikan panel.
    ///
    /// SEMUA TANDA TANGAN DI SINI DIREFLEKSIKAN DARI PIA ASLI, BUKAN DITEBAK.
    /// Dibaca dari SolidWorks.Interop.sldworks.dll pada instalasi nyata:
    ///
    ///   CommandManager GetCommandManager(Int32 Cookie)
    ///   CommandGroup   CreateCommandGroup2(Int32 UserID, String Title,
    ///                      String ToolTip, String Hint, Int32 Position,
    ///                      Boolean IgnorePreviousVersion, ref Int32 Errors)
    ///
    /// `Errors` adalah `ref`, BUKAN `out`. Refleksi melaporkan IsOut=true untuk
    /// parameter ini, dan itu sempat membuat kami menulis `out` - tapi IsIn
    /// JUGA true, yaitu [In][Out], dan C# menuntut `ref` untuk kombinasi itu.
    /// Refleksinya benar; pembacaannya yang salah.
    ///   Int32          AddCommandItem2(String Name, Int32 Position,
    ///                      String HintString, String ToolTip,
    ///                      Int32 ImageListIndex, String CallbackFunction,
    ///                      String EnableMethod, Int32 UserID,
    ///                      Int32 MenuTBOption)
    ///   Int32          RemoveCommandGroup2(Int32 UserID, Boolean RuntimeOnly)
    ///
    /// Aturan proyek: stub compile-check HARUS mengikuti bentuk ini. Kalau
    /// compile-check lulus tapi build nyata gagal, yang benar adalah build
    /// nyata - perbaiki stub, jangan kodenya.
    ///
    /// KENAPA TOMBOLNYA DATANG TERAKHIR.
    ///
    /// Tombol adalah satu-satunya bagian add-in yang memanggil balik KE kode
    /// kita berdasarkan NAMA METHOD lewat IDispatch. Itu permukaan yang tidak
    /// bisa diperiksa compiler: salah ketik nama callback menghasilkan tombol
    /// yang diam, bukan error build. Jadi ia ditambahkan setelah jalur yang
    /// lebih berisiko terbukti hidup, bukan sebelum.
    ///
    /// ATURAN THREAD: setiap method publik di kelas ini hanya boleh dipanggil
    /// dari thread utama SolidWorks.
    /// </summary>
    internal sealed class CommandGroupHost : IDisposable
    {
        /// <summary>
        /// ID grup perintah. HARUS STABIL setelah rilis pertama.
        ///
        /// SolidWorks menyimpan posisi toolbar dan preferensi pengguna dengan
        /// ID ini sebagai kunci. Mengubahnya berarti tombol pengguna pindah
        /// sendiri dan muncul grup kedua yang kosong - alasan yang sama kenapa
        /// GUID add-in tidak boleh berubah.
        /// </summary>
        internal const int UserId = 0x4D49_4B53;

        /// <summary>ID item di dalam grup. Stabil karena alasan yang sama.</summary>
        internal const int ItemIdTogglePanel = 0;

        /// <summary>
        /// swMenuItem (1) | swToolbarItem (2). Nilainya dari swconst asli:
        /// enum itu HANYA punya swMenuItem=1 dan swToolbarItem=2, jadi
        /// "keduanya" adalah OR bitwise, bukan anggota enum tersendiri.
        /// </summary>
        private const int MenuDanToolbar = 3;

        /// <summary>-1 berarti ditaruh di akhir.</summary>
        private const int PosisiAkhir = -1;

        /// <summary>
        /// Nama method callback, sebagai STRING - dan itu bahayanya.
        ///
        /// SolidWorks memanggilnya lewat IDispatch berdasarkan nama. Compiler
        /// tidak pernah melihat hubungan ini, jadi `nameof` dipakai supaya
        /// mengganti nama method ikut mengganti string ini. Itu satu-satunya
        /// perlindungan yang bisa didapat di jalur yang memang late-bound.
        /// </summary>
        private static readonly string NamaCallback = nameof(MikosAiAddin.MikosAiTogglePanel);

        private static readonly string NamaEnable = nameof(MikosAiAddin.MikosAiStatusTombolPanel);

        private readonly HostDiagnostics _diagnostics;

        private ICommandManager? _manager;
        private bool _terpasang;

        internal CommandGroupHost(HostDiagnostics diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>
        /// Membangun grup perintah. true kalau tombolnya benar-benar aktif.
        ///
        /// Kegagalan di sini TIDAK BOLEH menggagalkan pemuatan add-in: panel
        /// masih bisa dibuka dari daftar TaskPane di sisi kanan SolidWorks,
        /// jadi kehilangan tombol itu merepotkan, bukan fatal.
        /// </summary>
        internal bool Bangun(ICommandManager? manager, string judul)
        {
            if (manager is null)
            {
                // Sebabnya sudah dicatat oleh SolidWorksApi.
                return false;
            }

            _manager = manager;

            ICommandGroup? grup;
            var galat = 0;

            try
            {
                // IgnorePreviousVersion: true.
                //
                // Artinya "buang data grup lama dari registry dan bangun ulang".
                // Dipilih SENGAJA selama proyek masih berubah: kalau false,
                // SolidWorks mempertahankan daftar item versi sebelumnya, dan
                // item yang kita hapus dari kode tetap muncul sebagai tombol
                // mati yang memanggil callback yang sudah tidak ada.
                //
                // Harganya: tata letak toolbar yang dipindahkan pengguna ikut
                // tereset. Itu dapat diterima SEKARANG karena belum ada
                // pengguna yang menata apa pun; begitu ada rilis stabil, ini
                // harus menjadi false dan perubahan item dikelola dengan ID.
                grup = _manager.CreateCommandGroup2(
                    UserId,
                    judul,
                    "Panel otomasi MikosAI",
                    "Tampilkan atau sembunyikan panel MikosAI",
                    PosisiAkhir,
                    true,
                    ref galat);
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("CreateCommandGroup2", exception);
                return false;
            }

            if (grup is null)
            {
                // Kode galat disertakan apa adanya. Tidak dipetakan ke teks,
                // karena enum-nya tidak ada di swconst versi ini - menebak
                // artinya akan menghasilkan diagnostik yang terdengar pasti
                // padahal karangan.
                _diagnostics.AddError(
                    $"CreateCommandGroup2 mengembalikan null (kode galat {galat}).");
                return false;
            }

            if (galat != 0)
            {
                // Bukan kegagalan: grupnya ada. Tapi dicatat, karena nilai
                // bukan-nol di sini biasanya berarti data registry lama ikut
                // bicara.
                _diagnostics.AddError(
                    $"info: CreateCommandGroup2 berhasil dengan kode galat {galat}.");
            }

            // Ikon. Kegagalan di sini dibiarkan: tombol tanpa ikon tetap
            // bisa diklik, dan menolak membangun grup gara-gara gambar akan
            // menukar masalah kecil dengan masalah besar.
            try
            {
                var ikon = BuatStripIkon();
                if (ikon is not null)
                {
                    grup.IconList = ikon;
                    grup.MainIconList = ikon;
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("memasang ikon grup perintah", exception);
            }

            try
            {
                var indeks = grup.AddCommandItem2(
                    "Panel MikosAI",
                    PosisiAkhir,
                    "Tampilkan atau sembunyikan panel MikosAI",
                    "Panel MikosAI",
                    0,
                    NamaCallback,
                    NamaEnable,
                    ItemIdTogglePanel,
                    MenuDanToolbar);

                if (indeks < 0)
                {
                    _diagnostics.AddError(
                        $"AddCommandItem2 menolak item (indeks {indeks}).");
                    return false;
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("AddCommandItem2", exception);
                return false;
            }

            try
            {
                grup.HasToolbar = true;
                grup.HasMenu = true;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menyetel HasToolbar/HasMenu", exception);
            }

            try
            {
                if (!grup.Activate())
                {
                    // Activate() false berarti grupnya TIDAK tampil. Ini satu
                    // dari dua tempat di mana pembangunan bisa "berhasil"
                    // tanpa tombol yang terlihat, jadi dilaporkan sebagai
                    // kegagalan alih-alih dibiarkan jadi tombol hantu.
                    _diagnostics.AddError("CommandGroup.Activate mengembalikan false.");
                    return false;
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("CommandGroup.Activate", exception);
                return false;
            }

            _terpasang = true;
            return true;
        }

        /// <summary>
        /// Membuat strip bitmap untuk ikon toolbar, satu berkas per ukuran.
        ///
        /// SolidWorks menerima satu array path lewat IconList dan memilih
        /// ukuran yang cocok dengan setelan tampilan pengguna. Strip-nya
        /// selebar (jumlah item x sisi); kita punya satu item, jadi lebarnya
        /// sama dengan sisinya.
        ///
        /// Dibuat saat runtime, bukan dikomit sebagai biner - alasan yang sama
        /// dengan ikon TaskPane: tidak ada berkas biner di repo, dan tidak ada
        /// path yang bisa salah.
        /// </summary>
        private string[]? BuatStripIkon()
        {
            // Ukuran yang diminta SolidWorks modern. Memberi semuanya lebih
            // murah daripada menebak mana yang dipakai.
            var ukuran = new[] { 20, 32, 40, 64, 96, 128 };

            try
            {
                var folder = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "MikosAI",
                    "ikon");

                Directory.CreateDirectory(folder);

                var hasil = new string[ukuran.Length];

                for (var i = 0; i < ukuran.Length; i++)
                {
                    var sisi = ukuran[i];
                    var path = Path.Combine(folder, $"toolbar-{sisi}.bmp");

                    using (var bitmap = new Bitmap(sisi, sisi))
                    {
                        using (var g = Graphics.FromImage(bitmap))
                        {
                            // Magenta sebagai warna transparan: itu konvensi
                            // SolidWorks untuk bitmap toolbar.
                            g.Clear(Color.FromArgb(255, 0, 255));

                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                            var tepi = Math.Max(1, sisi / 8);
                            var diameter = sisi - (tepi * 2);

                            using var kuas = new SolidBrush(Color.FromArgb(99, 102, 241));
                            g.FillEllipse(kuas, tepi, tepi, diameter, diameter);

                            var dalam = Math.Max(1, sisi / 4);
                            using var kuasDalam = new SolidBrush(Color.FromArgb(24, 24, 27));
                            g.FillEllipse(
                                kuasDalam,
                                dalam,
                                dalam,
                                sisi - (dalam * 2),
                                sisi - (dalam * 2));
                        }

                        bitmap.Save(path, ImageFormat.Bmp);
                    }

                    hasil[i] = path;
                }

                return hasil;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("membuat strip ikon toolbar", exception);
                return null;
            }
        }

        /// <summary>
        /// Melepas grup saat add-in dilepas.
        ///
        /// RuntimeOnly: true. Yang dibuang hanya grup di sesi ini, bukan data
        /// registry-nya - jadi tata letak toolbar pengguna tidak ikut hilang
        /// setiap kali SolidWorks ditutup.
        /// </summary>
        public void Dispose()
        {
            if (!_terpasang || _manager is null)
            {
                _manager = null;
                return;
            }

            try
            {
                _manager.RemoveCommandGroup2(UserId, true);
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("RemoveCommandGroup2", exception);
            }
            finally
            {
                _terpasang = false;
                _manager = null;
            }
        }
    }
}
