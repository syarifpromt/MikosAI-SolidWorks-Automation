using System;
using System.Reflection;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// CreateCommandGroup2: 1 berarti BERHASIL, 0 berarti GAGAL.
    ///
    /// KENAPA TEST INI ADA.
    ///
    /// Versi pertama CommandGroupHost memperlakukan "kode bukan nol" sebagai
    /// anomali dan nol sebagai normal - kebalikan persis dari kenyataan. Dari
    /// swconst asli:
    ///
    ///   swCreateCommandGroup_Failed             = 0
    ///   swCreateCommandGroup_Success            = 1
    ///   swCreateCommandGroup_Exceeds_ToolBarIDs = 2
    ///
    /// Akibatnya grup yang GAGAL dibuat akan lolos sebagai sukses, dan satu-
    /// satunya gejalanya adalah tombol yang tidak muncul - tanpa satu baris
    /// pun di diagnostik. Itu kelas kegagalan yang sudah terlalu sering kami
    /// bayar di proyek ini: melapor berhasil sambil gagal.
    ///
    /// Compiler tidak bisa menangkap perbandingan yang terbalik, dan stub
    /// compile-check juga tidak - keduanya soal tipe, bukan soal nilai. Jadi
    /// keputusannya dipisah menjadi fungsi dan dipaku di sini.
    ///
    /// Dibaca lewat refleksi karena nama assembly add-in berbeda antara build
    /// nyata dan compile-check. Lihat AddinAssembly.
    /// </summary>
    public class KodeGrupPerintahTests
    {
        private static MethodInfo Method(string nama)
        {
            var tipe = AddinAssembly.Tipe("MikosAI.Addin.CommandGroupHost");

            return tipe.GetMethod(nama, BindingFlags.Static | BindingFlags.NonPublic)
                   ?? throw new InvalidOperationException(
                       $"CommandGroupHost.{nama} tidak ada lagi - test ini sudah tidak menguji apa pun.");
        }

        private static bool Berhasil(int kode)
            => (bool)Method("GrupBerhasil").Invoke(null, new object[] { kode })!;

        private static string Jelaskan(int kode)
            => (string)Method("JelaskanKodeGrup").Invoke(null, new object[] { kode })!;

        [Fact]
        public void satu_berarti_berhasil()
        {
            Assert.True(Berhasil(1));
        }

        /// <summary>
        /// Inti dari test ini. Nol adalah nilai yang paling mudah dikira
        /// "tidak ada galat", dan di sini ia justru satu-satunya kegagalan.
        /// </summary>
        [Fact]
        public void nol_berarti_gagal_bukan_tanpa_galat()
        {
            Assert.False(Berhasil(0));
        }

        [Fact]
        public void kelebihan_id_toolbar_juga_gagal()
        {
            Assert.False(Berhasil(2));
        }

        [Fact]
        public void kode_tak_dikenal_tidak_dianggap_berhasil()
        {
            // Kalau SolidWorks menambah kode baru, defaultnya harus menolak.
            // Memperlakukan yang tidak dikenal sebagai sukses berarti
            // mengarang jaminan - aturan yang sama dengan verdict BLOCKED
            // untuk status tool yang tidak dikenal.
            Assert.False(Berhasil(3));
            Assert.False(Berhasil(-1));
            Assert.False(Berhasil(int.MaxValue));
        }

        [Fact]
        public void penjelasan_kode_memakai_nama_asli_dari_swconst()
        {
            Assert.Equal("swCreateCommandGroup_Failed", Jelaskan(0));
            Assert.Equal("swCreateCommandGroup_Success", Jelaskan(1));
            Assert.Equal("swCreateCommandGroup_Exceeds_ToolBarIDs", Jelaskan(2));
            Assert.Equal("kode tidak dikenal", Jelaskan(99));
        }
    }
}
