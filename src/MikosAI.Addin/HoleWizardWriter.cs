using System;
using System.Collections.Generic;
using System.Linq;
using MikosAI.Core.Plan;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MikosAI.Addin
{
    /// <summary>
    /// Membuat lubang Hole Wizard lewat FEATURE DEFINITION, bukan HoleWizard5.
    ///
    /// KENAPA BUKAN HoleWizard5.
    ///
    /// HoleWizard5 punya dua belas parameter Double bernama Value1..Value12
    /// yang artinya BERGANTUNG TIPE LUBANG, dan namanya tidak memberi petunjuk
    /// apa pun. Itu perangkap urutan parameter yang paling buruk di seluruh API
    /// ini.
    ///
    /// IWizardHoleFeatureData2.InitializeHole mengisi seluruh dimensi DARI
    /// BASIS DATA Hole Wizard, jadi kedua belas Value itu tidak perlu disentuh
    /// sama sekali. Menghilangkan sumber kesalahan lebih baik daripada menambah
    /// test untuk mendeteksinya.
    ///
    /// Keuntungan kedua: pembacaan ulang lewat NAMA. Standard, FastenerType,
    /// dan FastenerSize bertipe String, jadi verifikasi membandingkan "ISO" dan
    /// "M6" apa adanya alih-alih menebak apakah indeks 135 memang yang
    /// dimaksud.
    ///
    /// TANPA FALLBACK. Kalau jalur ini gagal, hasilnya kegagalan yang
    /// disebutkan - bukan diam-diam pindah ke HoleWizard5. Jatuh antarjalur
    /// akan menghasilkan lubang dengan dimensi yang salah dan lolos sebagai
    /// sukses.
    ///
    /// APA YANG BELUM TERBUKTI. Seluruh kelas ini disusun dari TANDA TANGAN
    /// PIA, bukan dari semantik yang pernah dijalankan. Kami sudah dua kali
    /// tertipu dengan menyimpulkan yang kedua dari yang pertama - CreatePoint
    /// dan GetNextDisplayDimension. Karena itu setiap asumsi di bawah dibungkus
    /// Require dengan pesan yang MENYEBUT asumsinya, supaya run pertama di
    /// SolidWorks memberi tahu kami mana yang salah alih-alih menghasilkan
    /// lubang yang keliru.
    /// </summary>
    internal sealed class HoleWizardWriter
    {
        /// <summary>
        /// Pemetaan nama yang diketik manusia ke enum swconst.
        ///
        /// Memakai SIMBOL, bukan angka. Dengan begitu COMPILER yang menjamin
        /// nama enumnya ada: kalau SolidWorks menghapus atau mengganti nama
        /// anggotanya, build gagal - bukan menghasilkan indeks yang menunjuk
        /// standar lain diam-diam.
        ///
        /// Angka di komentar hanya untuk pembaca; yang mengikat simbolnya.
        /// </summary>
        private static readonly Dictionary<string, (swWzdHoleStandards_e Standar, swWzdHoleStandardFastenerTypes_e Fastener)> Pemetaan =
            new Dictionary<string, (swWzdHoleStandards_e, swWzdHoleStandardFastenerTypes_e)>(StringComparer.Ordinal)
            {
                ["ISO"] = (swWzdHoleStandards_e.swStandardISO, swWzdHoleStandardFastenerTypes_e.swStandardISOHexBolt),            // 8, 135
                ["ANSI Metric"] = (swWzdHoleStandards_e.swStandardAnsiMetric, swWzdHoleStandardFastenerTypes_e.swStandardAnsiMetricHexBolt), // 1, 29
                ["DIN"] = (swWzdHoleStandards_e.swStandardDIN, swWzdHoleStandardFastenerTypes_e.swStandardDINHexBolt),            // 4, 63
                ["JIS"] = (swWzdHoleStandards_e.swStandardJIS, swWzdHoleStandardFastenerTypes_e.swStandardJISHexBolt),            // 9, 152
            };

        private readonly ISldWorks _app;
        private readonly HostDiagnostics _diagnostics;

        internal HoleWizardWriter(ISldWorks app, HostDiagnostics diagnostics)
        {
            _app = app;
            _diagnostics = diagnostics;
        }

        private static void Require(bool ok, string pesan)
        {
            if (!ok) throw new InvalidOperationException(pesan);
        }

        private static swWzdGeneralHoleTypes_e TipeUmum(string holeType)
        {
            switch (holeType)
            {
                case "counterbore": return swWzdGeneralHoleTypes_e.swWzdCounterBore;   // 0
                case "simple": return swWzdGeneralHoleTypes_e.swWzdHole;               // 2
                default:
                    // HoleSpec sudah menyaring ini; kalau sampai di sini berarti
                    // daftar di Core dan di sini sudah menyimpang.
                    throw new InvalidOperationException(
                        $"holeType '{holeType}' tidak dipetakan di writer walau lolos HoleSpec - "
                        + "daftar di Core dan di Addin menyimpang.");
            }
        }

        /// <summary>
        /// Membuat satu fitur Hole Wizard pada face yang SUDAH terpilih.
        ///
        /// Pemilihan face sengaja ditinggalkan di pemanggil: ia yang tahu face
        /// mana, dan menyerahkannya ke sini akan menggandakan aturan EntityRef
        /// di dua tempat.
        /// </summary>
        internal HasilLubang Buat(IModelDoc2 model, HoleSpec spec)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            if (spec is null) throw new ArgumentNullException(nameof(spec));

            var definisi = model.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmHoleWzd) as IWizardHoleFeatureData2;
            Require(definisi is not null,
                "CreateDefinition(swFmHoleWzd) tidak mengembalikan IWizardHoleFeatureData2. "
                + "ASUMSI YANG PATAH: jalur feature-definition tersedia untuk Hole Wizard.");

            var standar = swWzdHoleStandards_e.swStandardISO;
            var fastener = swWzdHoleStandardFastenerTypes_e.swStandardISOHexBolt;

            if (spec.HoleType != "simple")
            {
                Require(Pemetaan.ContainsKey(spec.Standard!),
                    $"Standar '{spec.Standard}' lolos HoleSpec tapi tidak ada di pemetaan writer - "
                    + "daftar di Core dan di Addin menyimpang.");
                (standar, fastener) = Pemetaan[spec.Standard!];
            }

            // TIDAK ADA AccessSelections DI JALUR BUAT. Ini diketahui dengan
            // mahal.
            //
            // Versi pertama memanggil AccessSelections pada definisi yang baru
            // dibuat, dengan alasan "kalau tidak dibutuhkan ia tidak
            // merugikan". Alasan itu SALAH: SolidWorks mati seketika, tanpa
            // exception, tanpa entri di crash.log kami, dan tanpa Event Log
            // Windows - structured exception native yang tidak lewat CLR sama
            // sekali.
            //
            // Yang memberi tahu kami jurnal SolidWorks sendiri:
            //
            //   auFeatureDataObject_c::disconnectFromCoreObject
            //   auHoleSeriesFeatureData_c::GetThisClass
            //
            // Crash terjadi saat objek feature-data DILEPAS. Access/Release
            // adalah pasangan untuk MENGEDIT definisi fitur yang SUDAH ADA -
            // didapat lewat IFeature.GetDefinition. Definisi yang baru dibuat
            // CreateDefinition belum terhubung ke objek inti mana pun, jadi
            // melepasnya berarti memutus sesuatu yang tidak pernah tersambung.
            //
            // Jalur buat: CreateDefinition -> InitializeHole -> CreateFeature.
            // Titik.
            try
            {
                definisi!.InitializeHole(
                    (int)TipeUmum(spec.HoleType),
                    (int)standar,
                    (int)fastener,
                    spec.FastenerSize ?? string.Empty,
                    (int)swEndConditions_e.swEndCondThroughAll);   // 1
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    "InitializeHole melempar. ASUMSI YANG PATAH: InitializeHole menerima "
                    + $"(tipe, standar, fastener, ukuran, endCondition) apa adanya. Pesan: {e.Message}", e);
            }

            var fitur = model.FeatureManager.CreateFeature(definisi) as IFeature;

            Require(fitur is not null,
                "CreateFeature tidak menghasilkan fitur. ASUMSI YANG PATAH: face yang "
                + "terpilih cukup sebagai masukan posisi Hole Wizard.");

            Require(model.EditRebuild3(), "Rebuild gagal setelah Hole Wizard.");
            Require(fitur!.GetErrorCode2(out _) == 0, "Fitur Hole Wizard melaporkan rebuild error.");

            return BacaUlangFitur(model, fitur!, spec);
        }

        /// <summary>
        /// Membaca ulang fitur dari model - bukan dari apa yang kita kira sudah
        /// ditulis.
        ///
        /// Nilai yang dibaca di sini SENGAJA tidak dibandingkan dengan angka
        /// harapan apa pun: diameter counterbore untuk ISO M6 ditentukan basis
        /// data Hole Wizard, bukan oleh kami, dan menuliskan angkanya berarti
        /// menyalin keluaran tool lalu menyebutnya harapan. Yang dibandingkan
        /// pemanggil: standar dan ukuran yang diminta, jumlah lubang, dan arah
        /// perubahan volume.
        /// </summary>
        internal HasilLubang BacaUlangFitur(IModelDoc2 model, IFeature fitur, HoleSpec spec)
        {
            var data = fitur.GetDefinition() as IWizardHoleFeatureData2;
            Require(data is not null,
                "GetDefinition tidak mengembalikan IWizardHoleFeatureData2 pada fitur yang baru dibuat. "
                + "ASUMSI YANG PATAH: fitur hasil CreateFeature bisa dibaca balik sebagai data Hole Wizard.");

            // Juga TANPA AccessSelections. Properti di bawah hanya DIBACA,
            // dan pasangan Access/Release ada untuk mengubah - bukan untuk
            // melihat. Setelah apa yang terjadi di jalur buat, memanggilnya
            // "untuk berjaga-jaga" bukan kehati-hatian melainkan cara lain
            // menjatuhkan SolidWorks.
            //
            // Kalau ternyata GetSketchPoints memang menuntut akses, ia akan
            // mengembalikan null atau kosong - dan itu ditangkap pemanggil
            // sebagai jumlah titik yang tidak cocok, bukan sebagai proses yang
            // mati.
            try
            {
                var titik = new List<Titik3D>();
                var jumlah = data!.GetSketchPointCount();

                if (data.GetSketchPoints() is object[] points)
                {
                    foreach (var p in points)
                    {
                        if (p is ISketchPoint sp)
                        {
                            // Dilaporkan APA ADANYA dalam meter -> mm. Transformasi
                            // balik ke koordinat model dilakukan pemanggil, yang
                            // memegang sketsanya; menebaknya di sini berarti
                            // mengasumsikan sketsa posisi sejajar Front Plane.
                            titik.Add(new Titik3D(sp.X * 1000.0, sp.Y * 1000.0, sp.Z * 1000.0));
                        }
                    }
                }

                return new HasilLubang
                {
                    FeatureName = fitur.Name,
                    Standard = data.Standard,
                    FastenerType = data.FastenerType,
                    FastenerSize = data.FastenerSize,
                    CounterBoreDiameterMm = data.CounterBoreDiameter * 1000.0,
                    CounterBoreDepthMm = data.CounterBoreDepth * 1000.0,
                    ThruHoleDiameterMm = data.ThruHoleDiameter * 1000.0,
                    SketchPointCount = jumlah,
                    PositionsSketchSpaceMm = titik,
                    Requested = spec,
                };
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    "Gagal membaca ulang data lubang. ASUMSI YANG PATAH: properti "
                    + $"IWizardHoleFeatureData2 bisa dibaca tanpa AccessSelections. Pesan: {e.Message}", e);
            }
        }
    }

    /// <summary>
    /// Hasil pembacaan ulang satu fitur Hole Wizard.
    ///
    /// Posisi dilaporkan dalam RUANG SKETSA, dan namanya mengatakan itu.
    /// Menamainya "positions" saja akan membuat pemanggil mengira ia koordinat
    /// model - kesalahan yang persis sama dengan yang kami buat di CreatePoint.
    /// </summary>
    internal sealed class HasilLubang
    {
        internal string FeatureName { get; set; } = string.Empty;
        internal string? Standard { get; set; }
        internal string? FastenerType { get; set; }
        internal string? FastenerSize { get; set; }
        internal double CounterBoreDiameterMm { get; set; }
        internal double CounterBoreDepthMm { get; set; }
        internal double ThruHoleDiameterMm { get; set; }
        internal int SketchPointCount { get; set; }
        internal IReadOnlyList<Titik3D> PositionsSketchSpaceMm { get; set; } = Array.Empty<Titik3D>();
        internal HoleSpec? Requested { get; set; }
    }
}
