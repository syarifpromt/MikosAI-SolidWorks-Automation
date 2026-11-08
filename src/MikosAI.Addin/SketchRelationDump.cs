using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SolidWorks.Interop.sldworks;

namespace MikosAI.Addin
{
    /// <summary>
    /// Diagnostic opt-in, bukan field/tool kontrak. Hanya dipanggil pembaca
    /// lewat dispatcher utama. Berkas request kedaluwarsa membatasi capture.
    /// Tidak mengedit sketsa atau mengklasifikasi temuan sebagai limit resmi.
    /// </summary>
    internal static class SketchRelationDump
    {
        internal static void WriteIfRequested(ISketch sketch, string name, string documentTitle)
        {
            var folder = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "MikosAI", "diagnostics", "sketch-relations");
            var requestPath = Path.Combine(folder, "request.json");
            if (!File.Exists(requestPath)) return;
            using var request = JsonDocument.Parse(File.ReadAllText(requestPath));
            var config = request.RootElement;
            if (config.GetProperty("documentTitle").GetString() != documentTitle ||
                DateTimeOffset.Parse(config.GetProperty("expiresAtUtc").GetString()!, CultureInfo.InvariantCulture) < DateTimeOffset.UtcNow ||
                !config.GetProperty("sketchNames").EnumerateArray().Any(x => x.GetString() == name)) return;

