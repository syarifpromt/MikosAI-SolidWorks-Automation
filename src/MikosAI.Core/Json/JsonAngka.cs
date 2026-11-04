using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikosAI.Core.Json
{
    /// <summary>
    /// Menulis <see cref="double"/> seperti yang dimaksud manusia, bukan seperti
    /// yang dilihat prosesor.
    ///
    /// KENAPA ADA.
    ///
    /// System.Text.Json di .NET Framework menulis double dengan G17 - 17 angka
    /// penting, selalu. Di .NET Core ia memakai bentuk round-trip TERPENDEK.
    /// Kita menargetkan net48 karena add-in harus hidup di dalam SLDWORKS.exe,
    /// jadi kita dapat perilaku yang pertama.
    ///
    /// Akibatnya terlihat di pembacaan sungguhan dari SolidWorks:
    ///
    ///     "surfaceAreaMm2": 24259.173191999998,
    ///     "centerOfMass": { "y": -9.9718389999999992 }
    ///
    /// Angka-angka itu SUDAH dibulatkan ke mikrometer oleh ReadOnlyToolShaper.
    /// Pembulatannya benar; yang menambahkan ekornya adalah penulis JSON-nya.
    ///
    /// Itu bukan soal kosmetik. Alasan pembulatannya ditulis sejak awal adalah
    /// karena angka seperti -9,9718389999999992 membuat model berpikir ada
    /// ketidaktepatan pengukuran padahal tidak ada - dan model yang menyimpulkan
    /// itu akan melaporkannya ke pengguna sebagai temuan.
    ///
    /// KENAPA LEWAT DECIMAL, BUKAN ToString("R").
    ///
    /// "R" menghasilkan bentuk terpendek, tapi di .NET Framework ia punya cacat
    /// lama: untuk sebagian nilai ia TIDAK round-trip. Memakainya di konverter
    /// umum berarti menukar keluaran jelek dengan keluaran salah.
    ///
    /// Konversi ke decimal memakai 15 angka penting, yang persis membuang ekor
    /// representasi biner tanpa menyentuh angka yang kita maksud. Nilai di luar
    /// jangkauan decimal - dan NaN serta infinity - dilewatkan ke penulis
    /// bawaan, supaya tidak ada angka yang dikarang demi kerapian.
    /// </summary>
    public sealed class DoubleRapiConverter : JsonConverter<double>
    {
        /// <summary>Batas aman decimal, dengan margin.</summary>
        private const double BatasDecimal = 7.9e28;

        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetDouble();

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                // JSON tidak punya NaN maupun infinity. Penulis bawaan melempar
                // di sini, dan perilaku itu DIPERTAHANKAN: angka yang tidak bisa
                // diwakili harus jadi kegagalan yang terlihat, bukan nol yang
                // tampak masuk akal.
                writer.WriteNumberValue(value);
                return;
            }

            if (value > -BatasDecimal && value < BatasDecimal)
            {
                writer.WriteNumberValue((decimal)value);
                return;
            }

            // Di luar jangkauan decimal, G17 adalah satu-satunya pilihan yang
            // pasti round-trip. Jelek, tapi benar - dan ukuran sebesar itu tidak
            // muncul di geometri CAD.
            writer.WriteRawValue(value.ToString("G17", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Opsi serialisasi bersama.
    ///
    /// Konverter di atas hanya berguna kalau ia dipakai di SETIAP tempat angka
    /// ditulis. Satu instance dibagi, bukan dibuat per panggilan: membuat
    /// JsonSerializerOptions berulang kali juga membatalkan cache metadata-nya.
    /// </summary>
    public static class JsonAngka
    {
        private static readonly JsonSerializerOptions Rapi = Buat(indent: false);
        private static readonly JsonSerializerOptions RapiIndent = Buat(indent: true);

        /// <summary>Untuk SerializeToNode dan serialisasi padat.</summary>
        public static JsonSerializerOptions Opsi => Rapi;

        /// <summary>Untuk keluaran yang dibaca manusia dan model.</summary>
        public static JsonSerializerOptions OpsiIndent => RapiIndent;

        private static JsonSerializerOptions Buat(bool indent)
        {
            var opsi = new JsonSerializerOptions { WriteIndented = indent };
            opsi.Converters.Add(new DoubleRapiConverter());
            return opsi;
        }
    }
}
