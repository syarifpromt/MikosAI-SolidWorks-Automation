using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MikosAI.Core.Tools;
using SolidWorks.Interop.sldworks;

namespace MikosAI.Addin
{
    /// <summary>Dipanggil hanya oleh SolidWorksReader pada dispatcher utama.</summary>
    internal static class SketchConnectivityReader
    {
        internal static void Read(ISketch sketch, SketchReading reading)
        {
            var endpoints = new List<SketchConnectivity.Endpoint>();
            var skipped = new List<string>();
            var limits = new HashSet<string>(StringComparer.Ordinal);
            var lineEnds = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var segments = sketch.GetSketchSegments() as object[] ?? Array.Empty<object>();
            foreach (var item in segments)
            {
                if (item is not ISketchSegment segment)
                    throw new InvalidOperationException("Entitas sketsa bukan ISketchSegment.");
                if (segment.ConstructionGeometry)
                {
                    continue;
                }
                var key = SegmentId(segment);
                // Accessor ujung berbeda per tipe. Hanya garis yang dibuktikan
                // oleh fixture ini; kurva lain disebut, bukan dianggap bersih.
                if (segment.GetType() != 0 || item is not ISketchLine line)
                {
                    skipped.Add(key);
                    continue;
                }
                var start = Point((object)line.GetStartPoint2());
                var end = Point((object)line.GetEndPoint2());
                endpoints.Add(start);
                endpoints.Add(end);
                lineEnds.Add(key, new[] { start.Id, end.Id });
            }
            reading.SegmentsNotAnalyzed = skipped;
            var endpointIds = new HashSet<string>(endpoints.Select(p => p.Id), StringComparer.Ordinal);
            var contacts = new HashSet<string>(StringComparer.Ordinal);
            var manager = sketch.RelationManager;
            if (manager is null) throw new InvalidOperationException("RelationManager tidak terbaca.");
            // swSketchRelationFilterType_e.swAll = 0, direfleksikan dari PIA.
            var relations = manager.GetRelations(0) as object[] ?? Array.Empty<object>();
            if (relations.Length != manager.GetRelationsCount(0))
                throw new InvalidOperationException("Daftar relasi tidak lengkap.");
            foreach (var item in relations)
            {
                if (item is not ISketchRelation relation)
                    throw new InvalidOperationException("Entitas relasi tidak terbaca.");
                var kind = relation.GetRelationType();
                if (kind == 13) limits.Add("at_intersect_not_analyzed");
                if (kind != 9) continue; // swConstraintType_COINCIDENT
                var entities = relation.GetEntities() as object[];
                var types = relation.GetEntitiesType() as int[];
                if (entities is null || types is null || entities.Length != types.Length)
                    throw new InvalidOperationException("Entitas relasi Coincident tidak lengkap.");
                for (var i = 0; i < entities.Length; i++)
                {
                    if (types[i] != 2 || entities[i] is not ISketchPoint point) continue;
                    // GetID hanya unik dalam satu sketsa. Jangan mengklasifikasi
                    // titik sketsa lain yang kebetulan mempunyai ID sama.
                    if (!Equals(point.GetSketch(), sketch)) continue;
                    var id = Id((object)point.GetID());
                    for (var j = 0; j < entities.Length; j++)
                    {
                        if (types[j] < 3 || types[j] > 7 || entities[j] is not ISketchSegment target) continue;
                        if (target.ConstructionGeometry || !Equals(target.GetSketch(), sketch)) continue;
                        if (!lineEnds.TryGetValue(SegmentId(target), out var ends)) continue;
                        if (ends.Contains(id)) continue; // ujung target, bukan badan
                        if (endpointIds.Contains(id)) contacts.Add(id);
                        // Bukan endpoint menurut ID belum berarti berada di
                        // badan segmen. Temuan lama ditelusuri di dump privat;
                        // ID limit ini belum disetujui dan tidak diekspos lagi.
                    }
                }
            }
            reading.Limits = limits.OrderBy(x => x, StringComparer.Ordinal).ToList();
            // Angka parsial akan terlihat seperti ukuran seluruh sketsa.
            if (skipped.Count == 0) SketchConnectivity.Populate(reading, endpoints, contacts);
        }

        private static SketchConnectivity.Endpoint Point(object value)
        {
            if (value is not ISketchPoint point)
                throw new InvalidOperationException("Ujung garis tidak terbaca.");
            var result = new SketchConnectivity.Endpoint
            {
                Id = Id((object)point.GetID()), X = point.X * 1000, Y = point.Y * 1000, Z = point.Z * 1000,
            };
            if (new[] { result.X, result.Y, result.Z }.Any(x => double.IsNaN(x) || double.IsInfinity(x)))
                throw new InvalidOperationException("Koordinat ujung tidak valid.");
            return result;
        }

        private static string SegmentId(ISketchSegment segment)
            => segment.GetType().ToString(CultureInfo.InvariantCulture) + ":" + Id((object)segment.GetID());

        private static string Id(object value)
        {
            if (value is not Array array || array.Length != 2)
                throw new InvalidOperationException("GetID tidak mengembalikan pasangan ID.");
            return Convert.ToInt32(array.GetValue(0), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                + ":" + Convert.ToInt32(array.GetValue(1), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }
    }
}