            var errors = new List<string>();
            object? Attempt(string label, Func<object?> read)
            {
                try { return read(); }
                catch (Exception e) { errors.Add(label + ": " + e.Message); return null; }
            }
            string? Id(object value)
            {
                if (value is not Array ids || ids.Length != 2) return null;
                return Convert.ToInt32(ids.GetValue(0), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                    + ":" + Convert.ToInt32(ids.GetValue(1), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            }
            double[] Coordinates(ISketchPoint p) => new[] { p.X * 1000, p.Y * 1000, p.Z * 1000 };
            string? PointId(ISketchPoint p) => Id((object)p.GetID());
            string? SegmentId(ISketchSegment s) => s.GetType().ToString(CultureInfo.InvariantCulture) + ":" + Id((object)s.GetID());
            var endpoints = new HashSet<string>(StringComparer.Ordinal);
            var constructionEndpoints = new HashSet<string>(StringComparer.Ordinal);
            var segments = sketch.GetSketchSegments() as object[] ?? Array.Empty<object>();
            foreach (var item in segments)
            {
                if (item is not ISketchSegment s || item is not ISketchLine line) continue;
                var ids = s.ConstructionGeometry ? constructionEndpoints : endpoints;
                if ((object)line.GetStartPoint2() is ISketchPoint a && PointId(a) is string start) ids.Add(start);
                if ((object)line.GetEndPoint2() is ISketchPoint b && PointId(b) is string end) ids.Add(end);
            }
            Dictionary<string, object?> Point(ISketchPoint p)
            {
                var id = PointId(p);
                return new Dictionary<string, object?>
                {
                    ["kind"] = "point", ["id"] = id, ["pointType"] = p.Type,
                    ["isOriginByType"] = p.Type == 8, ["isExternalByType"] = p.Type == 10,
                    ["positionMm"] = Coordinates(p), ["ownerIsThisSketch"] = Equals(p.GetSketch(), sketch),
                    ["isAnalyzedEndpointById"] = id != null && endpoints.Contains(id),
                    ["isConstructionEndpointById"] = id != null && constructionEndpoints.Contains(id),
                };
            }
            Dictionary<string, object?> Segment(ISketchSegment s)
            {
                var row = new Dictionary<string, object?>
                {
                    ["kind"] = "segment", ["id"] = SegmentId(s), ["segmentType"] = s.GetType(),
                    ["construction"] = s.ConstructionGeometry, ["ownerIsThisSketch"] = Equals(s.GetSketch(), sketch),
                };
                if (s is ISketchLine line)
                {
                    row["start"] = Attempt("segment.start", () => Point((ISketchPoint)(object)line.GetStartPoint2()));
                    row["end"] = Attempt("segment.end", () => Point((ISketchPoint)(object)line.GetEndPoint2()));
                }
                return row;
            }
            object Describe(object? entity)
            {
                if (entity is ISketchPoint p) return Point(p);
                if (entity is ISketchSegment s) return Segment(s);
                return new Dictionary<string, object?> { ["kind"] = entity is null ? "null" : "other" };
            }
            var relationRows = new List<object?>();
            var candidates = new List<object?>();
            var manager = sketch.RelationManager;
            var relations = manager.GetRelations(0) as object[] ?? Array.Empty<object>();
            for (var index = 0; index < relations.Length; index++)
            {
                var relationIndex = index;
                relationRows.Add(Attempt("relation " + index, () =>
                {
                    var relation = (ISketchRelation)relations[relationIndex];
                    var entities = relation.GetEntities() as object[] ?? Array.Empty<object>();
                    var types = relation.GetEntitiesType() as int[] ?? Array.Empty<int>();
                    var kind = relation.GetRelationType();
                    if (kind == 9 && types.Length == entities.Length)
                    {
                        for (var i = 0; i < entities.Length; i++)
                        {
                            if (types[i] != 2 || entities[i] is not ISketchPoint p || !Equals(p.GetSketch(), sketch)) continue;
                            var id = PointId(p);
                            for (var j = 0; j < entities.Length; j++)
                            {
                                if (types[j] < 3 || types[j] > 7 || entities[j] is not ISketchSegment s ||
                                    s.ConstructionGeometry || !Equals(s.GetSketch(), sketch) || s.GetType() != 0 || s is not ISketchLine line) continue;
                                var a = (ISketchPoint)(object)line.GetStartPoint2();
                                var b = (ISketchPoint)(object)line.GetEndPoint2();
                                if (id == PointId(a) || id == PointId(b) || id is null || endpoints.Contains(id) || constructionEndpoints.Contains(id)) continue;
                                var pos = Coordinates(p);
                                var start = Coordinates(a);
                                var end = Coordinates(b);
                                double Distance(double[] x, double[] y) => Math.Sqrt(x.Zip(y, (u, v) => (u-v)*(u-v)).Sum());
                                var d = end.Zip(start, (x, y) => x-y).ToArray();
                                var length2 = d.Sum(x => x*x);
                                double? t = length2 > 0 ? pos.Zip(start, (x, y) => x-y).Zip(d, (x,y) => x*y).Sum()/length2 : (double?)null;
                                candidates.Add(new Dictionary<string, object?>
                                {
                                    ["relationIndex"] = relationIndex, ["pointEntityIndex"] = i, ["segmentEntityIndex"] = j,
                                    ["point"] = Point(p), ["target"] = Segment(s),
                                    ["oldDetectorWouldEmit"] = "non_endpoint_point_on_segment (UNAPPROVED)",
                                    ["distanceToStartMm"] = Distance(pos, start), ["distanceToEndMm"] = Distance(pos, end),
                                    ["projectionParameter"] = t,
                                    ["distanceToInfiniteLineMm"] = t.HasValue ? Distance(pos, start.Zip(d, (x,y) => x+t.Value*y).ToArray()) : (double?)null,
                                    ["note"] = "ID berbeda bukan bukti titik berada pada badan segmen; periksa posisi dan tipe titik.",
                                });
                            }
                        }
                    }
                    return new Dictionary<string, object?>
                    {
                        ["index"] = relationIndex, ["relationType"] = kind, ["entityTypes"] = types,
                        ["entities"] = entities.Select((x, i) => Attempt("entity " + relationIndex + ":" + i, () => Describe(x))).ToArray(),
                        ["definitionEntities"] = Attempt("definition " + relationIndex, () =>
                            (relation.GetDefinitionEntities2() as object[] ?? Array.Empty<object>()).Select(Describe).ToArray()),
                    };
                }));
            }
            var payload = new Dictionary<string, object?>
            {
                ["schemaVersion"] = 1, ["requestId"] = config.GetProperty("requestId").GetString(),
                ["writtenAtUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["documentTitle"] = documentTitle, ["sketchName"] = name,
                ["coordinateSpace"] = "sketch; mm", ["endpointPositionToleranceMm"] = 1e-6,
                ["pointTypeEnum"] = "Internal=0, User=1, Spline=2, Datum=3, VirtualSharp=4, Parabola=5, MidPoint=6, FramePoint=7, Origin=8, Ellipse=9, External=10, Unknown=-1; refleksi PIA SW2025 SP01.1",
                ["segments"] = segments.Select((x,i) => Attempt("segment " + i, () => Describe(x))).ToArray(),
                ["points"] = (sketch.GetSketchPoints2() as object[] ?? Array.Empty<object>()).Select((x,i) => Attempt("point " + i, () => Describe(x))).ToArray(),
                ["relationCountReported"] = manager.GetRelationsCount(0), ["relations"] = relationRows,
                ["unexplainedCandidates"] = candidates, ["errors"] = errors,
            };
            Directory.CreateDirectory(folder);
            // Nama output dari request berupa indeks, bukan nama sketsa/path masukan.
            var names = config.GetProperty("sketchNames").EnumerateArray().Select(x => x.GetString()).ToList();
            var output = Path.Combine(folder, "sketch-" + names.IndexOf(name).ToString(CultureInfo.InvariantCulture) + ".json");
            File.WriteAllText(output, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
    }
}
