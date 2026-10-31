using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MikosAI.Core.Capabilities;
using MikosAI.Core.Execution;
using MikosAI.Core.Plan;
using MikosAI.Core.Testing;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    public class PlanRunnerTests
    {
        private static PlanValidator Validator()
        {
            // Ledger dengan semua tool v0.1 dibuat verified, supaya test fokus
            // ke perilaku runner - bukan ke gating (diuji di LedgerAndCatalogTests).
            var yaml = File.ReadAllText(RepoFiles.CapabilitiesYaml)
                           .Replace("status: not_implemented", "status: verified");

            return new PlanValidator(ToolCatalog.Load(RepoFiles.ToolsJson, CapabilityLedger.Parse(yaml)));
        }

        private static PlanValidator ValidatorWithRealLedger()
            => new PlanValidator(ToolCatalog.Load(
                RepoFiles.ToolsJson,
                CapabilityLedger.Load(RepoFiles.CapabilitiesYaml)));

        private static string Plan(
            string steps,
            string parameters = "",
            string questions = "",
            string acceptance = @"{ ""id"": ""a1"", ""check"": ""rebuild_clean"" }")
            => $@"{{
              ""schemaVersion"": ""1.0"",
              ""title"": ""Uji"",
              ""units"": {{ ""length"": ""mm"", ""angle"": ""deg"" }},
              ""document"": {{ ""type"": ""part"" }},
              ""parameters"": {{ {parameters} }},
              ""steps"": [ {steps} ],
              ""acceptance"": [ {acceptance} ],
              ""openQuestions"": [ {questions} ]
            }}";

        private const string TwoGoodSteps = @"
            { ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} },
            { ""id"": ""s2"", ""tool"": ""list_features"", ""arguments"": {} }";

        // ====================================================================
        // Jaminan terpenting: plan yang ditolak TIDAK menyentuh model
        // ====================================================================

        [Fact]
        public async Task plan_tidak_valid_ditolak_tanpa_memanggil_executor_sekali_pun()
        {
            var executor = new FakeToolExecutor();
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            // schemaVersion salah.
            var json = Plan(TwoGoodSteps).Replace(@"""schemaVersion"": ""1.0""", @"""schemaVersion"": ""9.9""");

            var result = await runner.RunAsync(json);

            Assert.False(result.Executed);
            Assert.Equal(PlanRejectionReason.Invalid, result.RejectionReason);
            Assert.Equal(0, executor.CallCount);
            Assert.Equal(Verdict.Blocked, result.Final.Verdict);
            Assert.False(result.IsSuccessful);
        }

        [Fact]
        public async Task tool_yang_belum_terverifikasi_menolak_run_tanpa_menyentuh_model()
        {
            var executor = new FakeToolExecutor();
            var runner = new PlanRunner(ValidatorWithRealLedger(), executor, new FakeAcceptanceEvaluator());

            var result = await runner.RunAsync(Plan(TwoGoodSteps));

            Assert.False(result.Executed);
            Assert.Equal(PlanRejectionReason.ToolsNotAvailable, result.RejectionReason);
            Assert.Equal(0, executor.CallCount);

            // Pesannya harus menjelaskan ini batas kemampuan, bukan salah tulis plan.
            Assert.Contains("belum terverifikasi", result.RejectionMessage);
        }

        [Fact]
        public async Task pertanyaan_blocking_menolak_run_tanpa_menyentuh_model()
        {
            var executor = new FakeToolExecutor();
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(
                TwoGoodSteps,
                questions: @"{ ""question"": ""Material apa?"", ""blocking"": true }");

            var result = await runner.RunAsync(json);

            Assert.False(result.Executed);
            Assert.Equal(PlanRejectionReason.BlockingQuestions, result.RejectionReason);
            Assert.Equal(0, executor.CallCount);
            Assert.Contains("Material apa?", result.RejectionMessage);
        }

        [Fact]
        public async Task pertanyaan_blocking_bisa_dilanjutkan_setelah_manusia_mengakui()
        {
            var executor = new FakeToolExecutor();
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(
                TwoGoodSteps,
                questions: @"{ ""question"": ""Material apa?"", ""blocking"": true }");

            var result = await runner.RunAsync(
                json,
                new PlanRunOptions { BlockingQuestionsAcknowledged = true });

            Assert.True(result.Executed);
            Assert.Equal(2, executor.CallCount);
        }

        [Fact]
        public async Task override_parameter_yang_tidak_ada_ditolak_tanpa_menyentuh_model()
        {
            var executor = new FakeToolExecutor();
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var result = await runner.RunAsync(
                Plan(TwoGoodSteps, parameters: @"""lebar"": { ""value"": 100 }"),
                new PlanRunOptions
                {
                    ParameterOverrides = new Dictionary<string, object?> { ["tidakAda"] = 5 },
                });

            Assert.False(result.Executed);
            Assert.Equal(PlanRejectionReason.ParameterResolution, result.RejectionReason);
            Assert.Equal(0, executor.CallCount);
        }

        // ====================================================================
        // Eksekusi normal
        // ====================================================================

        [Fact]
        public async Task step_dijalankan_berurutan()
        {
            var executor = new FakeToolExecutor();
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var result = await runner.RunAsync(Plan(TwoGoodSteps));

            Assert.True(result.Executed);
            Assert.Equal(new[] { "get_active_document", "list_features" },
                executor.Calls.Select(c => c.Tool).ToArray());
            Assert.Equal(Verdict.Pass, result.Final.Verdict);
            Assert.True(result.IsSuccessful);
        }

        [Fact]
        public async Task kriteria_penerimaan_dievaluasi_setelah_semua_step_selesai()
        {
            var evaluator = new FakeAcceptanceEvaluator();
            var runner = new PlanRunner(Validator(), new FakeToolExecutor(), evaluator);

            var result = await runner.RunAsync(Plan(TwoGoodSteps));

            Assert.Equal(1, evaluator.EvaluateCount);
            Assert.Single(result.AcceptanceChecks);
        }

        [Fact]
        public async Task acceptance_yang_gagal_menggagalkan_seluruh_run_walaupun_semua_step_lulus()
        {
            var evaluator = new FakeAcceptanceEvaluator().With("a1", Verdict.Fail);
            var runner = new PlanRunner(Validator(), new FakeToolExecutor(), evaluator);

            var result = await runner.RunAsync(Plan(TwoGoodSteps));

            Assert.True(result.Executed);
            Assert.All(result.Steps, s => Assert.Equal(Verdict.Pass, s.Verification.Verdict));
            Assert.Equal(Verdict.Fail, result.Final.Verdict);
            Assert.False(result.IsSuccessful);
        }

        [Fact]
        public async Task tanpa_evaluator_kriteria_penerimaan_menjadi_Blocked_bukan_diabaikan()
        {
            // Plan menuntut bukti yang tidak kita punya caranya memeriksa.
            // Itu BLOCKED, bukan PASS.
            var runner = new PlanRunner(Validator(), new FakeToolExecutor(), acceptanceEvaluator: null);

            var result = await runner.RunAsync(Plan(TwoGoodSteps));

            Assert.True(result.Executed);
            Assert.Equal(Verdict.Blocked, result.Final.Verdict);
            Assert.Contains(result.AcceptanceChecks, c => c.Verdict == Verdict.Blocked);
        }

        // ====================================================================
        // Kegagalan step
        // ====================================================================

        [Fact]
        public async Task step_yang_gagal_menghentikan_run_dan_step_berikutnya_tidak_dijalankan()
        {
            // Tidak ada opsi "continue" - melanjutkan di atas step yang gagal
            // menghasilkan model yang salah secara diam-diam.
            var executor = new FakeToolExecutor()
                .Fails("list_features", ErrorKind.Transient, "SolidWorks sibuk.");

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(@"
                { ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} },
                { ""id"": ""s2"", ""tool"": ""list_features"", ""arguments"": {} },
                { ""id"": ""s3"", ""tool"": ""get_bounding_box"", ""arguments"": {} }");

            var result = await runner.RunAsync(json);

            Assert.False(executor.WasCalled("get_bounding_box"));
            Assert.True(result.Steps.Single(s => s.StepId == "s3").Skipped);
            Assert.Equal(Verdict.Fail, result.Final.Verdict);
        }

        [Fact]
        public async Task acceptance_tidak_diperiksa_kalau_run_berhenti_di_tengah()
        {
            var evaluator = new FakeAcceptanceEvaluator();
            var executor = new FakeToolExecutor()
                .Fails("get_active_document", ErrorKind.Fatal, "rusak");

            var runner = new PlanRunner(Validator(), executor, evaluator);

            var result = await runner.RunAsync(Plan(TwoGoodSteps));

            // Memeriksa acceptance pada model setengah jadi hanya kebisingan.
            Assert.Equal(0, evaluator.EvaluateCount);
            Assert.Contains(result.AcceptanceChecks, c => c.Id == "acceptance_not_evaluated");
        }

        [Fact]
        public async Task step_yang_gagal_mendapat_usulan_pemulihan()
        {
            var executor = new FakeToolExecutor()
                .Fails("get_active_document", ErrorKind.Transient, "SolidWorks sibuk.");

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var result = await runner.RunAsync(Plan(TwoGoodSteps));
            var outcome = result.Steps.Single(s => s.StepId == "s1");

            Assert.NotNull(outcome.Recovery);
            Assert.Equal(ErrorKind.Transient, outcome.Recovery!.Kind);
            Assert.Equal(RecoveryAction.Retry, outcome.Recovery.Action);
        }

        [Fact]
        public async Task runner_mengusulkan_pemulihan_tapi_TIDAK_mengeksekusinya()
        {
            // Pemisahan ini yang mencegah AI mengulang-ulang operasi yang
            // merusak model. Usulan retry tidak boleh berarti runner mencoba lagi.
            var executor = new FakeToolExecutor()
                .Fails("get_active_document", ErrorKind.Transient, "sibuk");

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            await runner.RunAsync(Plan(TwoGoodSteps));

            Assert.Equal(1, executor.Calls.Count(c => c.Tool == "get_active_document"));
        }

        [Fact]
        public async Task executor_yang_melempar_exception_tidak_menjatuhkan_run()
        {
            // Kita in-process di dalam SolidWorks - exception yang lolos bisa
            // menjatuhkan seluruh aplikasi pengguna.
            var executor = new FakeToolExecutor()
                .Throws("get_active_document", new InvalidOperationException("sesuatu meledak"));

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var result = await runner.RunAsync(Plan(TwoGoodSteps));

            Assert.True(result.Executed);
            Assert.Equal(Verdict.Fail, result.Final.Verdict);
            Assert.NotNull(result.Steps[0].Recovery);
        }

        [Fact]
        public async Task sukses_tanpa_bukti_verifikasi_menjadi_Blocked()
        {
            var executor = new FakeToolExecutor().SucceedsWithoutEvidence("get_active_document");
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var result = await runner.RunAsync(
                Plan(@"{ ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} }"));

            Assert.Equal(Verdict.Blocked, result.Steps[0].Verification.Verdict);
            Assert.Contains(result.Steps[0].Verification.Checks, c => c.Id == "no_verification_evidence");
        }

        [Fact]
        public async Task tool_yang_sukses_tapi_verifikasinya_gagal_menggagalkan_run()
        {
            // extrude_cut yang tidak memotong apa pun.
            var executor = new FakeToolExecutor()
                .SucceedsButVerificationFails("extrude_cut", "volume_decreased", "Volume tidak berubah.");

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(@"{ ""id"": ""s1"", ""tool"": ""extrude_cut"", ""arguments"": {} }");
            var result = await runner.RunAsync(json);

            Assert.True(result.Steps[0].Result!.Ok);
            Assert.Equal(Verdict.Fail, result.Final.Verdict);
            Assert.False(result.IsSuccessful);
        }

        [Fact]
        public async Task review_manual_yang_menggantung_menahan_run_dari_status_berhasil()
        {
            var executor = new FakeToolExecutor().NeedsManualReview("get_active_document");
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var result = await runner.RunAsync(
                Plan(@"{ ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} }"));

            Assert.Equal(Verdict.Pass, result.Final.Verdict);
            Assert.True(result.Final.ManualReviewRequired);
            Assert.False(result.IsSuccessful);
        }

        // ====================================================================
        // Parameter
        // ====================================================================

        [Fact]
        public async Task referensi_parameter_diselesaikan_sebelum_executor_melihatnya()
        {
            var executor = new FakeToolExecutor();
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(
                @"{ ""id"": ""s1"", ""tool"": ""add_circle"", ""arguments"": { ""center"": { ""x"": 0, ""y"": 0 }, ""diameter"": ""$lubang"" } }",
                parameters: @"""lubang"": { ""value"": 12 }");

            await runner.RunAsync(json);

            var arguments = executor.ArgumentsFor("add_circle");

            // Executor harus menerima angka, bukan string "$lubang".
            Assert.Equal(12, arguments!["diameter"]!.GetValue<int>());
        }

        [Fact]
        public async Task override_parameter_mengubah_argumen_yang_diterima_executor()
        {
            // Inilah jalur Lapis 2: ubah satu angka, jalankan ulang, tanpa AI.
            var executor = new FakeToolExecutor();
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(
                @"{ ""id"": ""s1"", ""tool"": ""add_circle"", ""arguments"": { ""center"": { ""x"": 0, ""y"": 0 }, ""diameter"": ""$lubang"" } }",
                parameters: @"""lubang"": { ""value"": 12 }");

            await runner.RunAsync(
                json,
                new PlanRunOptions
                {
                    ParameterOverrides = new Dictionary<string, object?> { ["lubang"] = 20 },
                });

            Assert.Equal(20, executor.ArgumentsFor("add_circle")!["diameter"]!.GetValue<int>());
        }

        [Fact]
        public async Task referensi_parameter_di_dalam_array_bersarang_juga_diselesaikan()
        {
            var executor = new FakeToolExecutor();
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(
                @"{ ""id"": ""s1"", ""tool"": ""hole_wizard"", ""arguments"": {
                    ""positions"": [ { ""x"": ""$inset"", ""y"": ""$inset"" }, { ""x"": 0, ""y"": 0 } ]
                } }",
                parameters: @"""inset"": { ""value"": 15 }");

            await runner.RunAsync(json);

            var positions = executor.ArgumentsFor("hole_wizard")!["positions"]!.AsArray();

            Assert.Equal(15, positions[0]!["x"]!.GetValue<int>());
            Assert.Equal(15, positions[0]!["y"]!.GetValue<int>());
            Assert.Equal(0, positions[1]!["x"]!.GetValue<int>());
        }

        [Fact]
        public async Task plan_bisa_dijalankan_dua_kali_dengan_override_berbeda()
        {
            // Argumen asli tidak boleh ikut termodifikasi oleh run pertama.
            var runner1 = new FakeToolExecutor();
            var runner2 = new FakeToolExecutor();

            var json = Plan(
                @"{ ""id"": ""s1"", ""tool"": ""add_circle"", ""arguments"": { ""center"": { ""x"": 0, ""y"": 0 }, ""diameter"": ""$lubang"" } }",
                parameters: @"""lubang"": { ""value"": 12 }");

            await new PlanRunner(Validator(), runner1, new FakeAcceptanceEvaluator()).RunAsync(json);
            await new PlanRunner(Validator(), runner2, new FakeAcceptanceEvaluator()).RunAsync(
                json,
                new PlanRunOptions { ParameterOverrides = new Dictionary<string, object?> { ["lubang"] = 30 } });

            Assert.Equal(12, runner1.ArgumentsFor("add_circle")!["diameter"]!.GetValue<int>());
            Assert.Equal(30, runner2.ArgumentsFor("add_circle")!["diameter"]!.GetValue<int>());
        }

        // ====================================================================
        // Blok expect
        // ====================================================================

        [Fact]
        public async Task expect_yang_cocok_menghasilkan_check_yang_lulus()
        {
            var executor = new FakeToolExecutor()
                .Returns("extrude_boss", ("featureName", "Boss-Extrude1"));

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(@"{
                ""id"": ""s1"", ""tool"": ""extrude_boss"", ""arguments"": {},
                ""expect"": { ""featureName"": ""Boss-Extrude1"" }
            }");

            var result = await runner.RunAsync(json);

            Assert.Contains(result.Steps[0].Verification.Checks,
                c => c.Id == "expect_featureName" && c.Verdict == Verdict.Pass);
            Assert.Equal(Verdict.Pass, result.Final.Verdict);
        }

        [Fact]
        public async Task expect_yang_tidak_cocok_menggagalkan_step()
        {
            var executor = new FakeToolExecutor()
                .Returns("extrude_boss", ("featureName", "Boss-Extrude7"));

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(@"{
                ""id"": ""s1"", ""tool"": ""extrude_boss"", ""arguments"": {},
                ""expect"": { ""featureName"": ""Boss-Extrude1"" }
            }");

            var result = await runner.RunAsync(json);

            Assert.Equal(Verdict.Fail, result.Final.Verdict);
        }

        [Fact]
        public async Task expect_bertipe_boolean_dibandingkan_dengan_benar()
        {
            // Regresi: perbandingan lewat ToString() membuat ini gagal palsu,
            // karena true.ToString() di .NET menghasilkan "True" sementara JSON
            // menulis "true". Setiap harapan boolean jadi merah tanpa sebab.
            var executor = new FakeToolExecutor()
                .Returns("close_sketch", ("sketchFullyDefined", true));

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(@"{
                ""id"": ""s1"", ""tool"": ""close_sketch"", ""arguments"": {},
                ""expect"": { ""sketchFullyDefined"": true }
            }");

            var result = await runner.RunAsync(json);

            var check = result.Steps[0].Verification.Checks.Single(c => c.Id == "expect_sketchFullyDefined");

            Assert.Equal(Verdict.Pass, check.Verdict);
            Assert.Equal(Verdict.Pass, result.Final.Verdict);
        }

        [Fact]
        public async Task expect_boolean_yang_benar_benar_berbeda_tetap_gagal()
        {
            var executor = new FakeToolExecutor()
                .Returns("close_sketch", ("sketchFullyDefined", false));

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(@"{
                ""id"": ""s1"", ""tool"": ""close_sketch"", ""arguments"": {},
                ""expect"": { ""sketchFullyDefined"": true }
            }");

            var result = await runner.RunAsync(json);

            Assert.Equal(Verdict.Fail, result.Final.Verdict);
        }

        [Fact]
        public async Task expect_bertipe_angka_dibandingkan_secara_numerik()
        {
            // 4 dan 4.0 harus dianggap sama.
            var executor = new FakeToolExecutor()
                .Returns("linear_pattern", ("actualInstanceCount", 4.0));

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(@"{
                ""id"": ""s1"", ""tool"": ""linear_pattern"", ""arguments"": {},
                ""expect"": { ""faceCountDelta"": 4 }
            }").Replace(@"""faceCountDelta"": 4", @"""actualInstanceCount"": 4");

            var result = await runner.RunAsync(json);

            Assert.Equal(Verdict.Pass, result.Final.Verdict);
        }

        [Fact]
        public async Task expect_yang_tidak_bisa_diperiksa_hanya_menjadi_Warn_opsional()
        {
            // Nama fitur Hole Wizard berbeda antarversi dan antarbahasa UI -
            // harapan yang tidak bisa diperiksa tidak boleh menggagalkan run.
            var executor = new FakeToolExecutor().Returns("hole_wizard", ("holeCount", 4));
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(@"{
                ""id"": ""s1"", ""tool"": ""hole_wizard"", ""arguments"": {},
                ""expect"": { ""featureName"": ""CBORE for M6 Hex Head Bolt1"" }
            }");

            var result = await runner.RunAsync(json);

            var check = result.Steps[0].Verification.Checks.Single(c => c.Id == "expect_featureName");

            Assert.Equal(Verdict.Warn, check.Verdict);
            Assert.False(check.Required);
            Assert.Equal(Verdict.Warn, result.Final.Verdict);
        }

        // ====================================================================
        // stopAtStep
        // ====================================================================

        [Fact]
        public async Task stopAtStep_menghentikan_run_setelah_step_tersebut()
        {
            var executor = new FakeToolExecutor();
            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var json = Plan(@"
                { ""id"": ""s1"", ""tool"": ""get_active_document"", ""arguments"": {} },
                { ""id"": ""s2"", ""tool"": ""list_features"", ""arguments"": {} },
                { ""id"": ""s3"", ""tool"": ""get_bounding_box"", ""arguments"": {} }");

            await runner.RunAsync(json, new PlanRunOptions { StopAtStep = "s2" });

            Assert.True(executor.WasCalled("list_features"));
            Assert.False(executor.WasCalled("get_bounding_box"));
        }

        // ====================================================================
        // Contoh nyata
        // ====================================================================

        [Fact]
        public async Task contoh_mounting_plate_jalan_end_to_end_dengan_executor_palsu()
        {
            // Ini harness golden test versi tanpa SolidWorks. Begitu tool nyata
            // siap, test yang sama dijalankan dengan executor sungguhan.
            //
            // Field yang dikembalikan di bawah adalah KONTRAK untuk Jalur CAD:
            // setiap blok `expect` di plan hanya bisa diperiksa kalau tool
            // benar-benar mengembalikan field yang bersangkutan.
            var executor = new FakeToolExecutor()
                .Returns("create_sketch", ("sketchName", "SketchPlateProfile"), ("isOpen", true))
                .Returns("close_sketch", ("sketchName", "SketchPlateProfile"), ("sketchFullyDefined", true))
                .Returns("extrude_boss", ("featureName", "Boss-Extrude1"), ("volumeMm3After", 80000.0), ("volumeIncreases", true))
                .Returns("hole_wizard", ("featureName", "CBORE for M6 Hex Head Bolt1"), ("holeCount", 4))
                .Returns("fillet", ("filletedEdgeCount", 4));

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var result = await runner.RunAsync(File.ReadAllText(RepoFiles.MountingPlatePlan));

            Assert.True(result.Executed);
            Assert.Equal(6, result.Steps.Count);
            Assert.DoesNotContain(result.Steps, s => s.Skipped);
            Assert.Equal(5, result.AcceptanceChecks.Count);
            Assert.Equal(Verdict.Pass, result.Final.Verdict);
            Assert.True(result.IsSuccessful);
        }

        [Fact]
        public async Task contoh_mounting_plate_gagal_kalau_fillet_hanya_mengenai_3_tepi()
        {
            // Kegagalan nyata yang tidak dilaporkan API: fillet "sukses" tapi
            // hanya mengenai sebagian tepi.
            var executor = new FakeToolExecutor()
                .Returns("create_sketch", ("sketchName", "SketchPlateProfile"))
                .Returns("extrude_boss", ("featureName", "Boss-Extrude1"))
                .Returns("hole_wizard", ("holeCount", 4))
                .SucceedsButVerificationFails("fillet", "filleted_edge_count",
                    "Diminta 4 tepi, hanya 3 yang terfillet.");

            var runner = new PlanRunner(Validator(), executor, new FakeAcceptanceEvaluator());

            var result = await runner.RunAsync(File.ReadAllText(RepoFiles.MountingPlatePlan));

            Assert.Equal(Verdict.Fail, result.Final.Verdict);
            Assert.False(result.IsSuccessful);
        }
    }
}
