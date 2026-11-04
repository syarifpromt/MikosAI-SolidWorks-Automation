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
    /// padahal Sketch1 jelas induknya, dan Sketch1 melaporkan consumedByFeature
    /// null padahal ia jelas dikonsumsi Boss-Extrude1.
    ///
    /// Yang paling mahal justru yang paling sunyi: golden test kami sendiri
    /// meng-assert `hasOpenContour == false` dan LULUS - padahal nilainya
    /// hardcoded. Assertion yang tidak pernah bisa merah tidak menguji apa pun,
    /// dan ia menambah rasa aman tanpa menambah jaminan.
    /// </summary>
    public class FieldBelumDibacaTests
    {
        private static IReadOnlyDictionary<string, object?> SketsaPertama(ToolResult hasil)
            => ((IEnumerable<object?>)hasil.Data!["sketches"]!)
                .Cast<IReadOnlyDictionary<string, object?>>()
                .First();

        private static IReadOnlyList<string> Daftar(ToolResult hasil)
            => (IReadOnlyList<string>)hasil.Data!["fieldsNotPopulated"]!;

        /// <summary>
        /// Nol yang tidak pernah dihitung adalah karangan yang kebetulan
        /// terlihat seperti hitungan. Aturan kami sudah menyatakan "ukuran nol
        /// itu karangan"; ini penerapannya pada hitungan yang tidak dihitung.
        /// </summary>
        [Fact]
        public void dimension_count_yang_belum_dibaca_adalah_null_bukan_nol()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new[]
            {
                new SketchReading
                {
                    Name = "Sketch1",
                    ConstrainedStatus = SketchConstraintStatus.FullyDefined,
                    EntityCount = 6,
                    DimensionCount = null,
                },
            });

            Assert.Null(SketsaPertama(hasil)["dimensionCount"]);
        }

        /// <summary>
        /// Ini yang paling berbahaya begitu tool tulis ada: `false` di sini
        /// terbaca sebagai "sudah diperiksa dan tertutup".
        /// </summary>
        [Fact]
        public void has_open_contour_yang_belum_diperiksa_adalah_null_bukan_false()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new[]
            {
                new SketchReading
                {
                    Name = "Sketch1",
                    ConstrainedStatus = SketchConstraintStatus.FullyDefined,
                    HasOpenContour = null,
                },
            });

            Assert.Null(SketsaPertama(hasil)["hasOpenContour"]);
        }

        /// <summary>
        /// null tetap terbaca sebagai "tidak ada", bukan "tidak diketahui".
        /// Jadi daftarnya ikut dikirim - pemanggil tahu bedanya tanpa harus
        /// membaca dokumen yang tidak ada di depannya saat memanggil tool.
        /// </summary>
        [Fact]
        public void jawaban_sketsa_menyiarkan_field_yang_belum_dibaca()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new[]
            {
                new SketchReading { Name = "Sketch1" },
            });

            var daftar = Daftar(hasil);

            Assert.Contains("dimensionCount", daftar);
            Assert.Contains("consumedByFeature", daftar);
            Assert.Contains("hasOpenContour", daftar);
        }

        [Fact]
        public void jawaban_fitur_menyiarkan_field_yang_belum_dibaca()
        {
            var hasil = ReadOnlyToolShaper.FeatureTree(new FeatureTreeReading
            {
                Features = new[]
                {
                    new FeatureReading { Name = "Boss-Extrude1", TypeName = "Extrusion" },
                },
            });

            Assert.Contains("parentSketch", Daftar(hasil));
        }

        /// <summary>
        /// Daftar ini harus MENGECIL saat field benar-benar diimplementasikan.
        ///
        /// Test ini sengaja memaku isinya: menambah field ke daftar berarti
        /// mengakui ada janji baru yang tidak ditepati, dan itu harus jadi
        /// keputusan sadar - bukan sesuatu yang menyelinap masuk.
        /// </summary>
        [Fact]
        public void daftarnya_persis_empat_field_yang_diketahui_hari_ini()
        {
            Assert.Equal(new[] { "parentSketch" }, FieldBelumDibaca.ListFitur);
            Assert.Equal(
                new[] { "dimensionCount", "consumedByFeature", "hasOpenContour" },
                FieldBelumDibaca.ListSketsa);
        }
    }
}
