using System;
using System.Collections.Generic;

namespace MikosAI.Core.Tools
{
    /// <summary>
    /// Data polos hasil pembacaan model SolidWorks.
    ///
    /// Tipe-tipe di sini TIDAK menyentuh SolidWorks sama sekali. Add-in membaca
    /// API dan mengisi tipe ini; Core membentuknya jadi ToolResult beserta
    /// verifikasinya.
    ///
    /// Pemisahan itu disengaja: aturan yang menentukan apakah sebuah pembacaan
    /// sehat atau mencurigakan - sketsa over-defined itu FAIL, fitur bererror
    /// itu WARN, bodyCount nol itu fakta sah - bisa diuji dalam hitungan detik
    /// tanpa membuka CAD. Yang tersisa di add-in hanya penerjemahan API ke data,
    /// yang memang hanya bisa diverifikasi di mesin bermesin SolidWorks.
    ///
    /// SATUAN: semua panjang dalam MILIMETER. API SolidWorks memakai meter;
    /// konversi terjadi di pembaca add-in, satu tempat saja.
    /// </summary>
    public sealed class DocumentReading
    {
        public bool HasDocument { get; set; }
        public string? Title { get; set; }
        public string? Path { get; set; }

        /// <summary>part / assembly / drawing / unknown</summary>
        public string? DocumentType { get; set; }

        public bool IsDirty { get; set; }
        public string? ActiveConfiguration { get; set; }
        public string? SolidWorksRevision { get; set; }
    }

    public sealed class FeatureReading
    {
        public string Name { get; set; } = string.Empty;
        public string? TypeName { get; set; }
        public bool IsSuppressed { get; set; }
        public bool HasError { get; set; }
        public string? ParentSketch { get; set; }
    }

    public sealed class FeatureTreeReading
    {
        public IReadOnlyList<FeatureReading> Features { get; set; } = Array.Empty<FeatureReading>();
        public bool RebuildRequired { get; set; }
    }

    public sealed class Vector3Reading
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public static Vector3Reading Zero => new Vector3Reading();
    }

    public sealed class BoundingBoxReading
    {
        public Vector3Reading Min { get; set; } = Vector3Reading.Zero;
        public Vector3Reading Max { get; set; } = Vector3Reading.Zero;
        public int BodyCount { get; set; }
    }

    public sealed class MassPropertiesReading
    {
        public double VolumeMm3 { get; set; }
        public double SurfaceAreaMm2 { get; set; }
        public Vector3Reading CenterOfMass { get; set; } = Vector3Reading.Zero;

        /// <summary>
        /// null kalau material belum di-assign. JANGAN diisi dari densitas
        /// default lalu disajikan sebagai fakta.
        /// </summary>
        public double? MassGrams { get; set; }

        public bool DensityAssigned { get; set; }
    }

    /// <summary>
    /// Status constraint sketsa, dinormalkan dari nilai
    /// <c>ISketch.GetConstrainedStatus</c>.
    /// </summary>
    public enum SketchConstraintStatus
    {
        /// <summary>Tidak bisa ditentukan. Diperlakukan sebagai masalah, bukan diabaikan.</summary>
        Unknown = 0,

        UnderDefined = 1,
        FullyDefined = 2,

        /// <summary>Terlalu banyak constraint - sketsa tidak sehat.</summary>
        OverDefined = 3,

        /// <summary>Ada relasi menggantung - sketsa tidak sehat.</summary>
        Dangling = 4,

        /// <summary>Tidak terselesaikan.</summary>
        NoSolution = 5,

        /// <summary>Kosong, belum ada geometri.</summary>
        Empty = 6,
    }

    public sealed class SketchReading
    {
        public string Name { get; set; } = string.Empty;
        public SketchConstraintStatus ConstrainedStatus { get; set; } = SketchConstraintStatus.Unknown;
        public int EntityCount { get; set; }
        public int DimensionCount { get; set; }

        /// <summary>Nama fitur yang mengkonsumsi sketsa ini, atau null.</summary>
        public string? ConsumedByFeature { get; set; }

        public bool HasOpenContour { get; set; }
    }

    /// <summary>
    /// Satu entitas yang sedang dipilih pengguna, beserta referensi semantik
    /// yang bisa langsung dipakai tool lain.
    ///
    /// Inilah yang membuat AI terasa berada DI DALAM SolidWorks: ia tahu apa
    /// yang sedang di-klik, dan referensinya stabil - bukan Face1/Edge1.
    /// </summary>
    public sealed class SelectionReading
    {
        /// <summary>face / edge / vertex / plane / feature / sketch / unknown</summary>
        public string Kind { get; set; } = "unknown";

        public string? TypeName { get; set; }

        /// <summary>Nama, kalau entitasnya punya nama (bidang, fitur, sketsa).</summary>
        public string? Name { get; set; }

        /// <summary>Titik tengah dalam mm, kalau bisa dihitung.</summary>
        public Vector3Reading? Centroid { get; set; }

        /// <summary>Normal permukaan, kalau entitasnya permukaan.</summary>
        public Vector3Reading? Normal { get; set; }
    }
}
