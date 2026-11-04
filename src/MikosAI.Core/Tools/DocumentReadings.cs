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
        /// <summary>
        /// Sketsa induk fitur ini. ARRAY, bukan nilai tunggal.
        ///
        /// Alasannya berlaku sekarang, bukan antisipasi loft/sweep:
        /// <c>IFeature.GetParents()</c> mengembalikan array. Bentuk tunggal
        /// berarti MEMBUANG data yang sudah dikembalikan SolidWorks agar muat ke
        /// bentuk yang lebih sempit - dan saat kita menemukan bentuknya salah,
        /// datanya sudah tidak ada untuk membuktikannya.
        ///
        /// GetParents() memuat SEMUA parent, termasuk plane dan fitur, jadi
        /// isinya wajib difilter per tipe sebelum sampai ke sini.
        ///
        /// null = BELUM DIBACA. Kosong = sudah dibaca, memang tidak ada.
        /// </summary>
        public IReadOnlyList<string>? ParentSketches { get; set; }
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
        ///
        /// Catatan ini sudah ada sejak awal, tapi SELAMA INI DILANGGAR tanpa
        /// disadari: SolidWorks selalu mengembalikan massa, memakai densitas
        /// default 1000 kg/m3 kalau material belum di-assign, dan status
        /// perhitungannya tetap 0 karena perhitungannya sendiri berhasil.
        /// Jadi "status sukses" bukan bukti material ada.
        /// </summary>
        public double? MassGrams { get; set; }

        /// <summary>
        /// Nama material yang di-assign, atau null kalau tidak ada.
        ///
        /// Ini BUKTI untuk klaim <see cref="DensityAssigned"/>, bukan hiasan.
        /// Tanpa nama materialnya, klaim bahwa massa bisa dipercaya tidak bisa
        /// diperiksa siapa pun.
        /// </summary>
        public string? MaterialName { get; set; }

        /// <summary>
        /// Densitas yang BENAR-BENAR dipakai, diturunkan dari massa dibagi
        /// volume. Disertakan supaya angkanya bisa diaudit.
        ///
        /// Inilah yang membongkar bug-nya: volume 100266 mm3 dengan massa
        /// 100,27 g berarti densitas 1000 kg/m3 - air, bukan baja. Kalau
        /// densitasnya ikut dilaporkan sejak awal, kekeliruannya akan terlihat
        /// dalam sekali pandang.
        /// </summary>
        public double? DensityKgM3 { get; set; }

        /// <summary>
        /// true  = material di-assign, massa bisa dipercaya
        /// false = material belum di-assign, massa TIDAK dihitung
        /// null  = tidak bisa ditanyakan untuk tipe dokumen ini
        ///
        /// Keadaan ketiga itu bukan kerewelan. Assembly tidak punya satu
        /// material tunggal - materialnya per komponen - jadi menjawab true
        /// berarti mengklaim sesuatu yang tidak diperiksa, dan menjawab false
        /// berarti menolak massa yang mungkin sah. "Tidak tahu" adalah satu-
        /// satunya jawaban yang benar.
        /// </summary>
        public bool? DensityAssigned { get; set; }
    }

    /// <summary>
    /// Status constraint sketsa, dinormalkan dari nilai
    /// <c>ISketch.GetConstrainedStatus</c>.
    /// </summary>
    public enum SketchConstraintStatus
    {
        /// <summary>Tidak bisa ditentukan. Diperlakukan sebagai masalah, bukan diabaikan.</summary>
        Unknown = 0,

        /// <summary>swUnderConstrained.</summary>
        UnderDefined = 1,

        /// <summary>swFullyConstrained.</summary>
        FullyDefined = 2,

        /// <summary>swOverConstrained - terlalu banyak constraint, sketsa tidak sehat.</summary>
        OverDefined = 3,

        /// <summary>swNoSolution - solver tidak menemukan solusi.</summary>
        NoSolution = 4,

        /// <summary>swInvalidSolution - ada solusi, tapi tidak sah.</summary>
        InvalidSolution = 5,
    }

    public sealed class SketchReading
    {
        public string Name { get; set; } = string.Empty;
        public SketchConstraintStatus ConstrainedStatus { get; set; } = SketchConstraintStatus.Unknown;
        public int EntityCount { get; set; }

        /// <summary>
        /// Fitur yang memakai sketsa ini sebagai profil, path, atau posisi.
        ///
        /// BERBEDA dari <see cref="Dependents"/>. "Tidak dipakai fitur apa pun"
        /// adalah alasan yang masuk akal untuk MENGHAPUS sebuah sketsa, jadi
        /// yang dipakai <c>delete_feature</c> nanti adalah Dependents - bukan
        /// ini.
        ///
        /// null = BELUM DIBACA. Kosong = sudah dibaca, memang tidak dikonsumsi.
        /// </summary>
        public IReadOnlyList<string>? ConsumedByFeatures { get; set; }

        /// <summary>
        /// SELURUH children, tanpa filter - termasuk sketsa lain yang sekadar
        /// punya relasi ke geometri sketsa ini.
        ///
        /// <c>ISketch</c> tidak punya method konsumen sama sekali; keduanya
        /// diturunkan dari <c>IFeature.GetChildren()</c> pada node fitur sketsa.
        /// Itu sebabnya field ini tidak pernah terisi sejak awal - pembacanya
        /// memang tidak pernah punya aksesnya.
        /// </summary>
        public IReadOnlyList<string>? Dependents { get; set; }

        /// <summary>
        /// Dimensi yang MENGUNCI geometri. <c>swDimensionDriving = 2</c>.
        /// </summary>
        public int? DrivingDimensionCount { get; set; }

        /// <summary>
        /// Dimensi reference - tidak mengunci apa pun. <c>swDimensionDriven = 1</c>.
        /// </summary>
        public int? DrivenDimensionCount { get; set; }

        /// <summary>
        /// <c>swDimensionDrivenUnknown = 0</c>. SolidWorks PUNYA keadaan ini dan
        /// ia nyata, jadi ia punya embernya sendiri.
        ///
        /// Dibaca lewat <c>IDimension.DrivenState</c>, BUKAN
        /// <c>IDisplayDimension.IsReferenceDim()</c> yang bertipe Boolean dan
        /// tidak bisa menyatakan "tidak tahu". Memakai yang boolean akan memaksa
        /// setiap dimensi Unknown masuk ke salah satu ember - masalah
        /// dua-keadaan yang sama dengan modelChanged.
        /// </summary>
        public int? UnknownDrivenStateCount { get; set; }

        /// <summary>
        /// Jumlah dimensi yang BERHASIL dibaca.
        ///
        /// Ada supaya invariant driving + driven + unknown = dimensionCountRead
        /// bisa diperiksa dari luar. Tanpa angka ini, dimensi yang gagal dibaca
        /// akan lenyap tanpa jejak dari ketiga ember di atas.
        /// </summary>
        public int? DimensionCountRead { get; set; }
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
