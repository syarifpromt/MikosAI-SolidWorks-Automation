using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikosAI.Core.Capabilities;
using MikosAI.Core.Execution;
using MikosAI.Core.Mcp;
using MikosAI.Core.Testing;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    public class McpToolListTests
    {
        private static (ToolCatalog catalog, CapabilityLedger ledger) WithAllVerified()
        {
            var yaml = File.ReadAllText(RepoFiles.CapabilitiesYaml)
                           .Replace("status: not_implemented", "status: verified");

            var ledger = CapabilityLedger.Parse(yaml);
            return (ToolCatalog.Load(RepoFiles.ToolsJson, ledger), ledger);
        }

        [Fact]
        public void dengan_ledger_sebenarnya_hanya_tool_read_only_yang_diekspos()
        {
            // Keadaan jujur hari ini: document_inspection berstatus pilot, jadi
            // 6 tool read-only terbuka. Seluruh tool yang MENULIS masih
            // not_implemented dan tidak boleh muncul - itu invarian yang
            // sebenarnya dijaga, bukan "daftarnya kosong".
            var ledger = CapabilityLedger.Load(RepoFiles.CapabilitiesYaml);
            var catalog = ToolCatalog.Load(RepoFiles.ToolsJson, ledger);
            var tools = McpToolListBuilder.Build(catalog, ledger);

            Assert.Equal(6, tools.Count);
            Assert.All(tools, t => Assert.False(
                catalog.Find(t.Name)!.Mutates,
                $"Tool '{t.Name}' mengubah model tapi sudah diekspos."));
        }

        [Fact]
        public void tool_yang_sudah_verified_tidak_lagi_ditandai_pilot()
        {
            // Dulu test ini menegaskan sebaliknya: get_bounding_box HARUS
            // bertanda [PILOT], karena document_inspection memang pilot.
            //
            // Sejak 2026-11-05 kemampuan itu `verified` - golden test baca
            // lulus 33 pemeriksaan empat kali berturut-turut di SW2025 SP01.1.
            // Jadi tandanya harus HILANG, dan itu yang diuji sekarang.
            //
            // Mekanisme penandaannya sendiri tidak kehilangan penjaga: ia
            // diuji di kemampuan_pilot_ditandai_di_deskripsi() dengan ledger
            // sintetis, yang tidak ikut berubah saat status kemampuan nyata
            // naik. Test yang menguji mekanisme TIDAK boleh bergantung pada
            // keadaan ledger hari ini - kalau bergantung, ia lenyap diam-diam
            // begitu keadaannya berubah.
            var ledger = CapabilityLedger.Load(RepoFiles.CapabilitiesYaml);
            var catalog = ToolCatalog.Load(RepoFiles.ToolsJson, ledger);
            var tool = McpToolListBuilder.Build(catalog, ledger)
                .First(t => t.Name == "get_bounding_box");

            Assert.DoesNotContain("[PILOT]", tool.Description);
        }

        [Fact]
        public void tool_yang_verified_muncul_di_daftar()
        {
            var (catalog, ledger) = WithAllVerified();
            var tools = McpToolListBuilder.Build(catalog, ledger);

            Assert.Equal(20, tools.Count);
            Assert.Contains(tools, t => t.Name == "get_active_document");
        }

        [Fact]
        public void tool_reference_only_tidak_pernah_muncul()
        {
            // Yang menentukan eksposur adalah capabilityId milik TOOL di
            // tools.json, bukan daftar tools di ledger. Jadi id kemampuan di
            // sini harus yang sebenarnya.
            var yaml = @"
schema_version: '1.0'
capabilities:
  - id: document_inspection
    status: verified
    tools: [get_active_document]
  - id: part_features
    status: reference_only
    tools: [extrude_boss]
";
            var ledger = CapabilityLedger.Parse(yaml);
            var catalog = ToolCatalog.Load(RepoFiles.ToolsJson, ledger);
            var tools = McpToolListBuilder.Build(catalog, ledger);

            Assert.Contains(tools, t => t.Name == "get_active_document");
            Assert.DoesNotContain(tools, t => t.Name == "extrude_boss");

            // Kemampuan yang tidak disebut ledger juga tidak muncul.
            Assert.DoesNotContain(tools, t => t.Name == "hole_wizard");
        }

        [Fact]
        public void kemampuan_pilot_ditandai_di_deskripsi()
        {
            var yaml = @"
schema_version: '1.0'
capabilities:
  - id: part_features
    status: pilot
    tools: [extrude_boss]
";
            var ledger = CapabilityLedger.Parse(yaml);
            var catalog = ToolCatalog.Load(RepoFiles.ToolsJson, ledger);
            var tool = McpToolListBuilder.Build(catalog, ledger).First(t => t.Name == "extrude_boss");

            Assert.Contains("[PILOT]", tool.Description);
            Assert.Contains("direview manusia", tool.Description);
        }

        [Fact]
        public void referensi_skema_eksternal_di_inline_karena_klien_tidak_bisa_menyelesaikannya()
        {
            var (catalog, ledger) = WithAllVerified();
            var tool = McpToolListBuilder.Build(catalog, ledger).Single(t => t.Name == "add_circle");

            var json = tool.InputSchema.ToJsonString();

            // $ref ke berkas eksternal tidak bisa diselesaikan klien MCP.
            Assert.DoesNotContain("$ref", json);

            // Diganti penjelasan bentuknya, bukan path berkas.
            Assert.DoesNotContain("common.schema.json", json);
            Assert.Contains("milimeter", json);
        }

        [Fact]
        public void EntityRef_dijelaskan_dan_Face1_dinyatakan_tidak_didukung()
        {
            var (catalog, ledger) = WithAllVerified();
            var tool = McpToolListBuilder.Build(catalog, ledger).Single(t => t.Name == "fillet");

            var json = tool.InputSchema.ToJsonString();

            Assert.Contains("normal_and_point", json);
            Assert.Contains("TIDAK didukung", json);
        }
    }

    public class McpRequestHandlerTests
    {
        private static McpRequestHandler Handler(IToolExecutor? executor = null, params string[] toolNames)
        {
            var tools = toolNames
                .Select(name => new McpToolDescriptor
                {
                    Name = name,
                    Description = "tool uji",
                    InputSchema = new JsonObject { ["type"] = "object" },
                })
                .ToList();

            return new McpRequestHandler(tools, executor ?? new FakeToolExecutor());
        }

        [Fact]
        public async Task initialize_mengembalikan_protokol_dan_info_server()
        {
            var response = await Handler().HandleAsync(
                @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize"",""params"":{}}");

            Assert.NotNull(response);
            var result = response!["result"]!.AsObject();

            Assert.Equal("mikosai-solidworks", result["serverInfo"]!["name"]!.GetValue<string>());
            Assert.NotNull(result["protocolVersion"]);

            // Instruksi harus memberi tahu model bahwa daftar kosong itu wajar,
            // dan bahwa ok=true belum berarti berhasil.
            var instructions = result["instructions"]!.GetValue<string>();
            Assert.Contains("bukan error", instructions);
            Assert.Contains("verification", instructions);
        }

        [Fact]
        public async Task tools_list_mengembalikan_tool_yang_diekspos()
        {
            var response = await Handler(null, "get_active_document", "list_features").HandleAsync(
                @"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/list""}");

            var tools = response!["result"]!["tools"]!.AsArray();

            Assert.Equal(2, tools.Count);
        }

        [Fact]
        public async Task tools_list_boleh_kosong_dan_itu_bukan_error()
        {
            var response = await Handler().HandleAsync(
                @"{""jsonrpc"":""2.0"",""id"":3,""method"":""tools/list""}");

            Assert.Null(response!["error"]);
            Assert.Empty(response["result"]!["tools"]!.AsArray());
        }

        [Fact]
        public async Task tool_yang_tidak_diekspos_ditolak_di_batas_terakhir()
        {
            // Gating bukan hanya penyembunyian dari daftar - pemanggilan langsung
            // juga harus ditolak.
            var executor = new FakeToolExecutor();
            var handler = Handler(executor, "get_active_document");

            var response = await handler.HandleAsync(
                @"{""jsonrpc"":""2.0"",""id"":4,""method"":""tools/call"",""params"":{""name"":""extrude_boss"",""arguments"":{}}}");

            Assert.True(response!["result"]!["isError"]!.GetValue<bool>());
            Assert.Equal(0, executor.CallCount);
        }

        [Fact]
        public async Task tools_call_meneruskan_argumen_ke_executor()
        {
            var executor = new FakeToolExecutor();
            var handler = Handler(executor, "add_circle");

            await handler.HandleAsync(
                @"{""jsonrpc"":""2.0"",""id"":5,""method"":""tools/call"",""params"":{""name"":""add_circle"",""arguments"":{""diameter"":12}}}");

            Assert.Equal(12, executor.ArgumentsFor("add_circle")!["diameter"]!.GetValue<int>());
        }

        [Fact]
        public async Task isError_mengikuti_keberhasilan_TUGAS_bukan_keberhasilan_panggilan()
        {
            // extrude_cut yang tidak memotong apa pun: ok=true, verifikasi FAIL.
            // Model harus melihat ini sebagai kegagalan.
            var executor = new FakeToolExecutor()
                .SucceedsButVerificationFails("extrude_cut", "volume_decreased", "Volume tidak berubah.");

            var handler = Handler(executor, "extrude_cut");

            var response = await handler.HandleAsync(
                @"{""jsonrpc"":""2.0"",""id"":6,""method"":""tools/call"",""params"":{""name"":""extrude_cut"",""arguments"":{}}}");

            var result = response!["result"]!.AsObject();
            var text = result["content"]!.AsArray()[0]!["text"]!.GetValue<string>();

            Assert.True(result["isError"]!.GetValue<bool>());
            Assert.Contains("\"ok\": true", text);
            Assert.Contains("\"taskSuccessful\": false", text);
            Assert.Contains("FAIL", text);
        }

        [Fact]
        public async Task sukses_tanpa_verifikasi_juga_dilaporkan_sebagai_error()
        {
            var executor = new FakeToolExecutor().SucceedsWithoutEvidence("extrude_boss");
            var handler = Handler(executor, "extrude_boss");

            var response = await handler.HandleAsync(
                @"{""jsonrpc"":""2.0"",""id"":7,""method"":""tools/call"",""params"":{""name"":""extrude_boss"",""arguments"":{}}}");

            Assert.True(response!["result"]!["isError"]!.GetValue<bool>());
        }

        [Fact]
        public async Task executor_yang_melempar_exception_tidak_menjatuhkan_server()
        {
            var executor = new FakeToolExecutor()
                .Throws("get_active_document", new InvalidOperationException("meledak"));

            var handler = Handler(executor, "get_active_document");

            var response = await handler.HandleAsync(
                @"{""jsonrpc"":""2.0"",""id"":8,""method"":""tools/call"",""params"":{""name"":""get_active_document"",""arguments"":{}}}");

            Assert.NotNull(response);
            Assert.True(response!["result"]!["isError"]!.GetValue<bool>());
        }

        [Fact]
        public async Task json_rusak_menghasilkan_parse_error_bukan_crash()
        {
            var response = await Handler().HandleAsync("{ ini bukan json");

            Assert.Equal(-32700, response!["error"]!["code"]!.GetValue<int>());
        }

        [Fact]
        public async Task method_tidak_dikenal_menghasilkan_MethodNotFound()
        {
            var response = await Handler().HandleAsync(
                @"{""jsonrpc"":""2.0"",""id"":9,""method"":""sesuatu/aneh""}");

            Assert.Equal(-32601, response!["error"]!["code"]!.GetValue<int>());
        }

        [Fact]
        public async Task notifikasi_tanpa_id_tidak_mendapat_respons()
        {
            // Sesuai spesifikasi JSON-RPC.
            var response = await Handler().HandleAsync(
                @"{""jsonrpc"":""2.0"",""method"":""notifications/initialized""}");

            Assert.Null(response);
        }
    }

    public class AgentWorkspaceTests : IDisposable
    {
        private readonly string _root;

        public AgentWorkspaceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mikosai-ws-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (Exception)
            {
                // Pembersihan yang gagal tidak menggagalkan test.
            }
        }

        /// <summary>
        /// Prepare TIDAK menulis .agents/mcp_config.json.
        ///
        /// Dua test sebelumnya di sini menuntut berkas itu ADA, dan satu lagi
        /// memeriksa bentuk isinya. Keduanya menguji hal yang ternyata tidak ada
        /// gunanya: agy MENGABAIKAN berkas itu sepenuhnya - dibuktikan dengan
        /// menaruh server palsu di sana dan melihat `agy mcp list` tidak
        /// menampilkannya.
        ///
        /// Jadi yang dulu ditulis hanyalah satu salinan bearer token di disk
        /// tanpa pembaca. Test ini membalik tuntutannya: berkas itu harus TIDAK
        /// ADA, supaya tidak ada yang menambahkannya kembali tanpa sadar.
        /// </summary>
        [Fact]
        public void Prepare_tidak_menulis_salinan_token_yang_tidak_ada_pembacanya()
        {
            var workspace = new AgentWorkspace(_root);
            var token = AgentWorkspace.NewSessionToken();

            workspace.Prepare(new Uri("http://127.0.0.1:45678/mcp"), token);

            Assert.False(
                File.Exists(Path.Combine(_root, ".agents", "mcp_config.json")),
                ".agents/mcp_config.json ditulis lagi. agy mengabaikannya, jadi itu " +
                "cuma salinan bearer token di disk tanpa pembaca.");

            // Folder .agents juga tidak perlu ada lagi.
            Assert.False(Directory.Exists(Path.Combine(_root, ".agents")));
        }

        /// <summary>
        /// Token HANYA ada di session.json, dan berkas itu punya pembaca
        /// (scripts/mcp-roundtrip.ps1) serta ACL khusus pengguna.
        ///
        /// Test ini menjaga jumlah tempat secret itu tersimpan tetap satu di
        /// dalam workspace kita.
        /// </summary>
        [Fact]
        public void token_hanya_muncul_di_session_json()
        {
            var workspace = new AgentWorkspace(_root);
            var token = AgentWorkspace.NewSessionToken();

            workspace.Prepare(new Uri("http://127.0.0.1:45678/mcp"), token);

            var memuatToken = Directory
                .GetFiles(_root, "*", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains(token))
                .Select(f => f.Substring(_root.Length).TrimStart(Path.DirectorySeparatorChar))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(new[] { "session.json" }, memuatToken);
        }

        [Fact]
        public void URL_non_loopback_ditolak()
        {
            // Jaring pengaman: menulis URL non-loopback ke konfigurasi AI berarti
            // mengekspos tool CAD ke jaringan.
            var workspace = new AgentWorkspace(_root);

            Assert.Throws<ArgumentException>(
                () => workspace.Prepare(new Uri("http://192.168.1.10:8765/mcp"), "token"));
        }

        [Fact]
        public void token_sesi_cukup_panjang_dan_selalu_berbeda()
        {
            var tokens = Enumerable.Range(0, 50).Select(_ => AgentWorkspace.NewSessionToken()).ToList();

            Assert.All(tokens, t => Assert.True(t.Length >= 40, $"Token terlalu pendek: {t.Length}"));
            Assert.Equal(50, tokens.Distinct().Count());
        }

        [Fact]
        public void session_file_memuat_token_dan_url()
        {
            var workspace = new AgentWorkspace(_root);
            workspace.Prepare(new Uri("http://127.0.0.1:45678/mcp"), "token-rahasia");

            var session = JsonNode.Parse(File.ReadAllText(workspace.SessionFilePath))!.AsObject();

            Assert.Equal("token-rahasia", session["token"]!.GetValue<string>());
            Assert.Equal("http://127.0.0.1:45678/mcp", session["serverUrl"]!.GetValue<string>());
        }

        [Fact]
        public void Remove_menghapus_seluruh_workspace()
        {
            var workspace = new AgentWorkspace(_root);
            workspace.Prepare(new Uri("http://127.0.0.1:45678/mcp"), "token");

            Assert.True(Directory.Exists(_root));

            workspace.Remove();

            // Uninstall bersih: satu folder, hilang seluruhnya.
            Assert.False(Directory.Exists(_root));
        }
    }

    /// <summary>
    /// Round-trip HTTP sungguhan lewat loopback. Bukan mock - socket nyata,
    /// port nyata.
    /// </summary>
    public class McpHttpServerTests : IDisposable
    {
        private readonly string _token = AgentWorkspace.NewSessionToken();
        private readonly FakeToolExecutor _executor = new FakeToolExecutor();
        private readonly McpHttpServer _server;
        private readonly HttpClient _client = new HttpClient();

        public McpHttpServerTests()
        {
            var tools = new List<McpToolDescriptor>
            {
                new McpToolDescriptor
                {
                    Name = "get_active_document",
                    Description = "tool uji",
                    InputSchema = new JsonObject { ["type"] = "object" },
                },
            };

            _server = new McpHttpServer(new McpRequestHandler(tools, _executor), _token);
            _server.Start();
        }

        public void Dispose()
        {
            _client.Dispose();
            _server.Dispose();
        }

        private async Task<HttpResponseMessage> PostAsync(string json, string? token)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _server.Endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return await _client.SendAsync(request);
        }

        [Fact]
        public void server_memakai_port_ephemeral_dan_endpoint_loopback()
        {
            Assert.True(_server.Port > 0);
            Assert.True(_server.Endpoint.IsLoopback);
            Assert.Equal("127.0.0.1", _server.Endpoint.Host);
            Assert.True(_server.IsRunning);
        }

        [Fact]
        public async Task round_trip_tools_list_berhasil_dengan_token_yang_benar()
        {
            var response = await PostAsync(
                @"{""jsonrpc"":""2.0"",""id"":1,""method"":""tools/list""}",
                _token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
            var tools = body["result"]!["tools"]!.AsArray();

            Assert.Single(tools);
            Assert.Equal("get_active_document", tools[0]!["name"]!.GetValue<string>());
        }

        [Fact]
        public async Task round_trip_tools_call_mencapai_executor()
        {
            var response = await PostAsync(
                @"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/call"",""params"":{""name"":""get_active_document"",""arguments"":{}}}",
                _token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(_executor.WasCalled("get_active_document"));
        }

        [Fact]
        public async Task request_tanpa_token_ditolak_401()
        {
            var response = await PostAsync(
                @"{""jsonrpc"":""2.0"",""id"":3,""method"":""tools/list""}",
                token: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(0, _executor.CallCount);
        }

        [Fact]
        public async Task request_dengan_token_salah_ditolak_401()
        {
            var response = await PostAsync(
                @"{""jsonrpc"":""2.0"",""id"":4,""method"":""tools/list""}",
                token: "token-palsu-yang-panjangnya-mirip-aslinya-saja");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task token_lewat_query_string_juga_diterima()
        {
            // Fallback, karena belum terkonfirmasi apakah entry serverUrl
            // Antigravity meneruskan custom header.
            var url = $"{_server.Endpoint}?token={Uri.EscapeDataString(_token)}";

            var response = await _client.PostAsync(
                url,
                new StringContent(@"{""jsonrpc"":""2.0"",""id"":5,""method"":""tools/list""}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GET_ditolak_405()
        {
            var response = await _client.GetAsync(_server.Endpoint);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        [Fact]
        public async Task notifikasi_dijawab_202_tanpa_body()
        {
            var response = await PostAsync(
                @"{""jsonrpc"":""2.0"",""method"":""notifications/initialized""}",
                _token);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public void dua_server_bisa_hidup_bersamaan_dengan_port_berbeda()
        {
            // Port ephemeral berarti dua instance SolidWorks tidak bentrok.
            using var kedua = new McpHttpServer(
                new McpRequestHandler(Array.Empty<McpToolDescriptor>(), _executor),
                AgentWorkspace.NewSessionToken());

            kedua.Start();

            Assert.NotEqual(_server.Port, kedua.Port);
            Assert.True(kedua.IsRunning);
        }

        [Fact]
        public void Dispose_menghentikan_listener()
        {
            var server = new McpHttpServer(
                new McpRequestHandler(Array.Empty<McpToolDescriptor>(), _executor),
                AgentWorkspace.NewSessionToken());

            server.Start();
            Assert.True(server.IsRunning);

            server.Dispose();
            Assert.False(server.IsRunning);

            // Dispose kedua kali harus aman.
            server.Dispose();
        }
    }
}
