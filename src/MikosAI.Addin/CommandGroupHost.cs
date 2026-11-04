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
        /// swCreateCommandGroup_Success. SATU, bukan nol - lihat catatan di
        /// Bangun(). Nol justru berarti gagal.
        /// </summary>
        private const int SuksesBuatGrup = 1;

        /// <summary>swCommandTabButton_TextBelow, dari swconst asli.</summary>
        private const int TeksDiBawahIkon = 2;

        /// <summary>
        /// Jenis dokumen yang mendapat tab ribbon: part, assembly, drawing
        /// (swDocPART=1, swDocASSEMBLY=2, swDocDRAWING=3 di swconst asli).
        ///
        /// Ketiganya, karena panel ini membaca dokumen apa pun yang aktif.
        /// Memberi tab hanya untuk part akan membuat tombolnya lenyap begitu
        /// pengguna membuka assembly - dan lenyap tanpa sebab yang terlihat
        /// adalah cara tercepat membuat orang mengira add-in-nya mati.
        /// </summary>
        private static readonly int[] JenisDokumenBertab = { 1, 2, 3 };

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
        private int _idPerintah = -1;
        private string _judul = string.Empty;

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
            _judul = judul;

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

            // 1 BERARTI BERHASIL. 0 BERARTI GAGAL. Jangan dibalik.
            //
            // Ini bukan konvensi yang biasa, dan versi pertama kode ini
            // memang membalikkannya - memperlakukan "bukan nol" sebagai
            // anomali, padahal nol justru satu-satunya nilai yang berarti
            // gagal. Diambil dari swconst asli:
            //
            //   swCreateCommandGroup_Failed             = 0
            //   swCreateCommandGroup_Success            = 1
            //   swCreateCommandGroup_Exceeds_ToolBarIDs = 2
            //
            // Komentar sebelumnya di sini menyatakan enum-nya tidak ada di
            // swconst. Itu keliru: namanya `swCreateCommandGroupErrors`, TANPA
            // akhiran `_e`, dan pencarian kamilah yang meleset. Menyimpulkan
            // "tidak ada" dari satu pencarian yang gagal adalah cara yang sama
            // dengan menebak.
            if (grup is null || !GrupBerhasil(galat))
            {
                _diagnostics.AddError(
                    $"CreateCommandGroup2 gagal: kode {galat} ({JelaskanKodeGrup(galat)}), "
                    + $"grup {(grup is null ? "null" : "tidak null")}.");
                return false;
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

            int indeks;

            try
            {
                indeks = grup.AddCommandItem2(
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

            // --- Tab di ribbon CommandManager -------------------------------
            //
            // INI YANG MEMBUAT TOMBOLNYA TERLIHAT.
            //
            // CreateCommandGroup2 + HasToolbar + HasMenu + Activate() sudah
            // cukup untuk membuat tombolnya ADA - tapi hanya di dua tempat yang
            // tidak dilihat siapa pun di SolidWorks modern:
            //
            //   - menu Tools > MikosAI
            //   - toolbar KLASIK (View > Toolbars > MikosAI), yang tersembunyi
            //     selama CommandManager aktif, dan CommandManager aktif secara
            //     default
            //
            // Ribbon dengan tab Features / Sketch / Evaluate itu CommandManager,
            // dan ia TIDAK mengambil grup perintah secara otomatis. Tab harus
            // diminta sendiri: AddCommandTab -> AddCommandTabBox -> AddCommands.
            //
            // Versi pertama melewatkan ketiganya. Hasilnya add-in yang melapor
            // "commandGroup: true" dengan jujur, sementara penggunanya mencari
            // tombol yang tidak ada di tempat yang ia lihat. Diagnostik yang
            // benar tapi tidak menjawab pertanyaan orangnya tetap gagal.
            try
            {
                var idPerintah = grup.CommandID[indeks];
                _idPerintah = idPerintah;

                var jumlahTab = 0;

                foreach (var jenisDokumen in JenisDokumenBertab)
                {
                    if (PasangTab(judul, jenisDokumen, idPerintah))
                    {
                        jumlahTab++;
                    }
                }

                if (jumlahTab == 0)
                {
                    // Tombolnya masih ada di menu Tools, jadi ini bukan
                    // kegagalan total - tapi juga tidak boleh lewat diam-diam,
                    // karena inilah keadaan yang membuat orang mengira tombolnya
                    // tidak dibuat sama sekali.
                    _diagnostics.AddError(
                        "tab ribbon MikosAI tidak bisa dipasang untuk satu jenis dokumen pun. "
                        + "Tombol tetap ada di menu Tools > MikosAI.");
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("memasang tab CommandManager", exception);
            }

            _terpasang = true;
            return true;
        }

        /// <summary>
        /// Memasang satu tab ribbon untuk satu jenis dokumen.
        /// true kalau tabnya benar-benar berisi tombol kita.
        /// </summary>
        private bool PasangTab(string judul, int jenisDokumen, int idPerintah)
        {
            if (_manager is null)
            {
                return false;
            }

            try
            {
                // Tab dari sesi SEBELUMNYA harus dibuang, bukan dipakai ulang.
                //
                // Isinya menunjuk ID perintah sesi lama. ID itu tidak berlaku
                // lagi, jadi tab yang dipakai ulang menghasilkan tombol yang
                // terlihat normal dan tidak melakukan apa-apa - kegagalan yang
                // jauh lebih membingungkan daripada tab yang hilang.
                var lama = _manager.GetCommandTab(jenisDokumen, judul);

                if (lama is not null)
                {
                    _manager.RemoveCommandTab(lama);
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError($"membersihkan tab lama (jenis {jenisDokumen})", exception);
                // Diteruskan: AddCommandTab di bawah masih mungkin berhasil.
            }

            try
            {
                var tab = _manager.AddCommandTab(jenisDokumen, judul);

                if (tab is null)
                {
                    _diagnostics.AddError($"AddCommandTab mengembalikan null (jenis {jenisDokumen}).");
                    return false;
                }

                var kotak = tab.AddCommandTabBox();

                if (kotak is null)
                {
                    _diagnostics.AddError($"AddCommandTabBox mengembalikan null (jenis {jenisDokumen}).");
                    return false;
                }

                // Kedua argumen menyeberang COM sebagai VARIANT berisi
                // SAFEARRAY, jadi panjangnya HARUS sama: satu gaya teks per
                // perintah.
                var idList = new[] { idPerintah };
                var gaya = new[] { TeksDiBawahIkon };

                if (!kotak.AddCommands(idList, gaya))
                {
                    _diagnostics.AddError($"AddCommands mengembalikan false (jenis {jenisDokumen}).");
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError($"memasang tab ribbon (jenis {jenisDokumen})", exception);
                return false;
            }
        }

        /// <summary>
        /// Apakah kode dari CreateCommandGroup2 berarti berhasil.
        ///
        /// Dipisah jadi fungsi SEMATA-MATA supaya bisa diuji. Versi pertama
        /// kode ini memperlakukan "bukan nol" sebagai anomali - kebalikan
        /// persis dari yang benar - dan compiler tidak bisa menangkap
        /// kesalahan seperti itu. Satu-satunya cara memakunya adalah test.
        /// </summary>
        internal static bool GrupBerhasil(int kode) => kode == SuksesBuatGrup;

        internal static string JelaskanKodeGrup(int kode)
        {
            // Dari swconst asli, bukan tebakan.
            switch (kode)
            {
                case 0: return "swCreateCommandGroup_Failed";
                case 1: return "swCreateCommandGroup_Success";
                case 2: return "swCreateCommandGroup_Exceeds_ToolBarIDs";
                default: return "kode tidak dikenal";
            }
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

            // Tab ribbon dibuang lebih dulu. Ia memegang ID perintah milik grup,
            // jadi melepas grupnya duluan meninggalkan tab yang menunjuk ID
            // yang sudah tidak ada.
            foreach (var jenisDokumen in JenisDokumenBertab)
            {
                try
                {
                    var tab = _manager.GetCommandTab(jenisDokumen, _judul);

                    if (tab is not null)
                    {
                        _manager.RemoveCommandTab(tab);
                    }
                }
                catch (Exception exception)
                {
                    _diagnostics.AddError($"RemoveCommandTab (jenis {jenisDokumen})", exception);
                }
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
