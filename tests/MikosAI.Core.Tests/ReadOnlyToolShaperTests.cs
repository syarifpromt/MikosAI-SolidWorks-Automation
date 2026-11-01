using System.Collections.Generic;
using System.Linq;
using MikosAI.Core.Execution;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Aturan yang menentukan apakah sebuah pembacaan model sehat atau
    /// mencurigakan. Semuanya diuji tanpa SolidWorks, karena logikanya memang
    /// tidak menyentuh CAD - itu alasan ia tinggal di Core.
    /// </summary>
    public class ReadOnlyToolShaperTests
    {
        // ====================================================================
        // get_active_document
        // ====================================================================

        [Fact]
        public void tidak_ada_dokumen_aktif_BUKAN_error()
        {
            // AI butuh tahu ini supaya bisa memutuskan membuat dokumen baru.
            // Mengembalikannya sebagai kegagalan membuat model menebak-nebak.
            var hasil = ReadOnlyToolShaper.ActiveDocument(new DocumentReading { HasDocument = false });

            Assert.True(hasil.Ok);
            Assert.Equal(Verdict.Pass, hasil.Verification!.Verdict);
            Assert.Equal(false, hasil.Data!["hasDocument"]);
            Assert.True(hasil.IsTaskSuccessful);
        }

        [Fact]
        public void dokumen_tanpa_path_hanya_Warn_opsional()
        {
            var hasil = ReadOnlyToolShaper.ActiveDocument(new DocumentReading
            {
                HasDocument = true,
                Title = "Part1",
                Path = null,
            });

            Assert.Equal(Verdict.Warn, hasil.Verification!.Verdict);
            Assert.Contains(hasil.Verification.Checks,
                c => c.Id == "document_has_path" && !c.Required);
        }

        // ====================================================================
        // list_features
        // ====================================================================

        [Fact]
        public void fitur_bererror_membuat_verdict_minimal_Warn()
        {
            // Tree yang punya error bukan tree yang sehat. AI tidak boleh
            // membangun di atasnya seolah semuanya baik-baik saja.
            var hasil = ReadOnlyToolShaper.FeatureTree(new FeatureTreeReading
            {
                Features = new List<FeatureReading>
                {
                    new FeatureReading { Name = "Boss-Extrude1" },
                    new FeatureReading { Name = "Cut-Extrude1", HasError = true },
                },
            });

            Assert.Equal(Verdict.Warn, hasil.Verification!.Verdict);
            Assert.Equal(1, hasil.Data!["errorCount"]);
            Assert.Contains("Cut-Extrude1",
                hasil.Verification.Checks.Single(c => c.Id == "no_feature_errors").Message);
        }

        [Fact]
        public void tree_bersih_lulus()
        {
            var hasil = ReadOnlyToolShaper.FeatureTree(new FeatureTreeReading
            {
                Features = new List<FeatureReading>
                {
                    new FeatureReading { Name = "Boss-Extrude1", TypeName = "Extrusion" },
                },
            });

            Assert.Equal(Verdict.Pass, hasil.Verification!.Verdict);
            Assert.Equal(1, hasil.Data!["featureCount"]);
            Assert.Equal(0, hasil.Data["errorCount"]);
        }

        [Fact]
        public void rebuild_tertunda_ditandai_karena_pembacaan_bisa_basi()
        {
            var hasil = ReadOnlyToolShaper.FeatureTree(new FeatureTreeReading
            {
                Features = new List<FeatureReading> { new FeatureReading { Name = "A" } },
                RebuildRequired = true,
            });

            Assert.Equal(Verdict.Warn, hasil.Verification!.Verdict);
            Assert.Contains(hasil.Verification.Checks, c => c.Id == "model_rebuilt");
        }

        // ====================================================================
        // get_bounding_box
        // ====================================================================

        [Fact]
        public void dokumen_kosong_tanpa_body_hanya_Warn()
        {
            // Dokumen baru memang belum punya solid. Itu fakta sah.
            var hasil = ReadOnlyToolShaper.BoundingBox(new BoundingBoxReading { BodyCount = 0 });

            Assert.Equal(Verdict.Warn, hasil.Verification!.Verdict);
            Assert.True(hasil.IsTaskSuccessful);
        }

        [Fact]
        public void nol_body_tapi_kotak_pembatas_bukan_nol_adalah_kontradiksi()
        {
            // Pembacaan yang saling bertentangan tidak boleh lolos sebagai
            // sekadar peringatan.
            var hasil = ReadOnlyToolShaper.BoundingBox(new BoundingBoxReading
            {
                BodyCount = 0,
                Min = new Vector3Reading { X = 0, Y = 0, Z = 0 },
                Max = new Vector3Reading { X = 100, Y = 80, Z = 10 },
            });

            Assert.Equal(Verdict.Fail, hasil.Verification!.Verdict);
            Assert.False(hasil.IsTaskSuccessful);
        }

        [Fact]
        public void ukuran_dihitung_dari_min_dan_max()
        {
            var hasil = ReadOnlyToolShaper.BoundingBox(new BoundingBoxReading
            {
                BodyCount = 1,
                Min = new Vector3Reading { X = -50, Y = -40, Z = 0 },
                Max = new Vector3Reading { X = 50, Y = 40, Z = 10 },
            });

            var ukuran = Assert.IsType<Dictionary<string, object?>>(hasil.Data!["size"]);

            Assert.Equal(100.0, ukuran["x"]);
            Assert.Equal(80.0, ukuran["y"]);
            Assert.Equal(10.0, ukuran["z"]);
            Assert.Equal(Verdict.Pass, hasil.Verification!.Verdict);
        }

        [Fact]
        public void ekor_floating_point_dari_konversi_meter_dibulatkan()
        {
            // 0.09999999999999999 m -> 99.99999999999999 mm membuat model
            // berpikir ada ketidaktepatan padahal tidak ada.
            var hasil = ReadOnlyToolShaper.BoundingBox(new BoundingBoxReading
            {
                BodyCount = 1,
                Min = new Vector3Reading(),
                Max = new Vector3Reading { X = 99.99999999999999, Y = 0, Z = 0 },
            });

            var ukuran = Assert.IsType<Dictionary<string, object?>>(hasil.Data!["size"]);

            Assert.Equal(100.0, ukuran["x"]);
        }

        // ====================================================================
        // get_mass_properties
        // ====================================================================

        [Fact]
        public void tanpa_material_massa_null_dan_ditandai_tidak_dikarang()
        {
            // Angka massa dipakai orang untuk keputusan teknik. Mengarangnya
            // dari densitas default adalah hal paling berbahaya yang bisa
            // dilakukan tool ini.
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 80000,
                SurfaceAreaMm2 = 20000,
                DensityAssigned = false,
                MassGrams = null,
            });

            Assert.Null(hasil.Data!["massGrams"]);
            Assert.Equal(Verdict.Warn, hasil.Verification!.Verdict);

            var check = hasil.Verification.Checks.Single(c => c.Id == "material_assigned");
            Assert.Contains("TIDAK dikarang", check.Message);
        }

        [Fact]
        public void material_ada_tapi_massa_tidak_terbaca_menjadi_Blocked()
        {
            // Pembacaan yang tidak konsisten berarti kita tidak tahu apa yang
            // sebenarnya terjadi - itu BLOCKED, bukan WARN.
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 80000,
                DensityAssigned = true,
                MassGrams = null,
            });

            Assert.Equal(Verdict.Blocked, hasil.Verification!.Verdict);
            Assert.False(hasil.IsTaskSuccessful);
        }

        [Fact]
        public void material_ada_dan_massa_terbaca_lulus()
        {
            var hasil = ReadOnlyToolShaper.MassProperties(new MassPropertiesReading
            {
                VolumeMm3 = 80000,
                DensityAssigned = true,
                MassGrams = 216.0,
            });

            Assert.Equal(Verdict.Pass, hasil.Verification!.Verdict);
            Assert.Equal(216.0, hasil.Data!["massGrams"]);
        }

        // ====================================================================
        // inspect_sketches
        // ====================================================================

        [Theory]
        [InlineData(SketchConstraintStatus.OverDefined)]
        [InlineData(SketchConstraintStatus.Dangling)]
        [InlineData(SketchConstraintStatus.NoSolution)]
        public void sketsa_rusak_menghasilkan_Fail_bukan_Warn(SketchConstraintStatus status)
        {
            // Membangun fitur di atas sketsa rusak menghasilkan geometri salah
            // atau rebuild yang gagal belakangan.
            var hasil = ReadOnlyToolShaper.Sketches(new List<SketchReading>
            {
                new SketchReading { Name = "Sketch1", ConstrainedStatus = status },
            });

            Assert.Equal(Verdict.Fail, hasil.Verification!.Verdict);
            Assert.False(hasil.IsTaskSuccessful);
        }

        [Fact]
        public void sketsa_under_defined_hanya_Warn_opsional()
        {
            // Under-defined itu sah saat perancangan.
            var hasil = ReadOnlyToolShaper.Sketches(new List<SketchReading>
            {
                new SketchReading { Name = "Sketch1", ConstrainedStatus = SketchConstraintStatus.UnderDefined },
            });

            Assert.Equal(Verdict.Warn, hasil.Verification!.Verdict);
            Assert.True(hasil.IsTaskSuccessful);
        }

        [Fact]
        public void status_sketsa_yang_tidak_terbaca_menjadi_Blocked()
        {
            // Tidak bisa dibaca bukan berarti sehat.
            var hasil = ReadOnlyToolShaper.Sketches(new List<SketchReading>
            {
                new SketchReading { Name = "Sketch1", ConstrainedStatus = SketchConstraintStatus.Unknown },
            });

            Assert.Equal(Verdict.Blocked, hasil.Verification!.Verdict);
        }

        [Fact]
        public void sketsa_fully_defined_lulus_dan_dihitung()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new List<SketchReading>
            {
                new SketchReading { Name = "A", ConstrainedStatus = SketchConstraintStatus.FullyDefined },
                new SketchReading { Name = "B", ConstrainedStatus = SketchConstraintStatus.FullyDefined },
            });

            Assert.Equal(Verdict.Pass, hasil.Verification!.Verdict);
            Assert.Equal(2, hasil.Data!["fullyDefinedCount"]);
        }

        [Fact]
        public void sketsa_rusak_mengalahkan_sketsa_sehat_di_dokumen_yang_sama()
        {
            var hasil = ReadOnlyToolShaper.Sketches(new List<SketchReading>
            {
                new SketchReading { Name = "A", ConstrainedStatus = SketchConstraintStatus.FullyDefined },
                new SketchReading { Name = "B", ConstrainedStatus = SketchConstraintStatus.OverDefined },
            });

            Assert.Equal(Verdict.Fail, hasil.Verification!.Verdict);
        }

        // ====================================================================
        // get_selection
        // ====================================================================

        [Fact]
        public void seleksi_kosong_adalah_fakta_sah()
        {
            var hasil = ReadOnlyToolShaper.Selection(new List<SelectionReading>());

            Assert.Equal(Verdict.Pass, hasil.Verification!.Verdict);
            Assert.Equal(0, hasil.Data!["count"]);
        }

        [Fact]
        public void permukaan_terpilih_menghasilkan_EntityRef_normal_and_point()
        {
            // Inilah yang membuat alur "klik permukaan, minta AI bikin lubang
            // di situ" bekerja tanpa menyentuh Face1.
            var hasil = ReadOnlyToolShaper.Selection(new List<SelectionReading>
            {
                new SelectionReading
                {
                    Kind = "face",
                    Centroid = new Vector3Reading { X = 0, Y = 0, Z = 10 },
                    Normal = new Vector3Reading { X = 0, Y = 0, Z = 1 },
                },
            });

            var items = Assert.IsType<List<Dictionary<string, object?>>>(hasil.Data!["items"]);
            var entityRef = Assert.IsType<Dictionary<string, object?>>(items[0]["entityRef"]);

            Assert.Equal("face", entityRef["kind"]);
            Assert.Equal("normal_and_point", entityRef["by"]);
            Assert.NotNull(entityRef["normal"]);
            Assert.NotNull(entityRef["nearPoint"]);
        }

        [Fact]
        public void bidang_terpilih_dirujuk_lewat_nama()
        {
            var hasil = ReadOnlyToolShaper.Selection(new List<SelectionReading>
            {
                new SelectionReading { Kind = "plane", Name = "Top Plane" },
            });

            var items = Assert.IsType<List<Dictionary<string, object?>>>(hasil.Data!["items"]);
            var entityRef = Assert.IsType<Dictionary<string, object?>>(items[0]["entityRef"]);

            Assert.Equal("name", entityRef["by"]);
            Assert.Equal("Top Plane", entityRef["name"]);
        }

        [Fact]
        public void permukaan_tanpa_normal_tidak_bisa_dirujuk_dan_itu_ditandai()
        {
            // Kalau tidak ditandai, AI akan mengira bisa memakainya.
            var hasil = ReadOnlyToolShaper.Selection(new List<SelectionReading>
            {
                new SelectionReading { Kind = "face", Centroid = new Vector3Reading() },
            });

            var items = Assert.IsType<List<Dictionary<string, object?>>>(hasil.Data!["items"]);

            Assert.Null(items[0]["entityRef"]);
            Assert.Contains(hasil.Verification!.Checks, c => c.Id == "selection_referenceable");
        }

        [Fact]
        public void entitas_tidak_dikenal_tidak_pernah_menghasilkan_EntityRef()
        {
            var hasil = ReadOnlyToolShaper.Selection(new List<SelectionReading>
            {
                new SelectionReading { Kind = "unknown", Name = "sesuatu" },
            });

            var items = Assert.IsType<List<Dictionary<string, object?>>>(hasil.Data!["items"]);
            Assert.Null(items[0]["entityRef"]);
        }

        [Fact]
        public void TIDAK_ADA_tool_read_only_yang_menuntut_bukti_baca_ulang()
        {
            // Read-only tidak menulis apa pun, jadi tidak ada yang perlu
            // dibuktikan lewat save-reopen. Kalau requiresEvidence salah di-set
            // true, semua tool ini akan BLOCKED.
            var semua = new[]
            {
                ReadOnlyToolShaper.ActiveDocument(new DocumentReading { HasDocument = false }),
                ReadOnlyToolShaper.FeatureTree(new FeatureTreeReading()),
                ReadOnlyToolShaper.Selection(new List<SelectionReading>()),
            };

            Assert.All(semua, h => Assert.NotEqual(Verdict.Blocked, h.Verification!.Verdict));
        }
    }
}
