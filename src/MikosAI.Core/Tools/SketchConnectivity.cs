using System;
using System.Collections.Generic;
using System.Linq;

namespace MikosAI.Core.Tools
{
    /// <summary>
    /// Perhitungan pada satu sketsa. ID titik hanya berlaku dalam sketsa itu.
    /// Tiap kemunculan adalah ujung satu segmen non-konstruksi; titik bersama
    /// tetap memakai ID sama. Koordinat tidak menggabungkan titik berbeda.
    /// </summary>
    public static class SketchConnectivity
    {
        public const double ToleranceMm = 1e-6;

        public sealed class Endpoint
        {
            public string Id { get; set; } = string.Empty;
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }

        public static void Populate(SketchReading reading, IReadOnlyList<Endpoint> endpoints,
            IEnumerable<string> declaredContacts)
        {
            var groups = endpoints.GroupBy(e => e.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var contacts = new HashSet<string>(declaredContacts, StringComparer.Ordinal);
            if (contacts.Any(id => !groups.ContainsKey(id)))
                throw new ArgumentException("Kontak harus menunjuk ujung segmen yang dianalisis.");

            reading.OpenEndpointCount = groups.Count(g => g.Value.Count == 1);
            reading.BranchPointCount = groups.Count(g => g.Value.Count > 2);
            reading.OpenEndpointOnSegmentCount = contacts.Count(id => groups[id].Count == 1);
            reading.JoinedEndpointOnSegmentCount = contacts.Count(id => groups[id].Count > 1);
            reading.DeclaredEndpointOnSegmentCount = contacts.Count;
            reading.EndpointToleranceMm = ToleranceMm;

            // Hitung titik terbuka yang dekat dengan titik BERBEDA, bukan pasangan.
            // Titik berbeda yang tepat berimpit tetap belum tersambung menurut ID.
            var all = groups.Values.Select(g => g[0]).ToList();
            reading.NearlyCoincidentEndpointCount = groups.Values.Where(g => g.Count == 1)
                .Count(g => all.Any(other => other.Id != g[0].Id && Near(g[0], other)));
        }

        private static bool Near(Endpoint a, Endpoint b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz <= ToleranceMm * ToleranceMm;
        }
    }
}
