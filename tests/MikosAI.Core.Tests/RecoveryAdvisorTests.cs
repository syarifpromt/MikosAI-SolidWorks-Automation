using System;
using System.Collections.Generic;
using MikosAI.Core.Execution;
using Xunit;

namespace MikosAI.Core.Tests
{
    public class RecoveryAdvisorTests
    {
        // --- Default aman: tidak yakin berarti JANGAN retry -----------------

        [Fact]
        public void Kegagalan_tidak_dikenal_menjadi_Fatal()
        {
            var result = new ToolResult
            {
                Ok = false,
                CapabilityId = "part_features",
                Error = null,
            };

            Assert.Equal(ErrorKind.Fatal, RecoveryAdvisor.Classify(result));
        }

        [Fact]
        public void Kegagalan_tidak_dikenal_TIDAK_PERNAH_mengusulkan_retry()
        {
            // Aturan paling penting di kelas ini. Retry pada kegagalan yang tidak
            // kita pahami bisa merusak model berulang kali.
            var decision = RecoveryAdvisor.Decide(ErrorKind.Fatal);

            Assert.NotEqual(RecoveryAction.Retry, decision.Action);
            Assert.Equal(RecoveryAction.Fail, decision.Action);
        }

        [Fact]
        public void Hanya_transient_yang_boleh_retry()
        {
            foreach (ErrorKind kind in Enum.GetValues(typeof(ErrorKind)))
            {
                var decision = RecoveryAdvisor.Decide(kind, attemptsUsed: 0);

                if (kind == ErrorKind.Transient)
                {
                    Assert.Equal(RecoveryAction.Retry, decision.Action);
                }
                else
                {
                    Assert.NotEqual(RecoveryAction.Retry, decision.Action);
                }
            }
        }

        // --- Batas retry ----------------------------------------------------

        [Fact]
        public void Transient_berhenti_retry_setelah_mencapai_batas()
        {
            Assert.Equal(RecoveryAction.Retry, RecoveryAdvisor.Decide(ErrorKind.Transient, attemptsUsed: 0).Action);
            Assert.Equal(RecoveryAction.Retry, RecoveryAdvisor.Decide(ErrorKind.Transient, attemptsUsed: 1).Action);
            Assert.Equal(RecoveryAction.Fail, RecoveryAdvisor.Decide(ErrorKind.Transient, attemptsUsed: 2).Action);
            Assert.Equal(RecoveryAction.Fail, RecoveryAdvisor.Decide(ErrorKind.Transient, attemptsUsed: 99).Action);
        }

        // --- Kebijakan tidak boleh dilewati ---------------------------------

        [Fact]
        public void PolicyBlocked_selalu_Block_dan_tidak_bisa_dilewati()
        {
            for (var attempts = 0; attempts < 10; attempts++)
            {
                var decision = RecoveryAdvisor.Decide(ErrorKind.PolicyBlocked, attempts);
                Assert.Equal(RecoveryAction.Block, decision.Action);
            }
        }

        // --- Verifikasi gagal bukan alasan retry ----------------------------

        [Fact]
        public void VerificationFailed_mengusulkan_replan_bukan_retry()
        {
            // Operasinya terlaksana; mengulangnya menghasilkan kesalahan yang sama.
            var decision = RecoveryAdvisor.Decide(ErrorKind.VerificationFailed);

            Assert.Equal(RecoveryAction.Replan, decision.Action);
        }

        [Fact]
        public void Verifikasi_Fail_pada_hasil_yang_Ok_diklasifikasi_VerificationFailed()
        {
            var result = new ToolResult
            {
                Ok = true,
                CapabilityId = "part_features",
                Verification = VerificationGate.Evaluate(new List<VerificationCheck>
                {
                    VerificationCheck.Fail("nonzero_volume", "Volume nol."),
                }),
            };

            Assert.Equal(ErrorKind.VerificationFailed, RecoveryAdvisor.Classify(result));
        }

        [Fact]
        public void Verifikasi_Blocked_pada_hasil_yang_Ok_diklasifikasi_PolicyBlocked()
        {
            var result = new ToolResult
            {
                Ok = true,
                CapabilityId = "hole_features",
                Verification = VerificationGate.Evaluate(new List<VerificationCheck>
                {
                    VerificationCheck.Blocked("hole_standard_missing", "Standar lubang tidak diberikan."),
                }),
            };

            var kind = RecoveryAdvisor.Classify(result);

            Assert.Equal(ErrorKind.PolicyBlocked, kind);
            Assert.Equal(RecoveryAction.Block, RecoveryAdvisor.Decide(kind).Action);
        }

