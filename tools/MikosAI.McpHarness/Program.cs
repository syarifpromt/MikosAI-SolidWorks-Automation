using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MikosAI.Core.Capabilities;
using MikosAI.Core.Mcp;
using MikosAI.Core.Testing;
using MikosAI.Core.Tools;

namespace MikosAI.McpHarness
{
    /// <summary>
    /// Menyalakan server MCP produksi dengan executor tool palsu.
    ///
    /// YANG PALSU DI SINI HANYA EKSEKUTOR TOOL-NYA.
    ///
    /// Server HTTP, pemeriksaan bearer token, handler JSON-RPC, pembentuk daftar
    /// tool, dan gating capabilities.yaml semuanya kode produksi yang sama persis
    /// dengan yang berjalan di dalam SLDWORKS.exe. Jadi kalau protokolnya salah,
    /// ia salah di sini juga.
    ///
    /// Dua kegunaannya:
    ///
    ///   1. Membuat scripts\mcp-roundtrip.ps1 bisa diuji. Tanpa ini, satu-satunya
    ///      cara menguji script verifikasi adalah menjalankan hal yang sedang
    ///      diverifikasi - dan kalau keduanya salah dengan cara yang saling
    ///      menutupi, tidak ada yang tahu.
    ///
    ///   2. Membuat lapisan MCP dan agy bisa dikerjakan di mesin tanpa SolidWorks.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                return Jalankan(args);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Harness gagal: " + exception.Message);
                Console.Error.WriteLine(exception.StackTrace);
                return 1;
            }
        }

        private static int Jalankan(string[] args)
        {
            var berkasSesi = Argumen(args, "--session")
                ?? Path.Combine(Path.GetTempPath(), "mikosai-harness-session.json");

            var detik = int.TryParse(Argumen(args, "--seconds"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var d) ? d : 120;

            var akar = CariAkarRepo();

            Console.WriteLine("MikosAI - harness MCP (tanpa SolidWorks)");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("  Repo     : " + akar);

            // capabilities.yaml dan tools.json yang SEBENARNYA. Gating-nya tidak
            // dilonggarkan: daftar tool yang muncul di sini harus sama dengan yang
            // dilihat agy dari add-in sungguhan.
            var ledger = CapabilityLedger.Load(Path.Combine(akar, "capabilities.yaml"));
            var katalog = ToolCatalog.Load(Path.Combine(akar, "schemas", "tools.json"), ledger);
            var tools = McpToolListBuilder.Build(katalog, ledger);

            Console.WriteLine("  Tool     : " + tools.Count + " diekspos");
            foreach (var t in tools)
            {
                Console.WriteLine("             " + t.Name);
            }

            if (tools.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("  Nol tool diekspos. Itu sah menurut desain, tapi tidak ada yang bisa diuji.");
                return 1;
            }

            var fake = new FakeToolExecutor();
            foreach (var t in tools)
            {
                // Sukses tanpa data. Yang diuji harness ini transport dan
                // protokol, bukan isi pembacaan model.
                fake.Returns(t.Name);
            }

            var handler = new McpRequestHandler(tools, fake);
            var token = AgentWorkspace.NewSessionToken();

            using (var server = new McpHttpServer(handler, token))
            {
                server.Start();

                TulisSesi(berkasSesi, server.Endpoint.ToString(), token);

                Console.WriteLine();
                Console.WriteLine("  Endpoint : " + server.Endpoint);
                Console.WriteLine("  Sesi     : " + berkasSesi);
                Console.WriteLine("  PID      : " + System.Diagnostics.Process.GetCurrentProcess().Id);
                Console.WriteLine();
                Console.WriteLine("  Verifikasi dari terminal lain:");
                Console.WriteLine("    powershell -File scripts\\mcp-roundtrip.ps1 -SessionFile \"" + berkasSesi + "\"");
                Console.WriteLine();
                Console.WriteLine("  Hidup " + detik + " detik. Ctrl+C untuk berhenti lebih awal.");
                Console.Out.Flush();

                var berhenti = new ManualResetEventSlim(false);

                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    berhenti.Set();
                };

                berhenti.Wait(TimeSpan.FromSeconds(detik));
            }

            try
            {
                File.Delete(berkasSesi);
            }
            catch (IOException)
            {
                // Berkas sementara yang tertinggal tidak merugikan siapa pun.
            }

            Console.WriteLine();
            Console.WriteLine("  Server dimatikan, berkas sesi dihapus.");
            return 0;
        }

        private static void TulisSesi(string path, string url, string token)
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder!);
            }

            // Bentuknya harus sama dengan yang ditulis AgentWorkspace, karena
            // mcp-roundtrip.ps1 membaca keduanya dengan kode yang sama.
            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"schemaVersion\": \"1.0\",");
            json.AppendLine("  \"createdAt\": \"" + DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) + "\",");
            json.AppendLine("  \"serverUrl\": \"" + url + "\",");
            json.AppendLine("  \"token\": \"" + token + "\",");
            json.AppendLine("  \"pid\": " + System.Diagnostics.Process.GetCurrentProcess().Id);
            json.AppendLine("}");

            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        }

        private static string CariAkarRepo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "capabilities.yaml")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException(
                "Tidak bisa menemukan akar repo (yang memuat capabilities.yaml) dari " + AppContext.BaseDirectory);
        }

        private static string? Argumen(IReadOnlyList<string> args, string nama)
        {
            for (var i = 0; i < args.Count - 1; i++)
            {
                if (string.Equals(args[i], nama, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
