using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MikosAI.Core.Json;
using MikosAI.Core.Mcp;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Angka ditulis seperti yang dimaksud manusia.
    ///
    /// BAGAIMANA INI DITEMUKAN.
    ///
    /// Pembacaan sungguhan dari SolidWorks mengembalikan:
    ///
    ///     "surfaceAreaMm2": 24259.173191999998,
    ///     "centerOfMass": { "y": -9.9718389999999992 }
    ///
    /// Padahal ReadOnlyToolShaper SUDAH membulatkan keduanya ke mikrometer.
    /// Pembulatannya benar; yang menambahkan ekornya adalah penulis JSON-nya:
    /// System.Text.Json di .NET Framework menulis double dengan G17, selalu 17
    /// angka penting. Di .NET Core ia memakai bentuk terpendek. Kita di net48
    /// karena add-in harus hidup di dalam SLDWORKS.exe.
    ///
    /// KENAPA INI DIUJI DI TINGKAT TEKS.
    ///
    /// Test yang memeriksa nilai di dalam Dictionary akan LULUS sepanjang hari
    /// dengan bug ini aktif: nilainya memang sudah bulat. Yang salah hanya
    /// terlihat setelah diserialisasi. Jadi test ini memeriksa string JSON yang
    /// benar-benar dikirim ke model.
    /// </summary>
    public class AngkaJsonRapiTests
    {
        /// <summary>
        /// Nilai-nilai ini diambil dari pembacaan SolidWorks sungguhan, dan
        /// masing-masing menghasilkan ekor G17 yang berbeda.
        /// </summary>
        [Theory]
        [InlineData(24259.173192, "24259.173192")]
        [InlineData(-9.971839, "-9.971839")]
        [InlineData(-13.526158, "-13.526158")]
        [InlineData(100266.145343, "100266.145343")]
        [InlineData(0.1, "0.1")]
        [InlineData(0, "0")]
        [InlineData(10, "10")]
        [InlineData(-0.000001, "-0.000001")]
        public void double_ditulis_tanpa_ekor_biner(double nilai, string diharapkan)
        {
            var json = JsonSerializer.Serialize(nilai, JsonAngka.Opsi);

            Assert.Equal(diharapkan, json);
        }

        [Fact]
        public void g17_memang_yang_merusaknya_jadi_test_di_atas_bukan_kebetulan()
        {
            // Menegaskan premisnya. Kalau suatu hari runtime-nya berubah dan G17
            // tidak lagi menghasilkan ekor, test di atas jadi tidak bermakna -
            // dan baris ini yang akan memberi tahu.
            Assert.Equal("24259.173191999998", (24259.173192).ToString("G17",
                System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Jalur yang sebenarnya: hasil tool -> teks yang dibaca model.
        ///
        /// SerializeToNode yang membakar teks angkanya, jadi memasang konverter
        /// hanya di ToJsonString TIDAK cukup - dan test ini yang membuktikannya.
        /// </summary>
        [Fact]
        public void hasil_tool_sampai_ke_model_tanpa_ekor_biner()
        {
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 100266.145343,
                SurfaceAreaMm2 = 24259.173191999998,
                CenterOfMass = new Vector3Reading
                {
                    X = -13.526158000000001,
                    Y = -9.9718389999999992,
                    Z = 5,
                },
                MassGrams = 782.076,
                MaterialName = "Plain Carbon Steel",
                DensityKgM3 = 7800,
                DensityAssigned = true,
            });

            var teks = TeksYangDibacaModel(hasil);

            Assert.Contains("\"surfaceAreaMm2\": 24259.173192", teks);
            Assert.Contains("\"volumeMm3\": 100266.145343", teks);
            Assert.Contains("\"y\": -9.971839", teks);
            Assert.Contains("\"x\": -13.526158", teks);
            Assert.Contains("\"densityKgM3\": 7800", teks);

            // Dan tidak ada satu pun ekor yang lolos.
            Assert.DoesNotContain("99999", teks);
            Assert.DoesNotContain("00000", teks);
        }

        [Fact]
        public void kotak_pembatas_sampai_ke_model_tanpa_ekor_biner()
        {
            var hasil = ReadOnlyToolShaper.BoundingBox(new BoundingBoxReading
            {
                Min = new Vector3Reading { X = -82.144261, Y = -46.50234, Z = 0 },
                Max = new Vector3Reading { X = 55.091944, Y = 26.558661, Z = 10 },
                BodyCount = 1,
            });

            var teks = TeksYangDibacaModel(hasil);

            Assert.DoesNotContain("99999", teks);
            Assert.DoesNotContain("00000", teks);

            // Ukuran = max - min. Pengurangan floating point inilah sumber ekor
            // paling umum, jadi ia diperiksa eksplisit.
            Assert.Contains("\"z\": 10", teks);
        }

        private static string TeksYangDibacaModel(MikosAI.Core.Execution.ToolResult hasil)
        {
            var node = McpRequestHandler.BuildToolCallResult(hasil);

            var isi = node["content"]!.AsArray()
                .Select(n => n!.AsObject())
                .First(o => (string?)o["type"] == "text");

            return (string)isi["text"]!;
        }
    }
}