        // --- Provider silent ------------------------------------------------

        [Fact]
        public void ProviderSilent_diblokir_dan_pesannya_menjelaskan_ini_kegagalan_integrasi()
        {
            var decision = RecoveryAdvisor.Decide(ErrorKind.ProviderSilent);

            Assert.Equal(RecoveryAction.Block, decision.Action);
            Assert.Contains("integrasi", decision.Reason);
            Assert.NotEqual(RecoveryAction.Retry, decision.Action);
        }

        // --- Kelas yang mengembalikan kendali ke agent ----------------------

        [Fact]
        public void CapabilityGap_mengembalikan_kendali_ke_agent()
        {
            var decision = RecoveryAdvisor.Decide(ErrorKind.CapabilityGap);

            Assert.Equal(RecoveryAction.Block, decision.Action);
            Assert.True(decision.AgentResolutionRequired);
        }

        [Fact]
        public void ToolUnavailable_meminta_agent_mencoba_komposisi_tool_lain()
        {
            // Tool tidak tersedia BUKAN sama dengan kemampuan tidak ada.
            var decision = RecoveryAdvisor.Decide(ErrorKind.ToolUnavailable);

            Assert.Equal(RecoveryAction.Replan, decision.Action);
            Assert.True(decision.AgentResolutionRequired);
        }

        // --- Klasifikasi dari exception -------------------------------------

        [Theory]
        [InlineData(typeof(ArgumentException), ErrorKind.InvalidArgument)]
        [InlineData(typeof(ArgumentNullException), ErrorKind.InvalidArgument)]
        [InlineData(typeof(TimeoutException), ErrorKind.Transient)]
        [InlineData(typeof(System.IO.FileNotFoundException), ErrorKind.EnvironmentMissing)]
        [InlineData(typeof(UnauthorizedAccessException), ErrorKind.UserActionRequired)]
        public void Exception_diklasifikasi_sesuai_tipenya(Type exceptionType, ErrorKind expected)
        {
            var exception = (Exception)Activator.CreateInstance(exceptionType)!;

            Assert.Equal(expected, RecoveryAdvisor.Classify(null, exception));
        }

        [Fact]
        public void InvalidCastException_diklasifikasi_sebagai_masalah_lingkungan()
        {
            // Jebakan yang sudah terdokumentasi: handler PropertyManagerPage
            // tanpa IDispatch membuat ICreatePropertyManagerPage melempar ini.
            // Itu masalah deployment, bukan bug argumen.
            Assert.Equal(
                ErrorKind.EnvironmentMissing,
                RecoveryAdvisor.Classify(null, new InvalidCastException()));
        }

        [Fact]
        public void Exception_yang_tidak_dikenal_menjadi_Fatal()
        {
            Assert.Equal(ErrorKind.Fatal, RecoveryAdvisor.Classify(null, new Exception("sesuatu yang aneh")));
        }

        // --- Klasifikasi dari pesan -----------------------------------------

        [Theory]
        [InlineData("SolidWorks license is not available", ErrorKind.UserActionRequired)]
        [InlineData("SolidWorks is not installed", ErrorKind.EnvironmentMissing)]
        [InlineData("Operation timed out", ErrorKind.Transient)]
        [InlineData("This feature is not implemented yet", ErrorKind.CapabilityGap)]
        public void Pesan_error_dipakai_sebagai_petunjuk_terakhir(string message, ErrorKind expected)
        {
            var result = new ToolResult
            {
                Ok = false,
                CapabilityId = "part_features",
                Error = new ToolError { Kind = ErrorKind.Fatal, Message = message },
            };

            // Kind yang sudah eksplisit menang - jadi uji lewat jalur exception.
            Assert.Equal(expected, RecoveryAdvisor.Classify(null, new Exception(message)));

            // Dan pastikan Kind eksplisit memang diprioritaskan.
            Assert.Equal(ErrorKind.Fatal, RecoveryAdvisor.Classify(result));
        }

        // --- Prioritas ------------------------------------------------------

        [Fact]
        public void ErrorKind_yang_eksplisit_mengalahkan_segalanya()
        {
            var result = new ToolResult
            {
                Ok = false,
                CapabilityId = "part_features",
                Error = new ToolError { Kind = ErrorKind.Transient, Message = "not installed" },
            };

            // Pesannya mengandung "not installed" yang biasanya berarti
            // EnvironmentMissing, tapi Kind eksplisit harus menang.
            Assert.Equal(ErrorKind.Transient, RecoveryAdvisor.Classify(result, new ArgumentException()));
        }
    }
}
