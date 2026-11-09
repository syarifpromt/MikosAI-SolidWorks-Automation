using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MikosAI.Core.Execution;

namespace MikosAI.Core.Mcp
{
    /// <summary>
    /// Memproses pesan JSON-RPC MCP.
    ///
    /// Sengaja dipisah dari transport HTTP: seluruh logika protokol bisa diuji
    /// tanpa membuka socket, tanpa port, tanpa izin firewall. Transport hanya
    /// memindahkan byte.
    /// </summary>
    public sealed class McpRequestHandler
    {
        private const string ProtocolVersion = "2025-06-18";
        private const string ServerName = "mikosai-solidworks";
        private const string ServerVersion = "0.1.0";

        // Kode error standar JSON-RPC.
        private const int ParseError = -32700;
        private const int InvalidRequest = -32600;
        private const int MethodNotFound = -32601;
        private const int InternalError = -32603;

        private readonly IReadOnlyList<McpToolDescriptor> _tools;
        private readonly IToolExecutor _executor;
        private readonly HashSet<string> _toolNames;

        public McpRequestHandler(IReadOnlyList<McpToolDescriptor> tools, IToolExecutor executor)
        {
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _toolNames = new HashSet<string>(tools.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Memproses satu pesan. Mengembalikan null untuk notifikasi (pesan tanpa
        /// id), sesuai spesifikasi JSON-RPC.
        /// </summary>
        public async Task<JsonObject?> HandleAsync(string requestJson, CancellationToken cancellationToken = default)
        {
            JsonObject request;

            try
            {
                var node = JsonNode.Parse(requestJson);
                if (node is not JsonObject obj)
                {
                    return Error(null, InvalidRequest, "Pesan harus berupa objek JSON.");
                }
                request = obj;
            }
            catch (Exception exception)
            {
                return Error(null, ParseError, "JSON tidak bisa diparse: " + exception.Message);
            }

            var id = request["id"]?.DeepClone();
            var method = request["method"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(method))
            {
                return Error(id, InvalidRequest, "Field 'method' wajib ada.");
            }

            // Notifikasi: tidak ada id, jadi tidak ada respons.
            var isNotification = request["id"] is null;

            try
            {
                switch (method)
                {
                    case "initialize":
                        return isNotification ? null : Result(id, Initialize());

                    case "notifications/initialized":
                        return null;

                    case "ping":
                        return isNotification ? null : Result(id, new JsonObject());

                    case "tools/list":
                        return isNotification ? null : Result(id, ListTools());

                    case "tools/call":
                        var callResult = await CallToolAsync(request["params"]?.AsObject(), cancellationToken)
                            .ConfigureAwait(false);
                        return isNotification ? null : Result(id, callResult);

                    default:
                        return isNotification ? null : Error(id, MethodNotFound, $"Method '{method}' tidak dikenal.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Satu request yang meledak tidak boleh menjatuhkan server -
                // apalagi karena server ini hidup di dalam SLDWORKS.exe.
                return Error(id, InternalError, exception.Message);
            }
        }

        private JsonObject Initialize() => new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = true },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
            },
            ["instructions"] =
                "Tool SolidWorks dijalankan di dalam proses SolidWorks yang sedang berjalan. " +
                "Hanya kemampuan yang sudah terverifikasi yang tersedia - daftar tool bisa kosong, " +
                "dan itu bukan error. Setiap operasi tulis diverifikasi dengan membaca ulang model; " +
                "tool yang mengembalikan ok=true belum berarti tugasnya berhasil, periksa field verification.",
        };

        private JsonObject ListTools()
        {
            var array = new JsonArray();
            foreach (var tool in _tools)
            {
                array.Add(tool.ToJson());
            }

            return new JsonObject { ["tools"] = array };
        }

        private async Task<JsonObject> CallToolAsync(JsonObject? parameters, CancellationToken cancellationToken)
        {
            var name = parameters?["name"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(name))
            {
                return ToolCallError("Parameter 'name' wajib ada.");
            }

            // Tool yang tidak ada di daftar yang diekspos TIDAK boleh dieksekusi,
            // walaupun ia terdefinisi di tools.json. Ini penegakan gating di
            // batas terakhir - bukan hanya penyembunyian dari daftar.
            if (!_toolNames.Contains(name!))
            {
                return ToolCallError(
                    $"Tool '{name}' tidak tersedia. " +
                    "Kemungkinan kemampuannya belum terverifikasi di capabilities.yaml.");
            }

            var arguments = parameters?["arguments"]?.AsObject() ?? new JsonObject();

            ToolResult result;
            try
            {
                result = await _executor.ExecuteAsync(name!, arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return ToolCallError($"Tool '{name}' melempar exception: {exception.Message}");
            }

            return BuildToolCallResult(result);
        }

        /// <summary>
        /// Menyusun respons tools/call.
        ///
        /// `isError` dipasang berdasarkan keberhasilan TUGAS, bukan keberhasilan
        /// panggilan. Tool yang mengembalikan ok=true tapi verifikasinya FAIL
        /// adalah kegagalan, dan model harus melihatnya begitu.
        /// </summary>
        internal static JsonObject BuildToolCallResult(ToolResult result)
        {
            var summary = new JsonObject
            {
                ["ok"] = result.Ok,
                ["capabilityId"] = result.CapabilityId,
                ["taskSuccessful"] = result.IsTaskSuccessful,
            };

            // ModelChanged/readBackFailed adalah kontrak tulis yang sudah ada.
            // Read-only mempertahankan envelope lama tanpa klaim perubahan.
            if (result.IsMutation)
            {
                summary["modelChanged"]=result.ModelChanged;
                summary["readBackFailed"]=result.ReadBackFailed;
            }

            if (result.Data is not null && result.Data.Count > 0)
            {
                var data = new JsonObject();
                foreach (var pair in result.Data)
                {
                    // Opsi WAJIB diteruskan di sini, bukan hanya saat
                    // ToJsonString: SerializeToNode membakar teks angkanya ke
                    // dalam node, jadi konverter yang dipasang belakangan tidak
                    // lagi punya kesempatan memperbaikinya.
                    data[pair.Key] = pair.Value is null
                        ? null
                        : System.Text.Json.JsonSerializer.SerializeToNode(
                            pair.Value, MikosAI.Core.Json.JsonAngka.Opsi);
                }
                summary["data"] = data;
            }

            if (result.Verification is not null)
            {
                var checks = new JsonArray();
                foreach (var check in result.Verification.Checks)
                {
                    checks.Add(new JsonObject
                    {
                        ["id"] = check.Id,
                        ["verdict"] = VerdictParser.ToWire(check.Verdict),
                        ["required"] = check.Required,
                        ["message"] = check.Message,
                    });
                }

                summary["verification"] = new JsonObject
                {
                    ["verdict"] = VerdictParser.ToWire(result.Verification.Verdict),
                    ["manualReviewRequired"] = result.Verification.ManualReviewRequired,
                    ["checks"] = checks,
                };
            }
            else
            {
                summary["verification"] = null;
            }

            if (result.Error is not null)
            {
                summary["error"] = new JsonObject
                {
                    ["kind"] = result.Error.Kind.ToString(),
                    ["message"] = result.Error.Message,
                };
            }

            return new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = summary.ToJsonString(MikosAI.Core.Json.JsonAngka.OpsiIndent),
                    },
                },
                ["isError"] = !result.IsTaskSuccessful,
            };
        }

        private static JsonObject ToolCallError(string message) => new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = message },
            },
            ["isError"] = true,
        };

        private static JsonObject Result(JsonNode? id, JsonObject payload) => new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = payload,
        };

        private static JsonObject Error(JsonNode? id, int code, string message) => new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
    }
}
