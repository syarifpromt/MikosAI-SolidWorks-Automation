using System;
using System.Collections.Generic;
using System.Linq;

namespace MikosAI.Core.Plan
{
    /// <summary>
    /// Permintaan lubang Hole Wizard yang sudah divalidasi.
    ///
    /// Seluruh penolakan di kelas ini punya satu alasan yang sama: JANGAN
    /// MENEBAK NILAI ENGINEERING. Standar lubang dan ukuran fastener menentukan
    /// apakah baut benar-benar masuk; memilihkannya untuk pengguna berarti
    /// mengarang keputusan teknik lalu menyajikannya sebagai hasil.
    ///
    /// Yang tidak diberikan pengguna menjadi penolakan, bukan default.
    /// </summary>
    public sealed class HoleSpec
    {
        /// <summary>
        /// Tipe yang DIBUKA di M0.
        ///
        /// Sengaja hanya dua. Tiap tipe lubang memetakan ke basis data Hole
        /// Wizard dengan cara berbeda, dan satu tipe yang terbukti benar tidak
        /// membuktikan apa pun tentang tipe lain. Tipe di luar daftar ini
        /// ditolak sebagai "belum terverifikasi", bukan dicoba-coba.
        /// </summary>
        public static readonly IReadOnlyList<string> TipeDibuka =
            Array.AsReadOnly(new[] { "simple", "counterbore" });

        /// <summary>
        /// Standar yang dikenal, dengan NAMA seperti yang diketik manusia.
        ///
        /// Pemetaan ke enum ada di MikosAI.Addin, memakai simbol swconst
        /// langsung sehingga COMPILER yang menjamin nama enumnya ada. Daftar di
        /// sini hanya pintu masuk - ia menolak yang tidak dikenal lebih awal,
        /// dengan pesan yang menyebut pilihan yang sah.
        /// </summary>
        public static readonly IReadOnlyList<string> StandarDikenal =
            Array.AsReadOnly(new[] { "ISO", "ANSI Metric", "DIN", "JIS" });

        public string HoleType { get; }
        public string? Standard { get; }
        public string? FastenerSize { get; }
        public IReadOnlyList<Titik3D> Positions { get; }

        private HoleSpec(string holeType, string? standard, string? fastenerSize, IReadOnlyList<Titik3D> positions)
        {
            HoleType = holeType;
            Standard = standard;
            FastenerSize = fastenerSize;
            Positions = positions;
        }

        public static HoleSpec Buat(
            string? holeType,
            string? standard,
            string? fastenerSize,
            IReadOnlyList<Titik3D>? positions)
        {
            if (string.IsNullOrWhiteSpace(holeType))
            {
                throw new ArgumentException("holeType wajib diisi.");
            }

            var tipe = holeType!.Trim().ToLowerInvariant();

            if (!TipeDibuka.Contains(tipe))
            {
                // Dibedakan dari "tipe tidak dikenal": countersink dan tapped
                // ADA di skema, tapi belum punya test urutan parameter yang
                // lulus di SolidWorks nyata.
                throw new ArgumentException(
                    $"holeType '{tipe}' belum terverifikasi. Yang dibuka di M0: "
                    + string.Join(", ", TipeDibuka)
                    + ". Tipe lain tetap ada di skema tapi ditolak sampai test-nya lulus.");
            }

            if (positions is null || positions.Count == 0)
            {
                throw new ArgumentException("positions wajib berisi minimal satu titik.");
            }

            // Counterbore menuntut standar dan ukuran; simple tidak, karena
            // diameternya dinyatakan langsung.
            if (tipe != "simple")
            {
                if (string.IsNullOrWhiteSpace(standard))
                {
                    throw new ArgumentException(
                        $"standard wajib untuk holeType '{tipe}'. Tidak ada default - "
                        + "memilihkan standar berarti mengarang keputusan teknik. "
                        + "Pilihan yang dikenal: " + string.Join(", ", StandarDikenal));
                }

                if (string.IsNullOrWhiteSpace(fastenerSize))
                {
                    throw new ArgumentException(
                        $"fastenerSize wajib untuk holeType '{tipe}', mis. 'M6'. Tidak ada default.");
                }

                if (!StandarDikenal.Contains(standard!.Trim(), StringComparer.Ordinal))
                {
                    throw new ArgumentException(
                        $"standard '{standard}' tidak dikenal. Yang dikenal: "
                        + string.Join(", ", StandarDikenal));
                }
            }

            foreach (var p in positions)
            {
                if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z) ||
                    double.IsInfinity(p.X) || double.IsInfinity(p.Y) || double.IsInfinity(p.Z))
                {
                    throw new ArgumentException("positions memuat koordinat yang bukan angka.");
                }
            }

            return new HoleSpec(
                tipe,
                string.IsNullOrWhiteSpace(standard) ? null : standard!.Trim(),
                string.IsNullOrWhiteSpace(fastenerSize) ? null : fastenerSize!.Trim(),
                positions);
        }
    }

    /// <summary>
    /// Titik dalam KOORDINAT MODEL, milimeter.
    ///
    /// Bukan koordinat sketsa. Perbedaannya menentukan: ISketchManager.CreatePoint
    /// menerima koordinat RUANG SKETSA, jadi nilai di sini wajib ditransformasi
    /// lewat ISketch.ModelToSketchTransform sebelum dipakai - dan hasil bacaan
    /// ulang ditransformasi balik lewat IMathTransform.Inverse sebelum
    /// dilaporkan.
    ///
    /// Di Front Plane keduanya kebetulan sama, jadi transform yang hilang tetap
    /// lulus di sana. Itu sebabnya test wajib memakai face yang BUKAN Front
    /// Plane.
    /// </summary>
    public readonly struct Titik3D
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public Titik3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
