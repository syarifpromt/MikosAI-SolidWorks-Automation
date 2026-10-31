using System;
using System.Collections.Generic;
using System.IO;
using MikosAI.Core.Tracing;
using Xunit;

namespace MikosAI.Core.Tests
{
    public class TraceWriterTests : IDisposable
    {
        private readonly string _directory;

        public TraceWriterTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "mikosai-trace-test-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
            }
            catch (Exception)
            {
                // Pembersihan yang gagal tidak boleh menggagalkan test.
            }
        }

        // --- Redaksi --------------------------------------------------------

        [Theory]
        [InlineData("prompt")]
        [InlineData("systemPrompt")]
        [InlineData("api_key")]
        [InlineData("apiKey")]
        [InlineData("ANTIGRAVITY_API_KEY")]
        [InlineData("bearerToken")]
        [InlineData("secret")]
        [InlineData("password")]
        [InlineData("Authorization")]
        [InlineData("cookie")]
        public void kunci_sensitif_diredaksi(string key)
        {
            Assert.Equal("[redacted]", TraceWriter.Redact(key, "rahasia-sungguhan"));
        }

        [Theory]
        [InlineData("featureName")]
        [InlineData("toolName")]
        [InlineData("durationMs")]
        [InlineData("verdict")]
        public void kunci_biasa_tidak_diredaksi(string key)
        {
            Assert.Equal("nilai", TraceWriter.Redact(key, "nilai"));
        }

        [Fact]
        public void redaksi_berlaku_juga_di_objek_bersarang()
        {
            var nested = new Dictionary<string, object?>
            {
                ["provider"] = "antigravity",
                ["api_key"] = "jangan-sampai-tertulis",
            };

            var redacted = Assert.IsType<Dictionary<string, object?>>(
                TraceWriter.Redact("providerConfig", nested));

            Assert.Equal("antigravity", redacted["provider"]);
            Assert.Equal("[redacted]", redacted["api_key"]);
        }

        [Fact]
        public void nilai_yang_sangat_panjang_dipotong()
        {
            var redacted = Assert.IsType<string>(
                TraceWriter.Redact("summary", new string('x', 5000)));

            Assert.True(redacted.Length < 300);
            Assert.Contains("5000 karakter", redacted);
        }

        // --- Penulisan ------------------------------------------------------

        [Fact]
        public void event_ditulis_sebagai_satu_baris_JSON_per_event()
        {
            using (var writer = new TraceWriter(_directory, "run-1"))
            {
                writer.Write("run.started", new Dictionary<string, object?> { ["planTitle"] = "Uji" });
                writer.Write("tool.called", new Dictionary<string, object?> { ["tool"] = "get_active_document" });
                writer.Write("run.completed", new Dictionary<string, object?> { ["verdict"] = "PASS" });
            }

            var lines = File.ReadAllLines(Path.Combine(_directory, "run-1.jsonl"));

            Assert.Equal(3, lines.Length);
            Assert.Contains("run.started", lines[0]);
            Assert.Contains("get_active_document", lines[1]);
            Assert.Contains("PASS", lines[2]);
        }

        [Fact]
        public void setiap_baris_memuat_timestamp_dan_runId()
        {
            using (var writer = new TraceWriter(_directory, "run-2"))
            {
                writer.Write("run.started");
            }

            var line = File.ReadAllText(Path.Combine(_directory, "run-2.jsonl"));

            Assert.Contains("\"ts\"", line);
            Assert.Contains("\"runId\":\"run-2\"", line);
        }

        [Fact]
        public void api_key_tidak_pernah_sampai_ke_disk()
        {
            using (var writer = new TraceWriter(_directory, "run-3"))
            {
                writer.Write("provider.spawned", new Dictionary<string, object?>
                {
                    ["providerId"] = "antigravity",
                    ["api_key"] = "AIza-RAHASIA-BANGET",
                });
            }

            var content = File.ReadAllText(Path.Combine(_directory, "run-3.jsonl"));

            Assert.DoesNotContain("AIza-RAHASIA-BANGET", content);
            Assert.Contains("[redacted]", content);
            Assert.Contains("antigravity", content);
        }

        // --- Observabilitas adalah jalur samping ----------------------------

        [Fact]
        public void direktori_yang_tidak_bisa_dibuat_tidak_melempar_exception()
        {
            // Kalau trace tidak bisa ditulis, pekerjaan tetap harus jalan.
            var invalid = Path.Combine("Z:", "tidak", "ada", "drive", "ini");

            using var writer = new TraceWriter(invalid, "run-4");

            writer.Write("run.started");
            writer.Write("run.completed");

            Assert.True(writer.IsDisabled);
        }

        [Fact]
        public void Dispose_dua_kali_aman()
        {
            var writer = new TraceWriter(_directory, "run-5");
            writer.Write("run.started");

            writer.Dispose();
            writer.Dispose();
        }
    }
}
