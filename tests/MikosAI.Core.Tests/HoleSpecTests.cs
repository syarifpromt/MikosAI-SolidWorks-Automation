using System;
using System.Linq;
using MikosAI.Core.Plan;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// `HoleSpec` menolak, bukan melengkapi.
    ///
    /// Seluruh berkas ini menguji satu aturan: JANGAN MENEBAK NILAI
    /// ENGINEERING. Standar lubang dan ukuran fastener menentukan apakah baut
    /// benar-benar masuk; memilihkannya untuk pengguna berarti mengarang
    /// keputusan teknik lalu menyajikannya sebagai hasil.
    ///
    /// Yang diuji di sini bukan "apakah nilai yang benar diterima" - itu bagian
    /// mudahnya. Yang diuji: apakah nilai yang HILANG ditolak, alih-alih diisi
    /// default yang terdengar masuk akal.
    /// </summary>
    public class HoleSpecTests
    {
        private static Titik3D[] SatuTitik() => new[] { new Titik3D(20, 0, 10) };

        // ------------------------------------------------------------------
        // Yang sah
        // ------------------------------------------------------------------

        [Fact]
        public void counterbore_lengkap_diterima()
        {
            var spec = HoleSpec.Buat("counterbore", "ISO", "M6", SatuTitik());

            Assert.Equal("counterbore", spec.HoleType);
            Assert.Equal("ISO", spec.Standard);
            Assert.Equal("M6", spec.FastenerSize);
            Assert.Single(spec.Positions);
        }

        /// <summary>
        /// simple TIDAK menuntut standar - diameternya dinyatakan langsung,
        /// bukan diambil dari basis data fastener.
        /// </summary>
        [Fact]
        public void simple_tidak_menuntut_standar()
        {
            var spec = HoleSpec.Buat("simple", null, null, SatuTitik());

            Assert.Equal("simple", spec.HoleType);
            Assert.Null(spec.Standard);
        }

        [Fact]
        public void huruf_besar_kecil_tipe_dinormalkan()
        {
            Assert.Equal("counterbore", HoleSpec.Buat("CounterBore", "ISO", "M6", SatuTitik()).HoleType);
        }

        // ------------------------------------------------------------------
        // Yang ditolak - inti berkas ini
        // ------------------------------------------------------------------

        /// <summary>
        /// Tanpa standar, satu-satunya cara melanjutkan adalah MEMILIHKAN
        /// standar. Itu yang dilarang.
        /// </summary>
        [Fact]
        public void counterbore_tanpa_standar_ditolak_bukan_diberi_default()
        {
            var e = Assert.Throws<ArgumentException>(
                () => HoleSpec.Buat("counterbore", null, "M6", SatuTitik()));

            Assert.Contains("standard", e.Message, StringComparison.OrdinalIgnoreCase);
            // Pesannya wajib menyebut pilihan yang sah, supaya penolakannya bisa
            // ditindaklanjuti alih-alih jadi jalan buntu.
            Assert.Contains("ISO", e.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void counterbore_tanpa_ukuran_fastener_ditolak()
        {
            var e = Assert.Throws<ArgumentException>(
                () => HoleSpec.Buat("counterbore", "ISO", null, SatuTitik()));

            Assert.Contains("fastenerSize", e.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void standar_yang_tidak_dikenal_ditolak()
        {
            var e = Assert.Throws<ArgumentException>(
                () => HoleSpec.Buat("counterbore", "SNI", "M6", SatuTitik()));

            Assert.Contains("SNI", e.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// countersink dan tapped ADA di skema tool, tapi belum punya test
        /// urutan parameter yang lulus di SolidWorks nyata.
        ///
        /// Pesannya sengaja membedakan "belum terverifikasi" dari "tidak
        /// dikenal". Keduanya menolak, tapi hanya yang pertama berarti
        /// "tunggu, sedang dikerjakan".
        /// </summary>
        [Theory]
        [InlineData("countersink")]
        [InlineData("tapped")]
        public void tipe_yang_belum_terverifikasi_ditolak_dengan_sebab_yang_benar(string tipe)
        {
            var e = Assert.Throws<ArgumentException>(
                () => HoleSpec.Buat(tipe, "ISO", "M6", SatuTitik()));

            Assert.Contains("belum terverifikasi", e.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void tipe_kosong_ditolak(string? tipe)
        {
            Assert.Throws<ArgumentException>(() => HoleSpec.Buat(tipe, "ISO", "M6", SatuTitik()));
        }

        [Fact]
        public void tanpa_posisi_ditolak()
        {
            Assert.Throws<ArgumentException>(() => HoleSpec.Buat("counterbore", "ISO", "M6", Array.Empty<Titik3D>()));
            Assert.Throws<ArgumentException>(() => HoleSpec.Buat("counterbore", "ISO", "M6", null));
        }

        /// <summary>
        /// NaN yang lolos akan menjadi titik sketsa yang tidak ada di mana pun,
        /// dan SolidWorks akan gagal jauh dari sini dengan sebab yang tidak
        /// nyambung.
        /// </summary>
        [Fact]
        public void koordinat_bukan_angka_ditolak()
        {
            Assert.Throws<ArgumentException>(
                () => HoleSpec.Buat("counterbore", "ISO", "M6", new[] { new Titik3D(double.NaN, 0, 0) }));

            Assert.Throws<ArgumentException>(
                () => HoleSpec.Buat("counterbore", "ISO", "M6", new[] { new Titik3D(0, double.PositiveInfinity, 0) }));
        }

        // ------------------------------------------------------------------
        // Lingkup M0 dipaku
        // ------------------------------------------------------------------

        /// <summary>
        /// Menambah tipe ke daftar harus jadi keputusan sadar yang disertai
        /// test urutan parameter - bukan sesuatu yang menyelinap masuk.
        /// </summary>
        [Fact]
        public void lingkup_m0_persis_dua_tipe()
        {
            Assert.Equal(new[] { "simple", "counterbore" }, HoleSpec.TipeDibuka);
        }

        [Fact]
        public void empat_posisi_diterima_untuk_mounting_plate()
        {
            // Gerbang v0.1: 4 lubang M6 counterbore dalam SATU fitur.
            // Kontraknya menerima array posisi, jadi linear_pattern belum tentu
            // dibutuhkan.
            var titik = new[]
            {
                new Titik3D(-30, -20, 10), new Titik3D(30, -20, 10),
                new Titik3D(-30, 20, 10), new Titik3D(30, 20, 10),
            };

            var spec = HoleSpec.Buat("counterbore", "ISO", "M6", titik);

            Assert.Equal(4, spec.Positions.Count);
            Assert.Equal(4, spec.Positions.Select(p => p.ToString()).Distinct().Count());
        }
    }
}
