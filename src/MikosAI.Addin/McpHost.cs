using System;
using System.Collections.Generic;
using System.IO;
using MikosAI.Core.Capabilities;
using MikosAI.Core.Execution;
using MikosAI.Core.Mcp;
using MikosAI.Core.Testing;
using MikosAI.Core.Tools;

namespace MikosAI.Addin
{
    /// <summary>
    /// Menyalakan server MCP di dalam proses SolidWorks, dan menyiapkan
    /// workspace untuk AI CLI.
    ///
    /// Inilah inti topologi proyek ini: server MCP hidup DI DALAM
    /// <c>SLDWORKS.exe</c>, jadi tool handler memanggil executor lewat
    /// pemanggilan fungsi biasa - bukan lewat protokol tambahan. Lihat
    /// docs/adr/0002.
    ///
    /// EXECUTOR MASIH PALSU, DAN ITU DISENGAJA.
    ///
    /// Tool CAD nyata adalah pekerjaan Fase 1. Yang dibuktikan tahap ini adalah
    /// seluruh jalurnya tersambung: SolidWorks memuat add-in, add-in menyalakan
    /// server MCP, workspace tertulis, dan `agy` bisa memanggil tool. Mengisi
    /// pipanya lebih mudah daripada membuktikan pipanya ada.
    ///
    /// Daftar tool yang diekspos saat ini KOSONG, karena belum ada kemampuan
    /// berstatus verified atau pilot di capabilities.yaml. Itu benar, bukan bug.
    /// </summary>
    internal sealed class McpHost : IDisposable
    {
        private readonly HostDiagnostics _diagnostics;

        private AgentWorkspace? _workspace;
        private McpHttpServer? _server;
        private bool _disposed;

        internal McpHost(HostDiagnostics diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        internal Uri? Endpoint { get; private set; }

        internal string? WorkspaceDirectory { get; private set; }

        internal string? McpConfigPath { get; private set; }

        internal int ToolCount { get; private set; }

        internal bool IsRunning => _server?.IsRunning == true;

        /// <summary>
        /// Menyalakan server. Tidak pernah melempar exception - kegagalan di sini
        /// tidak boleh menggagalkan pemuatan add-in, karena add-in tetap berguna
        /// walaupun integrasi AI belum jalan.
        /// </summary>
        internal bool Mulai()
        {
            try
            {
                // Ledger dan katalog dibaca dari repo. Di instalasi nanti keduanya
                // ikut dikemas; untuk sekarang dicari relatif ke assembly.
                var akar = CariAkarRepo();

                if (akar is null)
                {
                    _diagnostics.AddError(
                        "capabilities.yaml dan schemas/tools.json tidak ditemukan - server MCP tidak dinyalakan. " +
                        "Ini wajar kalau add-in dijalankan dari lokasi yang bukan hasil build repo.");
                    return false;
                }

                var ledger = CapabilityLedger.Load(Path.Combine(akar, "capabilities.yaml"));
                var katalog = ToolCatalog.Load(Path.Combine(akar, "schemas", "tools.json"), ledger);

                // Periksa konsistensi saat startup. Lebih baik gagal keras di sini
                // daripada menemukan tool menggantung di tengah eksekusi.
                var masalah = katalog.Validate();
                foreach (var item in masalah)
                {
                    _diagnostics.AddError("ledger/katalog: " + item);
                }

                var tools = McpToolListBuilder.Build(katalog, ledger);
                ToolCount = tools.Count;

                // Executor palsu. Diganti executor nyata di Fase 1.
                var executor = new FakeToolExecutor();
                var handler = new McpRequestHandler(tools, executor);

                var token = AgentWorkspace.NewSessionToken();

                _server = new McpHttpServer(handler, token);
                _server.Start();

                Endpoint = _server.Endpoint;

                _workspace = new AgentWorkspace();
                _workspace.Prepare(_server.Endpoint, token);

                WorkspaceDirectory = _workspace.WorkingDirectory;
                McpConfigPath = _workspace.McpConfigPath;

                return true;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menyalakan server MCP", exception);
                Hentikan();
                return false;
            }
        }

        /// <summary>Ringkasan untuk panel UI.</summary>
        internal IReadOnlyDictionary<string, object?> Ringkasan() => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["running"] = IsRunning,
            ["endpoint"] = Endpoint?.ToString(),
            ["toolCount"] = ToolCount,
            ["workspace"] = WorkspaceDirectory,
            ["mcpConfig"] = McpConfigPath,
        };

        private void Hentikan()
        {
            try
            {
                _server?.Dispose();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menghentikan server MCP", exception);
            }
            finally
            {
                _server = null;
            }
        }

        /// <summary>
        /// Mencari akar repo dari lokasi assembly, dengan menelusuri ke atas
        /// sampai menemukan capabilities.yaml.
        ///
        /// Sementara. Instalasi nanti mengemas berkas-berkas ini di samping
        /// assembly, dan fungsi ini menyusut jadi satu pemeriksaan.
        /// </summary>
        private static string? CariAkarRepo()
        {
            try
            {
                var direktori = Path.GetDirectoryName(typeof(McpHost).Assembly.Location);

                for (var naik = 0; naik < 8 && !string.IsNullOrEmpty(direktori); naik++)
                {
                    if (File.Exists(Path.Combine(direktori!, "capabilities.yaml"))
                        && File.Exists(Path.Combine(direktori!, "schemas", "tools.json")))
                    {
                        return direktori;
                    }

                    direktori = Path.GetDirectoryName(direktori);
                }
            }
            catch (Exception)
            {
                // Jatuh ke null.
            }

            return null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            Hentikan();

            // Workspace TIDAK dihapus saat disconnect. Pengguna mungkin masih
            // ingin melihat mcp_config.json terakhir, dan folder itu ditulis ulang
            // setiap sesi. Penghapusan adalah urusan uninstall.
            _workspace = null;
        }
    }
}
