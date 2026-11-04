using System.Collections.Generic;
using MikosAI.Core.Execution;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// HITUNGAN NOL ITU FAKTA. UKURAN NOL ITU KARANGAN.
    ///
    /// Bedanya halus dan mahal. "Ada nol fitur" itu pernyataan benar tentang
    /// dunia. "Kotak pembatasnya 0 x 0 x 0 mm" BUKAN - kotak pembatasnya tidak
    /// ada, dan itu dua pernyataan yang berbeda.
    ///
    /// Ini ditemukan dari pembacaan sungguhan di SolidWorks tanpa dokumen
    /// terbuka. Jawabannya memuat, dalam satu tool yang sama:
    ///
    ///     "volumeMm3": 0,
    ///     "massGrams": null,       &lt;- benar, disertai catatan tidak dikarang
    ///     "surfaceAreaMm2": 0,
    ///
    /// Disiplinnya sudah dipegang untuk massa dan terlewat untuk sisanya.
    ///
    /// Nol berbahaya justru karena ia tampak masuk akal. Model yang membacanya
    /// punya segala alasan melaporkan "part-nya 0 x 0 x 0 mm" alih-alih "tidak
    /// ada body", dan laporan itu akan terdengar persis seperti pengukuran
    /// sungguhan. null tidak bisa disalahpahami begitu.
    /// </summary>
    public class UkuranTidakAdaBukanNolTests
    {
        // -------------------------------------------------------------------
        // get_bounding_box
        // -------------------------------------------------------------------

        [Fact]
        public void kotak_pembatas_tanpa_body_melaporkan_null_bukan_nol()
        {
            var hasil = ReadOnlyToolShaper.BoundingBox(new BoundingBoxReading
            {
                Min = Vector3Reading.Zero,
                Max = Vector3Reading.Zero,
                BodyCount = 0,
            });

            Assert.NotNull(hasil.Data);
            Assert.Null(hasil.Data!["min"]);
            Assert.Null(hasil.Data["max"]);
            Assert.Null(hasil.Data["size"]);

            // bodyCount TETAP angka: ini hitungan, dan nol benar.
            Assert.Equal(0, hasil.Data["bodyCount"]);
        }

        [Fact]
        public void kotak_pembatas_dengan_body_melaporkan_angkanya()
        {
            var hasil = ReadOnlyToolShaper.BoundingBox(new BoundingBoxReading
            {
                Min = new Vector3Reading { X = -10, Y = -5, Z = 0 },
                Max = new Vector3Reading { X = 10, Y = 5, Z = 3 },
                BodyCount = 1,
            });

            var size = Assert.IsType<Dictionary<string, object?>>(hasil.Data!["size"]);

            Assert.Equal(20d, size["x"]);
            Assert.Equal(10d, size["y"]);
            Assert.Equal(3d, size["z"]);
        }

        /// <summary>
        /// Kasus kontradiktif: tidak ada body, tapi kotaknya bukan nol.
        ///
        /// Di sini angkanya HARUS tetap dilaporkan. Menyembunyikannya di balik
        /// null akan menghapus satu-satunya bukti bahwa pembacaannya tidak
        /// konsisten - dan verifikasinya memang menaikkan ini jadi FAIL wajib.
        /// </summary>
        [Fact]
        public void kotak_pembatas_kontradiktif_tetap_melaporkan_angkanya_sebagai_bukti()
        {
            var hasil = ReadOnlyToolShaper.BoundingBox(new BoundingBoxReading
            {
                Min = Vector3Reading.Zero,
                Max = new Vector3Reading { X = 50, Y = 50, Z = 50 },
                BodyCount = 0,
            });

            Assert.NotNull(hasil.Data!["min"]);
            Assert.NotNull(hasil.Data["max"]);
            Assert.NotNull(hasil.Data["size"]);

            var check = Assert.Single(hasil.Verification!.Checks);
            Assert.Equal("has_solid_body", check.Id);
            Assert.Equal(Verdict.Fail, check.Verdict);
            Assert.True(check.Required);
        }

        // -------------------------------------------------------------------
        // get_mass_properties
        // -------------------------------------------------------------------

        [Fact]
        public void properti_massa_tanpa_body_melaporkan_null_bukan_nol()
        {
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 0,
                SurfaceAreaMm2 = 0,
                CenterOfMass = Vector3Reading.Zero,
                MassGrams = null,
                DensityAssigned = false,
            });

            Assert.Null(hasil.Data!["volumeMm3"]);
            Assert.Null(hasil.Data["surfaceAreaMm2"]);
            Assert.Null(hasil.Data["centerOfMass"]);
            Assert.Null(hasil.Data["massGrams"]);

            // densityAssigned TETAP bool: ini fakta, bukan ukuran.
            Assert.Equal(false, hasil.Data["densityAssigned"]);
        }

        /// <summary>
        /// Surface body punya area tanpa volume, dan itu pembacaan SUNGGUHAN.
        ///
        /// Kalau area digantungkan pada volume, pembacaan yang sah ini akan
        /// dihapus menjadi null - kehilangan informasi, bukan kejujuran.
        /// </summary>
        [Fact]
        public void area_tanpa_volume_tetap_dilaporkan_karena_surface_body_itu_nyata()
        {
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 0,
                SurfaceAreaMm2 = 1250.5,
                CenterOfMass = Vector3Reading.Zero,
                MassGrams = null,
                DensityAssigned = false,
            });

            Assert.Null(hasil.Data!["volumeMm3"]);
            Assert.Equal(1250.5, hasil.Data["surfaceAreaMm2"]);
        }

        /// <summary>
        /// {0,0,0} adalah titik pusat massa yang SAH untuk part simetris di
        /// titik asal.
        ///
        /// Karena itu centerOfMass tidak boleh dinilai dari isinya sendiri. Ia
        /// digantungkan pada volume: ada volume berarti ada massa, dan titik
        /// pusatnya punya arti - termasuk kalau nilainya nol.
        /// </summary>
        [Fact]
        public void titik_pusat_massa_nol_tetap_dilaporkan_kalau_ada_volume()
        {
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 8000,
                SurfaceAreaMm2 = 2400,
                CenterOfMass = Vector3Reading.Zero,
                MassGrams = 62.8,
                DensityAssigned = true,
            });

            var pusat = Assert.IsType<Dictionary<string, object?>>(hasil.Data!["centerOfMass"]);

            Assert.Equal(0d, pusat["x"]);
            Assert.Equal(0d, pusat["y"]);
            Assert.Equal(0d, pusat["z"]);

            Assert.Equal(8000d, hasil.Data["volumeMm3"]);
            Assert.Equal(62.8, hasil.Data["massGrams"]);
        }

        // -------------------------------------------------------------------
        // Hitungan tidak boleh ikut dinull-kan
        // -------------------------------------------------------------------

        /// <summary>
        /// Tool yang melaporkan HITUNGAN harus tetap melaporkan nol.
        ///
        /// Perbaikan ini mudah dibawa terlalu jauh: menull-kan segala nol akan
        /// membuat "ada nol fitur" - yang merupakan fakta berguna - jadi tidak
        /// bisa dibedakan dari "jumlah fitur tidak terbaca".
        /// </summary>
        [Fact]
        public void hitungan_nol_tetap_nol_bukan_null()
        {
            var fitur = ReadOnlyToolShaper.FeatureTree(new FeatureTreeReading());
            Assert.Equal(0, fitur.Data!["featureCount"]);

            var sketsa = ReadOnlyToolShaper.Sketches(new List<SketchReading>());
            Assert.Equal(0, sketsa.Data!["sketchCount"]);

            var seleksi = ReadOnlyToolShaper.Selection(new List<SelectionReading>());
            Assert.Equal(0, seleksi.Data!["count"]);
        }
    }
}
