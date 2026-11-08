using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MikosAI.Core.Plan
{
    /// <summary>
    /// Alur uji native yang disengaja sempit sebelum tool tulis dipromosikan.
    /// Bukan pengganti PlanValidator/ledger dan tidak dipakai endpoint MCP/AI.
    /// Hanya empat langkah tetap pada dokumen BARU milik run manual ini.
    /// </summary>
    public sealed class BlockPilotPlan
    {
        public double WidthMm { get; private set; }
        public double HeightMm { get; private set; }
        public double DepthMm { get; private set; }
        public JsonObject[] Arguments { get; private set; } = Array.Empty<JsonObject>();
        public static readonly string[] Tools = { "create_sketch", "add_rectangle", "close_sketch", "extrude_boss" };

        public static BlockPilotPlan Read(JsonObject plan)
        {
            if (plan["schemaVersion"]?.GetValue<string>() != "1.0" ||
                plan["document"]?["type"]?.GetValue<string>() != "part" ||
                plan["units"]?["length"]?.GetValue<string>() != "mm" ||
                plan["units"]?["angle"]?.GetValue<string>() != "deg")
                throw new ArgumentException("Pilot memerlukan plan 1.0 dalam mm/deg.");
            if (plan["openQuestions"] is JsonArray questions && questions.Any(q=>q?["blocking"]?.GetValue<bool>()==true))
                throw new ArgumentException("Pertanyaan blocking belum dijawab.");
            if (plan["acceptance"] is JsonArray acceptance && acceptance.Any(c=>
                !new[]{"rebuild_clean","all_sketches_fully_defined","nonzero_volume"}.Contains(c?["check"]?.GetValue<string>()) ||
                (c?["arguments"] is JsonObject a && a.Count>0)))
                throw new ArgumentException("Pilot tidak mengabaikan acceptance yang belum didukung.");
            var steps = plan["steps"]?.AsArray() ?? throw new ArgumentException("Steps tidak ada.");
            if (steps.Count != Tools.Length || steps.Where((s,i) => s?["tool"]?.GetValue<string>() != Tools[i]).Any())
                throw new ArgumentException("Pilot hanya menerima urutan create_sketch/add_rectangle/close_sketch/extrude_boss.");
            var parameters = ParameterResolver.BuildParameterTable(plan["parameters"]?.AsObject(), null);
            var args = steps.Select((s,i) => ParameterResolver.Resolve(s!["arguments"]!.AsObject(), parameters, Tools[i])).ToArray();
            double Number(JsonNode? node)
            {
                if (node is null) return double.NaN;
                using var document=JsonDocument.Parse(node.ToJsonString());
                return document.RootElement.ValueKind==JsonValueKind.Number && document.RootElement.TryGetDouble(out var n) ? n : double.NaN;
            }
            if (args[0]["plane"]?["kind"]?.GetValue<string>() != "plane" ||
                args[0]["plane"]?["by"]?.GetValue<string>() != "name" ||
                args[0]["plane"]?["name"]?.GetValue<string>() != "Front Plane" ||
                args[0]["name"]?.GetValue<string>() != "BlockProfile" ||
                args[1]["mode"]?.GetValue<string>() != "corner" ||
                Number(args[1]["corner"]?["x"]) != 0 || Number(args[1]["corner"]?["y"]) != 0 ||
                args[1]["addDimensions"]?.GetValue<bool>() != true ||
                args[2]["sketchName"]?.GetValue<string>() != "BlockProfile" ||
                args[3]["sketch"]?["kind"]?.GetValue<string>() != "sketch" ||
                args[3]["sketch"]?["by"]?.GetValue<string>() != "name" ||
                args[3]["sketch"]?["name"]?.GetValue<string>() != "BlockProfile" ||
                args[3]["endCondition"]?["type"]?.GetValue<string>() != "blind" ||
                args[3]["direction"]?.GetValue<string>() != "normal" || args[3]["merge"]?.GetValue<bool>() != true ||
                args[3].ContainsKey("draftAngle"))
                throw new ArgumentException("Pilot dibatasi corner (0,0), Front Plane, BlockProfile, dimensi desain, blind normal tanpa draft.");
            double Length(JsonNode? node)
            {
                var value = Number(node);
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > 10000)
                    throw new ArgumentException("Ukuran pilot harus >0 dan <=10000 mm.");
                return value;
            }
            var width=Length(args[1]["width"]);
            var height=Length(args[1]["height"]);
            var depth=Length(args[3]["endCondition"]?["depth"]);
            args[1]["width"]=width; args[1]["height"]=height; args[3]["endCondition"]!["depth"]=depth;
            return new BlockPilotPlan { WidthMm=width, HeightMm=height, DepthMm=depth, Arguments=args };
        }
    }
}
