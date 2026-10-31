using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using MikosAI.Core.Capabilities;
using MikosAI.Core.Tools;

namespace MikosAI.Core.Mcp
{
    /// <summary>
    /// Satu tool dalam bentuk yang dipahami klien MCP.
    ///
    /// Dihasilkan dari <see cref="ToolCatalog"/> yang sudah difilter ledger.
    /// Tool yang kemampuannya belum verified atau pilot TIDAK PERNAH sampai ke
    /// sini - itu mekanisme yang membuat AI secara struktural tidak bisa
    /// memanggil hal yang belum kita uji.
    /// </summary>
    public sealed class McpToolDescriptor
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public JsonObject InputSchema { get; set; } = new JsonObject();

        public JsonObject ToJson() => new JsonObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = InputSchema.DeepClone(),
        };
    }

    /// <summary>
    /// Menyusun daftar tool MCP dari katalog dan ledger.
    /// </summary>
    public static class McpToolListBuilder
    {
        /// <summary>
        /// Membangun daftar tool yang boleh dilihat AI.
        ///
        /// Deskripsi tool digabung dengan catatan penting dan penanda review,
        /// karena itulah satu-satunya tempat kita bisa memberi konteks ke model
        /// sebelum ia memutuskan memanggil sesuatu.
        /// </summary>
        public static IReadOnlyList<McpToolDescriptor> Build(ToolCatalog catalog, CapabilityLedger ledger)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            if (ledger is null) throw new ArgumentNullException(nameof(ledger));

            return catalog.ExposableTools()
                .Select(tool => new McpToolDescriptor
                {
                    Name = tool.Name,
                    Description = BuildDescription(tool, ledger.Find(tool.CapabilityId)),
                    InputSchema = NormalizeSchema(tool),
                })
                .ToList();
        }

        private static string BuildDescription(ToolDefinition tool, Capability? capability)
        {
            var parts = new List<string> { tool.Description };

            if (capability?.Status == CapabilityStatus.Pilot)
            {
                parts.Add("[PILOT] Hasil tool ini wajib direview manusia dan tidak boleh dianggap final.");
            }

            if (capability?.EffectiveReviewRequired == true && capability.Status != CapabilityStatus.Pilot)
            {
                parts.Add("[REVIEW] Hasil tool ini butuh review manusia.");
            }

            if (!string.IsNullOrWhiteSpace(tool.Notes))
            {
                parts.Add("Catatan: " + tool.Notes);
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        /// Menyiapkan input schema untuk klien MCP.
        ///
        /// Skema di tools.json memakai <c>$ref</c> ke common.schema.json. Klien
        /// MCP tidak bisa menyelesaikan referensi berkas eksternal, jadi untuk
        /// sekarang referensi itu diganti objek permisif dengan deskripsi.
        ///
        /// Ini pilihan sadar: validasi ketat tetap terjadi di sisi kita
        /// (PlanValidator dan ToolExecutor), bukan dipercayakan ke klien.
        /// Klien hanya perlu tahu bentuk kasarnya.
        /// </summary>
        private static JsonObject NormalizeSchema(ToolDefinition tool)
        {
            if (tool.InputSchema.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(),
                };
            }

            var node = JsonNode.Parse(tool.InputSchema.GetRawText());
            if (node is not JsonObject schema)
            {
                return new JsonObject { ["type"] = "object" };
            }

            InlineExternalRefs(schema);
            return schema;
        }

        private static void InlineExternalRefs(JsonNode node)
        {
            switch (node)
            {
                case JsonObject obj:
                    if (obj.TryGetPropertyValue("$ref", out var reference)
                        && reference is JsonValue value
                        && value.TryGetValue<string>(out var text)
                        && text is not null
                        && text.Contains("common.schema.json"))
                    {
                        var defName = text.Substring(text.LastIndexOf('/') + 1);

                        obj.Remove("$ref");
                        obj["type"] = DefaultTypeFor(defName);

                        // Deskripsi bentuknya, bukan path berkas. Menunjuk berkas
                        // yang tidak bisa dibaca klien hanya jadi kebisingan.
                        obj["description"] = DescribeDefinition(defName);
                        return;
                    }

                    foreach (var property in obj.ToList())
                    {
                        if (property.Value is not null)
                        {
                            InlineExternalRefs(property.Value);
                        }
                    }
                    break;

                case JsonArray array:
                    foreach (var item in array.ToList())
                    {
                        if (item is not null)
                        {
                            InlineExternalRefs(item);
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Penjelasan singkat bentuk setiap definisi bersama, supaya model tahu
        /// apa yang harus dikirim tanpa perlu membaca berkas skema.
        /// </summary>
        private const string EntityRefShape =
            "Referensi entitas berbasis kriteria stabil. "
            + "Bidang, fitur, atau sketsa: { kind, by: \"name\", name }. "
            + "Permukaan: { kind: \"face\", by: \"normal_and_point\", normal, nearPoint }. "
            + "Tepi: { kind: \"edge\", by: \"midpoint\", nearPoint }. "
            + "Face1, Edge1, dan koordinat layar TIDAK didukung - mereka rusak begitu ada fitur disisipkan.";

        private static string DescribeDefinition(string definitionName)
        {
            switch (definitionName)
            {
                case "Length":
                    return "Panjang dalam milimeter. Boleh angka, atau referensi parameter seperti \"$plateWidth\".";
                case "Angle":
                    return "Sudut dalam derajat. Boleh angka, atau referensi parameter.";
                case "Count":
                    return "Bilangan bulat positif, atau referensi parameter.";
                case "Point2D":
                    return "Titik pada bidang sketsa aktif dalam mm: { x, y }.";
                case "Vector3D":
                    return "Vektor dalam koordinat model: { x, y, z }.";
                case "EntityRef":
                    return EntityRefShape;

                case "EntitySelector":
                    // Bentuk EntityRef diulang di sini dengan sengaja: tool
                    // seperti fillet dan chamfer HANYA memperlihatkan
                    // EntitySelector, jadi tanpa ini model tidak pernah tahu
                    // bentuk elemennya.
                    return "Array berisi satu atau beberapa referensi entitas. " + EntityRefShape;
                case "EndCondition":
                    return "Kondisi akhir extrude: { type: \"blind\", depth }, "
                           + "{ type: \"through_all\" }, { type: \"up_to_next\" }, atau "
                           + "{ type: \"up_to_surface\", face }.";
                default:
                    return $"Objek {definitionName}.";
            }
        }

        private static string DefaultTypeFor(string definitionName)
        {
            switch (definitionName)
            {
                case "Length":
                case "Angle":
                    // Boleh angka atau referensi parameter "$nama".
                    return "number";
                case "Count":
                    return "integer";
                case "EntitySelector":
                    return "array";
                default:
                    return "object";
            }
        }
    }
}
