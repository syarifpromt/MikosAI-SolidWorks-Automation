using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Lubang opsional pada permukaan ATAS balok. null berarti balok polos.
        ///
        /// Permukaan atas dipilih dengan sengaja, bukan demi kemudahan: ia
        /// BUKAN Front Plane, jadi koordinat sketsanya berbeda dari koordinat
        /// model. Di Front Plane keduanya kebetulan sama, sehingga transformasi
        /// yang hilang pun akan lulus - dan test yang tidak bisa gagal tidak
        /// menguji apa pun.
        /// </summary>
        public HoleSpec? Holes { get; private set; }

        public static readonly string[] Tools = { "create_sketch", "add_rectangle", "close_sketch", "extrude_boss" };

        /// <summary>Langkah kelima, opsional.</summary>
        public const string ToolLubang = "hole_wizard";

        public static BlockPilotPlan Create(double width,double height,double depth)
        {
            // Factory menggunakan batas yang sama dengan plan pilot dari disk.
            var plan=JsonNode.Parse(@"{""schemaVersion"":""1.0"",""units"":{""length"":""mm"",""angle"":""deg""},""document"":{""type"":""part""},""steps"":[
              {""tool"":""create_sketch"",""arguments"":{""plane"":{""kind"":""plane"",""by"":""name"",""name"":""Front Plane""},""name"":""BlockProfile""}},
              {""tool"":""add_rectangle"",""arguments"":{""mode"":""corner"",""corner"":{""x"":0,""y"":0},""addDimensions"":true}},
              {""tool"":""close_sketch"",""arguments"":{""sketchName"":""BlockProfile""}},
              {""tool"":""extrude_boss"",""arguments"":{""sketch"":{""kind"":""sketch"",""by"":""name"",""name"":""BlockProfile""},""endCondition"":{""type"":""blind""},""direction"":""normal"",""merge"":true}}]}" )!.AsObject();
            plan["steps"]![1]!["arguments"]!["width"]=width;
            plan["steps"]![1]!["arguments"]!["height"]=height;
            plan["steps"]![3]!["arguments"]!["endCondition"]!["depth"]=depth;
            return Read(plan);
        }

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
            // Empat langkah balok, ATAU empat plus hole_wizard. Tidak ada
            // bentuk lain: pilot ini sempit dengan sengaja.
            var adaLubang = steps.Count == Tools.Length + 1
                && steps[Tools.Length]?["tool"]?.GetValue<string>() == ToolLubang;
            if ((steps.Count != Tools.Length && !adaLubang)
                || steps.Take(Tools.Length).Where((s,i) => s?["tool"]?.GetValue<string>() != Tools[i]).Any())
                throw new ArgumentException(
                    "Pilot hanya menerima create_sketch/add_rectangle/close_sketch/extrude_boss, "
                    + "opsional diikuti hole_wizard.");
            var parameters = ParameterResolver.BuildParameterTable(plan["parameters"]?.AsObject(), null);
            var args = steps.Take(Tools.Length)
                .Select((s,i) => ParameterResolver.Resolve(s!["arguments"]!.AsObject(), parameters, Tools[i])).ToArray();
            var argLubang = adaLubang
                ? ParameterResolver.Resolve(steps[Tools.Length]!["arguments"]!.AsObject(), parameters, ToolLubang)
                : null;
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
            HoleSpec? holes = null;
            if (argLubang is not null)
            {
                var titik = new List<Titik3D>();
                foreach (var node in argLubang["positions"]?.AsArray() ?? throw new ArgumentException("hole_wizard memerlukan positions."))
                {
                    var x = Number(node?["x"]);
                    var y = Number(node?["y"]);
                    var z = Number(node?["z"]);
                    if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z))
                        throw new ArgumentException("positions memerlukan x, y, dan z dalam koordinat MODEL (mm).");
                    titik.Add(new Titik3D(x, y, z));
                }

                // HoleSpec yang menolak, bukan di sini - supaya aturan "jangan
                // menebak nilai engineering" hidup di SATU tempat.
                holes = HoleSpec.Buat(
                    argLubang["holeType"]?.GetValue<string>(),
                    argLubang["standard"]?.GetValue<string>(),
                    argLubang["fastenerSize"]?.GetValue<string>(),
                    titik);
            }

            return new BlockPilotPlan { WidthMm=width, HeightMm=height, DepthMm=depth, Arguments=args, Holes=holes };
        }
    }
}
