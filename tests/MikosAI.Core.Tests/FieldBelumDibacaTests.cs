using System.Collections.Generic;
using System.Linq;
using MikosAI.Core.Execution;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Field yang belum pernah dibaca TIDAK BOLEH dikirim sebagai nilai default
    /// yang terlihat seperti hasil pengukuran.
    ///
    /// BAGAIMANA INI DITEMUKAN.
    ///
    /// Saat meninjau schemas/tools.json bersama, kontraknya dibandingkan dengan
    /// keluaran server yang sedang berjalan. Empat field ternyata diemisikan
    /// tanpa pernah diisi pembaca:
    ///
    ///   hasOpenContour: false   -> "sudah diperiksa, sketsanya tertutup"
    ///   dimensionCount: 0       -> "sketsa ini tanpa dimensi"
    ///   consumedByFeature: null -> "sketsa ini tidak dipakai fitur apa pun"
    ///   parentSketch: null      -> "fitur ini tidak punya sketsa induk"
    ///
    /// Buktinya ada di part acuan: Boss-Extrude1 melaporkan parentSketch null
    /// padahal Sketch1 jelas induknya.
    ///
    /// Yang paling mahal justru yang paling sunyi: golden test kami sendiri
    /// meng-assert `hasOpenContour == false` dan LULUS - padahal nilainya
    /// hardcoded. Assertion yang tidak pernah bisa merah tidak menguji apa pun.
    ///
    /// Daftar fieldsNotPopulated kini DIHITUNG dari bacaan, bukan konstan.
    /// Daftar konstan cukup selama belum ada yang terisi, tapi begitu
    /// implementasinya datang ia akan terus menyebut field yang sudah terisi -
    /// dan mekanisme kejujuran yang berbohong ke arah sebaliknya sama tidak
    /// bergunanya.
    /// </summary>
    public class FieldBelumDibacaTests
    {
        private static IReadOnlyDictionary<string, object?> SketsaPertama(ToolResult hasil)
            => ((IEnumerable<object?>)hasil.Data!["sketches"]!)
                .Cast<IReadOnlyDictionary<string, object?>>()
                .First();

        private static IReadOnlyList<string> Daftar(ToolResult hasil)
            => (IReadOnlyList<string>)hasil.Data!["fieldsNotPopulated"]!;

        // ------------------------------------------------------------------
        // null berarti tidak diketahui, dan harus diakui
        // ------------------------------------------------------------------

        [Fact]
        public void sketsa_yang_belum_dibaca_mengakui_setiap_fieldnya()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new[]
            {
                new SketchReading { Name = "Sketch1" },
            });

            var daftar = Daftar(hasil);

            Assert.Contains("consumedByFeatures", daftar);
            Assert.Contains("dependents", daftar);
            Assert.Contains("drivingDimensionCount", daftar);
            Assert.Contains("drivenDimensionCount", daftar);
            Assert.Contains("unknownDrivenStateCount", daftar);
            Assert.Contains("dimensionCountRead", daftar);
        }

        [Fact]
        public void fitur_yang_belum_dibaca_mengakui_parent_sketches()
        {
            var hasil = ReadOnlyToolShaper.FeatureTree(new FeatureTreeReading
            {
                Features = new[]
                {
                    new FeatureReading { Name = "Boss-Extrude1", TypeName = "Extrusion" },
                },
            });

            Assert.Contains("parentSketches", Daftar(hasil));
        }

        // ------------------------------------------------------------------
        // Daftarnya MENYUSUT per field, bukan sekaligus
        // ------------------------------------------------------------------

        /// <summary>
        /// Inti dari perubahan ini. Kalau daftarnya tidak menyusut saat field
        /// terisi, ia berubah dari mekanisme kejujuran menjadi kebohongan arah
        /// sebaliknya - menyebut field yang sudah ada sebagai tidak ada.
        /// </summary>
        [Fact]
        public void field_yang_sudah_terisi_hilang_dari_daftar()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new[]
            {
                new SketchReading
                {
                    Name = "Sketch1",
                    ConsumedByFeatures = new[] { "Boss-Extrude1" },
                    Dependents = new[] { "Boss-Extrude1", "Sketch2" },
                },
            });

            var daftar = Daftar(hasil);

            Assert.DoesNotContain("consumedByFeatures", daftar);
            Assert.DoesNotContain("dependents", daftar);

            // Yang lain BELUM terisi, jadi harus tetap ada. Menyusut per field,
            // bukan sekaligus.
            Assert.Contains("drivingDimensionCount", daftar);
        }

        /// <summary>
        /// Kosong BUKAN "belum dibaca". Sketsa yang memang tidak dipakai fitur
        /// apa pun sudah terjawab - dan jawabannya adalah daftar kosong.
        /// </summary>
        [Fact]
        public void daftar_kosong_berarti_sudah_dibaca_bukan_belum()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new[]
            {
                new SketchReading
                {
                    Name = "Sketch1",
                    ConsumedByFeatures = new string[0],
                },
            });

            Assert.DoesNotContain("consumedByFeatures", Daftar(hasil));

            var nilai = (IReadOnlyList<string>?)SketsaPertama(hasil)["consumedByFeatures"];
            Assert.NotNull(nilai);
            Assert.Empty(nilai!);
        }

        [Fact]
        public void nol_yang_benar_benar_dihitung_bukan_belum_dibaca()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new[]
            {
                new SketchReading
                {
                    Name = "Sketch1",
                    DrivingDimensionCount = 0,
                    DrivenDimensionCount = 0,
                    UnknownDrivenStateCount = 0,
                    DimensionCountRead = 0,
                },
            });

            var daftar = Daftar(hasil);

            Assert.DoesNotContain("drivingDimensionCount", daftar);
            Assert.DoesNotContain("dimensionCountRead", daftar);
            Assert.Equal(0, SketsaPertama(hasil)["drivingDimensionCount"]);
        }

        /// <summary>
        /// Satu sketsa yang tidak terbaca sudah cukup. Melaporkan "sebagian
        /// terbaca" akan menggoda pemanggil memakai yang terbaca dan melupakan
        /// sisanya.
        /// </summary>
        [Fact]
        public void satu_sketsa_yang_tidak_terbaca_membuat_seluruh_field_diakui()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new[]
            {
                new SketchReading { Name = "Sketch1", ConsumedByFeatures = new[] { "A" } },
                new SketchReading { Name = "Sketch2" },   // tidak terbaca
            });

            Assert.Contains("consumedByFeatures", Daftar(hasil));
        }

        // ------------------------------------------------------------------
        // Konektivitas belum ada sama sekali
        // ------------------------------------------------------------------

        /// <summary>
        /// Definisinya masih menunggu keputusan atas kasus dua loop yang
        /// bersentuhan. Sampai itu selesai, ketiadaannya harus DIAKUI - kalau
        /// tidak, field yang hilang terbaca sebagai "sudah diperiksa dan
        /// bersih".
        /// </summary>
        [Fact]
        public void konektivitas_diakui_belum_diimplementasikan()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new[]
            {
                new SketchReading { Name = "Sketch1" },
            });

            var daftar = Daftar(hasil);

            Assert.Contains("openEndpointCount", daftar);
            Assert.Contains("branchPointCount", daftar);
        }
    }
}
