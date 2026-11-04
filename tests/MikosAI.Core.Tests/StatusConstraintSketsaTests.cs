using System;
using System.Reflection;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Pemetaan swConstrainedStatus_e. Pernah bergeser satu, dan itu mahal.
    ///
    /// NILAI SEBENARNYA, direfleksikan dari swconst asli:
    ///
    ///   swUnknownConstraint = 1
    ///   swUnderConstrained  = 2
    ///   swFullyConstrained  = 3
    ///   swOverConstrained   = 4
    ///   swNoSolution        = 5
    ///   swInvalidSolution   = 6
    ///   swAutosolveOff      = 7
    ///
    /// Versi sebelumnya memetakan 1 ke UnderDefined, 2 ke FullyDefined, dan 3 ke
    /// OverDefined - bergeser satu di seluruh rentang. Akibatnya dua arah:
    ///
    ///   fully defined -> dilaporkan OverDefined  : FAIL palsu, merepotkan
    ///   under defined -> dilaporkan FullyDefined : RUSAK TERLIHAT SEHAT
    ///
    /// Yang kedua itu alasan test ini ada. Sketsa yang belum terdefinisi akan
    /// lolos sebagai sehat, dan tool tulis Fase 3 akan membangun di atasnya -
    /// menghasilkan geometri yang bergeser tanpa satu pun peringatan.
    ///
    /// Ditemukan oleh golden test pada plat 100x80x10 yang jelas-jelas fully
    /// defined. Itulah gunanya acuan yang jawabannya sudah diketahui lebih
    /// dulu: ia bisa membantah pembacaan, bukan sekadar mengulanginya.
    /// </summary>
    public class StatusConstraintSketsaTests
    {
        private static SketchConstraintStatus Petakan(int kode)
        {
            var tipe = AddinAssembly.Tipe("MikosAI.Addin.SolidWorksReader");

            var metode = tipe.GetMethod(
                "StatusConstraint",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "SolidWorksReader.StatusConstraint tidak ada lagi - test ini sudah tidak menguji apa pun.");

            return (SketchConstraintStatus)metode.Invoke(null, new object[] { kode })!;
        }

        /// <summary>
        /// Inti dari seluruh berkas ini: 3 adalah swFullyConstrained.
        /// </summary>
        [Fact]
        public void tiga_adalah_fully_defined_bukan_over_defined()
        {
            Assert.Equal(SketchConstraintStatus.FullyDefined, Petakan(3));
        }

        /// <summary>
        /// Arah yang berbahaya: 2 adalah under-defined dan TIDAK BOLEH
        /// dilaporkan sebagai fully defined.
        /// </summary>
        [Fact]
        public void dua_adalah_under_defined_bukan_fully_defined()
        {
            Assert.Equal(SketchConstraintStatus.UnderDefined, Petakan(2));
            Assert.NotEqual(SketchConstraintStatus.FullyDefined, Petakan(2));
        }

        [Theory]
        [InlineData(1, SketchConstraintStatus.Unknown)]          // swUnknownConstraint
        [InlineData(2, SketchConstraintStatus.UnderDefined)]     // swUnderConstrained
        [InlineData(3, SketchConstraintStatus.FullyDefined)]     // swFullyConstrained
        [InlineData(4, SketchConstraintStatus.OverDefined)]      // swOverConstrained
        [InlineData(5, SketchConstraintStatus.NoSolution)]       // swNoSolution
        [InlineData(6, SketchConstraintStatus.InvalidSolution)]  // swInvalidSolution
        public void seluruh_rentang_dipetakan_sesuai_swconst(int kode, SketchConstraintStatus diharapkan)
        {
            Assert.Equal(diharapkan, Petakan(kode));
        }

        /// <summary>
        /// swAutosolveOff: SolidWorks sedang tidak menyelesaikan constraint,
        /// jadi status apa pun tidak berarti apa-apa.
        ///
        /// Unknown, bukan FullyDefined - dan Core memperlakukan Unknown sebagai
        /// BLOCKED. Menebak "sehat" saat solver dimatikan adalah mengarang
        /// jaminan.
        /// </summary>
        [Fact]
        public void autosolve_mati_tidak_dianggap_sehat()
        {
            Assert.Equal(SketchConstraintStatus.Unknown, Petakan(7));
        }

        /// <summary>
        /// Nol bukan anggota enum ini sama sekali. Versi sebelumnya memetakannya
        /// ke `Empty` - nilai yang kami karang sendiri dan tidak pernah
        /// dihasilkan SolidWorks.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(8)]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        public void kode_di_luar_enum_menjadi_unknown(int kode)
        {
            Assert.Equal(SketchConstraintStatus.Unknown, Petakan(kode));
        }
    }
}
