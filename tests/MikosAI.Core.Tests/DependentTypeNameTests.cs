using System.Collections.Generic;
using System.Linq;
using MikosAI.Core.Execution;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// `dependents` membawa {name, typeName}, bukan nama saja.
    ///
    /// KENAPA INI PUNYA TEST SENDIRI.
    ///
    /// Kontraknya sejak awal menyebut `array of {name, typeName}`, tapi
    /// implementasi pertama menyimpan `string[]` - typeName dibuang. Itu lolos
    /// karena tidak ada yang memeriksanya: field-nya terisi, bentuknya masuk
    /// akal, dan tidak ada satu pun test yang menanyakan tipe.
    ///
    /// Akibatnya tidak terlihat sampai dipakai. Daftar putih tipe konsumen
    /// diisi dari pengamatan nama tipe di model nyata, jadi tanpa typeName
    /// laporan fixture hanya akan berbunyi "ada child bernama Plane1" - tanpa
    /// cara mengetahui apa itu, dan tanpa cara mengisi daftarnya dengan bukti.
    ///
    /// Satu field yang hilang diam-diam membatalkan seluruh rencana sensus.
    /// </summary>
    public class DependentTypeNameTests
    {
        private static IReadOnlyDictionary<string, object?> SketsaPertama(ToolResult hasil)
            => ((IEnumerable<object?>)hasil.Data!["sketches"]!)
                .Cast<IReadOnlyDictionary<string, object?>>()
                .First();

        private static List<IReadOnlyDictionary<string, object?>> Dependents(ToolResult hasil)
            => ((IEnumerable<object?>)SketsaPertama(hasil)["dependents"]!)
                .Cast<IReadOnlyDictionary<string, object?>>()
                .ToList();

        private static ToolResult Hasil()
            => ReadOnlyToolShaper.Sketches(new[]
            {
                new SketchReading
                {
                    Name = "Sketch1",
                    ConsumedByFeatures = new[] { "Boss-Extrude1" },
                    Dependents = new[]
                    {
                        new DependentReading { Name = "Boss-Extrude1", TypeName = "Extrusion" },
                        new DependentReading { Name = "Plane1", TypeName = "RefPlane" },
                    },
                },
            });

        /// <summary>
        /// Inti dari berkas ini: tipe harus SAMPAI ke pemanggil, bukan hanya
        /// ada di dalam bacaan.
        /// </summary>
        [Fact]
        public void setiap_dependent_membawa_type_name()
        {
            var daftar = Dependents(Hasil());

            Assert.Equal(2, daftar.Count);
            Assert.Equal("Extrusion", daftar[0]["typeName"]);
            Assert.Equal("RefPlane", daftar[1]["typeName"]);
        }

        [Fact]
        public void dependent_tetap_membawa_namanya()
        {
            var daftar = Dependents(Hasil());

            Assert.Equal("Boss-Extrude1", daftar[0]["name"]);
            Assert.Equal("Plane1", daftar[1]["name"]);
        }

        /// <summary>
        /// Tipe yang tidak terbaca adalah null, BUKAN string kosong.
        ///
        /// String kosong terbaca sebagai "tipenya memang kosong", yang bukan
        /// keadaan yang bisa terjadi - setiap fitur punya tipe. null menyatakan
        /// "tidak terbaca", dan itu yang benar.
        /// </summary>
        [Fact]
        public void type_name_yang_tidak_terbaca_adalah_null()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new[]
            {
                new SketchReading
                {
                    Name = "Sketch1",
                    Dependents = new[] { new DependentReading { Name = "Entah" } },
                },
            });

            Assert.Null(Dependents(hasil)[0]["typeName"]);
        }

        /// <summary>
        /// Plane1 ADA di dependents tapi TIDAK di consumedByFeatures.
        ///
        /// Inilah pembedaan yang seluruh pekerjaan ini dibangun untuknya:
        /// "tidak dipakai fitur apa pun" adalah alasan yang masuk akal untuk
        /// MENGHAPUS sketsa, jadi delete_feature memakai dependents - yang
        /// memuat Plane1 - bukan consumedByFeatures.
        /// </summary>
        [Fact]
        public void plane_masuk_dependent_tapi_bukan_konsumen()
        {
            var hasil = Hasil();

            var konsumen = (IReadOnlyList<string>)SketsaPertama(hasil)["consumedByFeatures"]!;
            var dependents = Dependents(hasil).Select(d => (string)d["name"]!).ToList();

            Assert.Contains("Plane1", dependents);
            Assert.DoesNotContain("Plane1", konsumen);
        }

        // ------------------------------------------------------------------
        // Daftar putih
        // ------------------------------------------------------------------

        /// <summary>
        /// Daftar putih sengaja hanya memuat tipe yang TERAMATI. Test ini
        /// memakunya supaya penambahan dari ingatan jadi merah, bukan menyelinap.
        /// </summary>
        [Fact]
        public void daftar_putih_hanya_memuat_tipe_yang_teramati()
        {
            Assert.Equal(new[] { "Extrusion", "ICE" }, TipeKonsumenSketsa.Semua.Select(e => e.TypeName));
            Assert.True(TipeKonsumenSketsa.Dikenal("Extrusion"));
            Assert.True(TipeKonsumenSketsa.Dikenal("ICE"));
        }

        /// <summary>
        /// Tipe yang belum dikenal TIDAK dianggap konsumen. Arah salahnya
        /// disengaja: kurang melaporkan tidak berbahaya karena delete_feature
        /// memakai dependents, sementara lebih melaporkan berarti mengarang.
        /// </summary>
        [Theory]
        [InlineData("RefPlane")]
        [InlineData("ProfileFeature")]
        [InlineData("HoleWzd")]
        [InlineData("LPattern")]
        [InlineData(null)]
        [InlineData("")]
        public void tipe_yang_belum_dikenal_bukan_konsumen(string? typeName)
        {
            Assert.False(TipeKonsumenSketsa.Dikenal(typeName));
        }

        /// <summary>
        /// Setiap entri wajib menyebut fixture dan tempat ia diamati. Entri
        /// tanpa bukti adalah tebakan yang menyamar jadi data.
        /// </summary>
        [Fact]
        public void setiap_entri_daftar_putih_menyebut_buktinya()
        {
            Assert.All(TipeKonsumenSketsa.Semua, e =>
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Fixture));
                Assert.False(string.IsNullOrWhiteSpace(e.DiamatiPada));
            });
        }
    }
}
