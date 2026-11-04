using MikosAI.Core.Execution;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Massa tidak boleh dikarang dari densitas default.
    ///
    /// BAGAIMANA INI DITEMUKAN.
    ///
    /// Round-trip MCP terhadap SolidWorks sungguhan lulus 9/9, lalu angkanya
    /// diperiksa dengan tangan:
    ///
    ///     volumeMm3 = 100266,145 mm3  =  100,266 cm3
    ///     massGrams = 100,266 g
    ///     -> densitas = 1,00 g/cm3 = 1000 kg/m3
    ///
    /// Itu densitas AIR. Part-nya baja, yang 7800 kg/m3 - massanya seharusnya
    /// sekitar 782 g. Dan 1000 kg/m3 adalah densitas DEFAULT SolidWorks saat
    /// material belum di-assign.
    ///
    /// Padahal jawabannya memuat densityAssigned: true.
    ///
    /// Sebabnya: pembaca menyimpulkan "material ada" dari status perhitungan == 0
    /// dan massa > 0. Itu salah. SolidWorks SELALU mengembalikan massa, dan
    /// statusnya 0 karena perhitungannya sendiri memang berhasil - bukan karena
    /// materialnya ada.
    ///
    /// Catatan "JANGAN mengarang massa dari densitas default" sudah tertulis di
    /// MassPropertiesReading sejak awal. Ia dilanggar tanpa disadari karena tidak
    /// ada satu pun test yang menegakkannya.
    /// </summary>
    public class MassaTidakDikarangTests
    {
        [Fact]
        public void tanpa_material_massa_tidak_dilaporkan()
        {
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 100266.145343,
                SurfaceAreaMm2 = 24259.173192,
                CenterOfMass = new Vector3Reading { X = -13.5, Y = -10.0, Z = 5 },
                MassGrams = null,
                MaterialName = null,
                DensityKgM3 = null,
                DensityAssigned = false,
            });

            Assert.Null(hasil.Data!["massGrams"]);
            Assert.Null(hasil.Data["materialName"]);
            Assert.Null(hasil.Data["densityKgM3"]);
            Assert.Equal(false, hasil.Data["densityAssigned"]);

            // Volume dan area TETAP dilaporkan: keduanya tidak bergantung pada
            // material, dan keduanya pembacaan sungguhan.
            Assert.Equal(100266.145343, hasil.Data["volumeMm3"]);
            Assert.Equal(24259.173192, hasil.Data["surfaceAreaMm2"]);
        }

        [Fact]
        public void dengan_material_massa_dan_buktinya_dilaporkan()
        {
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 100266.145343,
                SurfaceAreaMm2 = 24259.173192,
                CenterOfMass = new Vector3Reading { X = -13.5, Y = -10.0, Z = 5 },
                MassGrams = 782.076,
                MaterialName = "Plain Carbon Steel",
                DensityKgM3 = 7800,
                DensityAssigned = true,
            });

            Assert.Equal(782.076, hasil.Data!["massGrams"]);
            Assert.Equal("Plain Carbon Steel", hasil.Data["materialName"]);
            Assert.Equal(7800d, hasil.Data["densityKgM3"]);
            Assert.Equal(true, hasil.Data["densityAssigned"]);

            Assert.Equal(Verdict.Pass, hasil.Verification!.Verdict);
            Assert.True(hasil.Verification.MayBeMarkedComplete);
        }

        /// <summary>
        /// Densitas ikut dilaporkan supaya angkanya bisa DIAUDIT.
        ///
        /// Inilah yang membongkar bug-nya, dan inilah yang akan membongkar
        /// kekeliruan serupa berikutnya dalam sekali pandang: 1000 kg/m3 pada
        /// part yang seharusnya baja langsung terlihat salah, sementara "massa
        /// 100,27 g" sendirian terdengar seperti pengukuran biasa.
        /// </summary>
        [Fact]
        public void densitas_dilaporkan_supaya_bisa_diaudit()
        {
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 100266.145343,
                MassGrams = 100.266145,
                MaterialName = "Water-ish default",
                DensityKgM3 = 1000,
                DensityAssigned = true,
            });

            // Nilainya ADA dan bisa dibandingkan dengan material yang diharapkan.
            Assert.Equal(1000d, hasil.Data!["densityKgM3"]);
        }

        /// <summary>
        /// Assembly: material TIDAK bisa ditanyakan, bukan tidak ada.
        ///
        /// Massanya tetap dilaporkan karena mungkin benar, tapi ditandai butuh
        /// tinjauan manusia. Angka massa dipakai untuk keputusan teknik, jadi
        /// kalau harus salah, lebih baik salah ke arah "jangan percaya buta".
        /// </summary>
        [Fact]
        public void assembly_melaporkan_massa_tapi_menuntut_tinjauan_manusia()
        {
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 500000,
                MassGrams = 3900,
                MaterialName = null,
                DensityKgM3 = 7800,
                DensityAssigned = null,
            });

            // Massanya dilaporkan - tidak disembunyikan.
            Assert.Equal(3900d, hasil.Data!["massGrams"]);
            Assert.Null(hasil.Data["densityAssigned"]);

            Assert.True(hasil.Verification!.ManualReviewRequired);
            Assert.False(hasil.Verification.MayBeMarkedComplete);
        }

        /// <summary>
        /// REDUNDANSI YANG DISENGAJA.
        ///
        /// Pembaca hidup di MikosAI.Addin dan tidak bisa diuji tanpa SolidWorks
        /// terpasang - itu sebabnya bug ini bertahan. Aturannya ditegakkan ulang
        /// di shaper, satu-satunya tempat yang bisa diuji otomatis.
        ///
        /// Pembacaan di bawah ini SENGAJA tidak konsisten: material tidak ada,
        /// tapi massanya terisi. Itu persis bentuk bug yang lama.
        /// </summary>
        [Fact]
        public void massa_ditahan_kalau_pembacanya_tidak_konsisten()
        {
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 100266.145343,
                MassGrams = 100.266145,        // dihitung dari densitas default
                MaterialName = null,
                DensityAssigned = false,       // ... padahal material tidak ada
            });

            // Angkanya TIDAK lolos.
            Assert.Null(hasil.Data!["massGrams"]);

            // Dan ia tidak hilang diam-diam: ketidakkonsistenannya dilaporkan,
            // karena bug yang tidak terlihat akan bertahan lama persis seperti
            // sebelumnya.
            var check = Assert.Single(
                hasil.Verification!.Checks,
                c => c.Id == "mass_consistent_with_material");

            Assert.Equal(Verdict.Blocked, check.Verdict);
            Assert.True(check.Required);

            // BLOCKED itu wajib, jadi hasilnya tidak boleh dianggap selesai.
            Assert.False(hasil.Verification.MayBeMarkedComplete);
        }
    }
}
