using System.Collections.Generic;
using MikosAI.Core.Execution;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Test untuk mekanisme keamanan inti. Kalau salah satu dari test ini merah,
    /// jangan ditambal dengan melonggarkan assertion - cari penyebabnya.
    /// </summary>
    public class VerificationGateTests
    {
        // --- Normalisasi status ---------------------------------------------

        [Theory]
        [InlineData("pass", Verdict.Pass)]
        [InlineData("PASS", Verdict.Pass)]
        [InlineData("passed", Verdict.Pass)]
        [InlineData("ok", Verdict.Pass)]
        [InlineData("warn", Verdict.Warn)]
        [InlineData("warning", Verdict.Warn)]
        [InlineData("review_required", Verdict.Warn)]
        [InlineData("fail", Verdict.Fail)]
        [InlineData("failed", Verdict.Fail)]
        [InlineData("blocked", Verdict.Blocked)]
        public void Parse_mengenali_status_yang_dikenal(string input, Verdict expected)
        {
            Assert.Equal(expected, VerdictParser.Parse(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("sukses")]
        [InlineData("partially-ok")]
        [InlineData("unknown")]
        [InlineData("maybe")]
        public void Status_yang_tidak_dikenal_menjadi_Blocked_bukan_Pass(string? input)
        {
            // Inilah aturannya: kalau kita tidak mengerti statusnya, kita tidak
            // tahu apakah tugasnya berhasil - dan "tidak tahu" bukan "berhasil".
            Assert.Equal(Verdict.Blocked, VerdictParser.Parse(input));
        }

        // --- Tidak ada bukti bukan berarti lulus ----------------------------

        [Fact]
        public void Operasi_tulis_tanpa_pemeriksaan_apa_pun_menjadi_Blocked()
        {
            var result = VerificationGate.Evaluate(
                checks: new List<VerificationCheck>(),
                manualReviewRequired: false,
                requiresEvidence: true);

            Assert.Equal(Verdict.Blocked, result.Verdict);
            Assert.False(result.MayBeMarkedComplete);
        }

        [Fact]
        public void Operasi_read_only_tanpa_pemeriksaan_boleh_Pass()
        {
            var result = VerificationGate.Evaluate(
                checks: new List<VerificationCheck>(),
                manualReviewRequired: false,
                requiresEvidence: false);

            Assert.Equal(Verdict.Pass, result.Verdict);
            Assert.True(result.MayBeMarkedComplete);
        }

        [Fact]
        public void Checks_null_diperlakukan_sama_dengan_kosong()
        {
            var result = VerificationGate.Evaluate(null, requiresEvidence: true);
            Assert.Equal(Verdict.Blocked, result.Verdict);
        }

        // --- Agregasi -------------------------------------------------------

        [Fact]
        public void Yang_terparah_menang()
        {
            var result = VerificationGate.Evaluate(new List<VerificationCheck>
            {
                VerificationCheck.Pass("feature_exists_in_tree"),
                new VerificationCheck { Id = "rebuild_succeeds", Verdict = Verdict.Warn },
                VerificationCheck.Fail("nonzero_volume", "Volume nol."),
            });

            Assert.Equal(Verdict.Fail, result.Verdict);
        }

        [Fact]
        public void Blocked_mengalahkan_Fail()
        {
            var result = VerificationGate.Evaluate(new List<VerificationCheck>
            {
                VerificationCheck.Fail("nonzero_volume", "Volume nol."),
                VerificationCheck.Blocked("save_reopen_dimension_readback", "Tidak bisa membuka ulang dokumen."),
            });

            Assert.Equal(Verdict.Blocked, result.Verdict);
        }

        [Fact]
        public void Fail_pada_check_required_tidak_bisa_ditutupi_oleh_Pass_lain()
        {
            // Sembilan pemeriksaan lulus tidak membuat satu kegagalan penting hilang.
            var checks = new List<VerificationCheck>();
            for (var i = 0; i < 9; i++)
            {
                checks.Add(VerificationCheck.Pass($"check_{i}"));
            }
            checks.Add(VerificationCheck.Fail("save_reopen_dimension_readback", "Dimensi tidak sesuai setelah dibuka ulang."));

            var result = VerificationGate.Evaluate(checks);

            Assert.Equal(Verdict.Fail, result.Verdict);
            Assert.False(result.MayBeMarkedComplete);
        }

        [Fact]
        public void Check_optional_dibatasi_maksimum_Warn()
        {
            // Pemeriksaan tambahan yang gagal tidak boleh memblokir hasil yang
            // sebenarnya benar.
            var result = VerificationGate.Evaluate(new List<VerificationCheck>
            {
                VerificationCheck.Pass("feature_exists_in_tree"),
                VerificationCheck.Fail("cosmetic_naming", "Nama fitur tidak sesuai konvensi.", required: false),
            });

            Assert.Equal(Verdict.Warn, result.Verdict);
        }

        [Fact]
        public void Check_optional_yang_Blocked_juga_dibatasi_Warn()
        {
            var result = VerificationGate.Evaluate(new List<VerificationCheck>
            {
                VerificationCheck.Pass("feature_exists_in_tree"),
                VerificationCheck.Blocked("optional_probe", "Tidak bisa diperiksa.", required: false),
            });

            Assert.Equal(Verdict.Warn, result.Verdict);
        }

        // --- Review manual --------------------------------------------------

        [Fact]
        public void Pass_dengan_review_manual_belum_boleh_dianggap_selesai()
        {
            var result = VerificationGate.Evaluate(
                new List<VerificationCheck> { VerificationCheck.Pass("feature_exists_in_tree") },
                manualReviewRequired: true);

            Assert.Equal(Verdict.Pass, result.Verdict);
            Assert.True(result.ManualReviewRequired);
            Assert.False(result.MayBeMarkedComplete);
        }

        // --- Penggabungan antar step ----------------------------------------

        [Fact]
        public void Combine_satu_step_Fail_menggagalkan_seluruh_run()
        {
            var combined = VerificationGate.Combine(new List<VerificationResult>
            {
                VerificationGate.Evaluate(new List<VerificationCheck> { VerificationCheck.Pass("a") }),
                VerificationGate.Evaluate(new List<VerificationCheck> { VerificationCheck.Pass("b") }),
                VerificationGate.Evaluate(new List<VerificationCheck> { VerificationCheck.Fail("c", "gagal") }),
            });

            Assert.Equal(Verdict.Fail, combined.Verdict);
            Assert.Equal(3, combined.Checks.Count);
        }

        [Fact]
        public void Combine_mewarisi_kewajiban_review_dari_step_mana_pun()
        {
            var combined = VerificationGate.Combine(new List<VerificationResult>
            {
                VerificationGate.Evaluate(new List<VerificationCheck> { VerificationCheck.Pass("a") }),
                VerificationGate.Evaluate(new List<VerificationCheck> { VerificationCheck.Pass("b") }, manualReviewRequired: true),
            });

            Assert.Equal(Verdict.Pass, combined.Verdict);
            Assert.True(combined.ManualReviewRequired);
            Assert.False(combined.MayBeMarkedComplete);
        }

        [Fact]
        public void Combine_tanpa_hasil_apa_pun_menjadi_Blocked()
        {
            Assert.Equal(Verdict.Blocked, VerificationGate.Combine(new List<VerificationResult>()).Verdict);
        }

        // --- ToolResult -----------------------------------------------------

        [Fact]
        public void ToolResult_Ok_tanpa_verifikasi_bukan_berarti_berhasil()
        {
            // Ini perbedaan antara "panggilan terlaksana" dan "tugas berhasil",
            // dan ini inti dari seluruh proyek.
            var result = new ToolResult { Ok = true, CapabilityId = "part_features" };

            Assert.True(result.Ok);
            Assert.False(result.IsTaskSuccessful);
        }

        [Fact]
        public void ToolResult_extrude_cut_yang_tidak_memotong_apa_pun_adalah_kegagalan()
        {
            // Skenario nyata: API mengembalikan sukses, tapi volume tidak berubah.
            var result = new ToolResult
            {
                Ok = true,
                CapabilityId = "part_features",
                Verification = VerificationGate.Evaluate(new List<VerificationCheck>
                {
                    VerificationCheck.Pass("feature_exists_in_tree"),
                    VerificationCheck.Fail("volume_decreased", "Volume tidak berubah - cut tidak memotong apa pun.",
                        observed: 1000.0, expected: "< 1000"),
                }),
            };

            Assert.True(result.Ok);
            Assert.Equal(Verdict.Fail, result.Verification!.Verdict);
            Assert.False(result.IsTaskSuccessful);
        }
    }
}
