using System;
using System.Collections.Generic;
using System.Linq;
using MikosAI.Core.Execution;

namespace MikosAI.Core.Tools
{
    /// <summary>
    /// Membentuk hasil pembacaan model menjadi <see cref="ToolResult"/>, beserta
    /// verifikasinya.
    ///
    /// Semua tool di sini READ-ONLY, jadi <c>requiresEvidence: false</c> -
    /// tidak ada yang perlu dibuktikan dengan baca-ulang karena tidak ada yang
    /// ditulis. Tapi pemeriksaan tetap ada, untuk hal yang berbeda: menandai
    /// model yang TIDAK SEHAT.
    ///
    /// Aturan-aturan itulah yang dites di sini, bukan di mesin bermesin CAD.
    /// </summary>
    public static class ReadOnlyToolShaper
    {
        public const string CapabilityId = "document_inspection";

        // --- get_active_document --------------------------------------------

        /// <summary>
        /// Dokumen aktif.
        ///
        /// <c>hasDocument: false</c> BUKAN error. Itu fakta sah, dan AI butuh
        /// tahu supaya bisa memutuskan membuat dokumen baru. Mengembalikannya
        /// sebagai kegagalan akan membuat model menebak-nebak.
        /// </summary>
        public static ToolResult ActiveDocument(DocumentReading reading)
        {
            if (reading is null) throw new ArgumentNullException(nameof(reading));

            var data = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["hasDocument"] = reading.HasDocument,
                ["title"] = reading.Title,
                ["path"] = reading.Path,
                ["type"] = reading.DocumentType,
                ["isDirty"] = reading.IsDirty,
                ["activeConfiguration"] = reading.ActiveConfiguration,
                ["solidworksRevision"] = reading.SolidWorksRevision,
            };

            var checks = new List<VerificationCheck>
            {
                VerificationCheck.Pass("document_queried", reading.HasDocument),
            };

            // Dokumen tersimpan tanpa path itu ganjil, tapi bukan kegagalan.
            if (reading.HasDocument && string.IsNullOrEmpty(reading.Path))
            {
                checks.Add(new VerificationCheck
                {
                    Id = "document_has_path",
                    Verdict = Verdict.Warn,
                    Required = false,
                    Message = "Dokumen belum pernah disimpan, jadi tidak punya path.",
                });
            }

            return Sukses(data, checks);
        }

        // --- list_features ---------------------------------------------------

        /// <summary>
        /// Isi FeatureManager tree.
        ///
        /// Fitur bererror membuat verdict minimal WARN: tree yang punya error
        /// bukan tree yang sehat, dan AI tidak boleh membangun di atasnya seolah
        /// semuanya baik-baik saja.
        /// </summary>
        public static ToolResult FeatureTree(FeatureTreeReading reading)
        {
            if (reading is null) throw new ArgumentNullException(nameof(reading));

            var fitur = reading.Features ?? Array.Empty<FeatureReading>();
            var bererror = fitur.Where(f => f.HasError).ToList();
            var tersupresi = fitur.Count(f => f.IsSuppressed);

            var data = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["features"] = fitur.Select(f => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = f.Name,
                    ["typeName"] = f.TypeName,
                    ["isSuppressed"] = f.IsSuppressed,
                    ["hasError"] = f.HasError,
                    ["parentSketch"] = f.ParentSketch,
                }).ToList(),
                ["featureCount"] = fitur.Count,
                ["suppressedCount"] = tersupresi,
                ["errorCount"] = bererror.Count,
                ["rebuildRequired"] = reading.RebuildRequired,
                ["fieldsNotPopulated"] = FieldBelumDibaca.ListFitur,
            };

            var checks = new List<VerificationCheck>();

            if (bererror.Count == 0)
            {
                checks.Add(VerificationCheck.Pass("no_feature_errors", 0));
            }
            else
            {
                checks.Add(new VerificationCheck
                {
                    Id = "no_feature_errors",
                    Verdict = Verdict.Warn,
                    Required = true,
                    Observed = bererror.Count,
                    Expected = 0,
                    Message = $"{bererror.Count} fitur bererror: {string.Join(", ", bererror.Select(f => f.Name))}.",
                });
            }

            if (reading.RebuildRequired)
            {
                checks.Add(new VerificationCheck
                {
                    Id = "model_rebuilt",
                    Verdict = Verdict.Warn,
                    Required = false,
                    Message = "Model perlu rebuild - pembacaan bisa tidak mencerminkan keadaan terakhir.",
                });
            }

            return Sukses(data, checks);
        }

        // --- get_bounding_box ------------------------------------------------

        /// <summary>
        /// Kotak pembatas.
        ///
        /// <c>bodyCount == 0</c> adalah fakta sah, bukan error - dokumen baru
        /// memang belum punya solid. Tapi kotak pembatas bukan-nol dengan nol
        /// body itu kontradiksi, dan itu ditandai.
        /// </summary>
        public static ToolResult BoundingBox(BoundingBoxReading reading)
        {
            if (reading is null) throw new ArgumentNullException(nameof(reading));

            var ukuran = new Vector3Reading
            {
                X = reading.Max.X - reading.Min.X,
                Y = reading.Max.Y - reading.Min.Y,
                Z = reading.Max.Z - reading.Min.Z,
            };

            var adaUkuran = ukuran.X > 1e-9 || ukuran.Y > 1e-9 || ukuran.Z > 1e-9;

            // HITUNGAN NOL ITU FAKTA. UKURAN NOL ITU KARANGAN.
            //
            // bodyCount = 0 benar: memang tidak ada body. Tapi kotak pembatasnya
            // BUKAN 0x0x0 - ia TIDAK ADA, dan itu dua pernyataan yang berbeda.
            //
            // Nol adalah angka yang tampak masuk akal. Model yang membacanya
            // punya segala alasan melaporkan "part-nya 0 x 0 x 0 mm" alih-alih
            // "tidak ada body", dan laporan itu akan terdengar persis seperti
            // pengukuran sungguhan.
            //
            // Disiplin ini sudah dipegang untuk massGrams di MassProperties,
            // lengkap dengan catatan bahwa massa tidak dikarang. Di sini ia
            // terlewat.
            var adaYangBisaDiukur = reading.BodyCount > 0 || adaUkuran;

            var data = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["min"] = adaYangBisaDiukur ? Vektor(reading.Min) : null,
                ["max"] = adaYangBisaDiukur ? Vektor(reading.Max) : null,
                ["size"] = adaYangBisaDiukur ? Vektor(ukuran) : null,

                // Tetap angka, bukan null: ini hitungan, dan nol benar.
                ["bodyCount"] = reading.BodyCount,
            };

            var checks = new List<VerificationCheck>();

            if (reading.BodyCount == 0)
            {
                checks.Add(new VerificationCheck
                {
                    Id = "has_solid_body",
                    Verdict = adaUkuran ? Verdict.Fail : Verdict.Warn,
                    Required = adaUkuran,
                    Observed = reading.BodyCount,
                    Message = adaUkuran
                        ? "Tidak ada solid body, tapi kotak pembatasnya bukan nol - pembacaan ini kontradiktif."
                        : "Dokumen belum punya solid body.",
                });
            }
            else
            {
                checks.Add(VerificationCheck.Pass("has_solid_body", reading.BodyCount));
            }

            return Sukses(data, checks);
        }

        // --- get_mass_properties ---------------------------------------------

        /// <summary>
        /// Properti massa.
        ///
        /// <c>massGrams</c> null kalau material belum di-assign. JANGAN
        /// mengarang massa dari densitas default lalu menyajikannya sebagai
        /// fakta - itu angka yang akan dipakai orang untuk keputusan teknik.
        /// </summary>
        public static ToolResult MassProperties(MassPropertiesReading reading)
        {
            if (reading is null) throw new ArgumentNullException(nameof(reading));

            // Aturan yang sama dengan BoundingBox: ukuran yang tidak ada
            // dilaporkan null, bukan nol.
            //
            // Body dengan volume nol tidak ada, jadi nol di situ tidak pernah
            // pengukuran yang sah - null tidak menghilangkan informasi apa pun.
            var adaVolume = reading.VolumeMm3 > 1e-9;
            var adaArea = reading.SurfaceAreaMm2 > 1e-9;

            var data = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["volumeMm3"] = adaVolume ? (double?)Bulatkan(reading.VolumeMm3) : null,

                // REDUNDANSI YANG DISENGAJA.
                //
                // Pembaca sudah seharusnya membiarkan massa null kalau material
                // belum di-assign. Tapi justru di situ bug-nya bertahan lama
                // tanpa terlihat, dan pembaca hidup di MikosAI.Addin yang tidak
                // bisa diuji tanpa SolidWorks terpasang.
                //
                // Aturannya ditegakkan ulang di sini, di satu-satunya tempat
                // yang bisa diuji otomatis. Kalau pembacanya keliru lagi,
                // angkanya tidak lolos - dan check di bawah melaporkan
                // ketidakkonsistenannya alih-alih menyembunyikannya.
                ["massGrams"] = reading.DensityAssigned == false
                    ? null
                    : (reading.MassGrams is null ? null : (double?)Bulatkan(reading.MassGrams.Value)),

                // Area diperiksa sendiri, bukan ikut volume: surface body punya
                // area tanpa volume, dan itu pembacaan sungguhan.
                ["surfaceAreaMm2"] = adaArea ? (double?)Bulatkan(reading.SurfaceAreaMm2) : null,

                // centerOfMass TIDAK boleh dinilai dari isinya sendiri: {0,0,0}
                // adalah nilai yang SAH untuk part simetris di titik asal.
                // Jadi ia digantungkan pada volume - tanpa massa, titik pusat
                // massa tidak punya arti.
                ["centerOfMass"] = adaVolume ? Vektor(reading.CenterOfMass) : null,

                // Tiga keadaan: true / false / null. null berarti tidak bisa
                // ditanyakan untuk tipe dokumen ini, bukan "tidak ada".
                ["densityAssigned"] = reading.DensityAssigned,

                // Bukti untuk klaim di atas. Tanpa keduanya, klaim bahwa massa
                // bisa dipercaya tidak bisa diperiksa siapa pun - dan itulah
                // yang membuat bug densitas default bertahan tanpa terlihat.
                ["materialName"] = reading.MaterialName,
                ["densityKgM3"] = reading.DensityKgM3 is null
                    ? null
                    : (double?)Bulatkan(reading.DensityKgM3.Value),
            };

            var checks = new List<VerificationCheck>();

            if (reading.VolumeMm3 > 1e-9)
            {
                checks.Add(VerificationCheck.Pass("nonzero_volume", reading.VolumeMm3));
            }
            else
            {
                checks.Add(new VerificationCheck
                {
                    Id = "nonzero_volume",
                    Verdict = Verdict.Warn,
                    Required = false,
                    Observed = reading.VolumeMm3,
                    Message = "Volume nol - dokumen belum punya solid body.",
                });
            }

            var manualReview = false;

            if (reading.DensityAssigned is null)
            {
                // TIDAK BISA DITANYAKAN - mis. assembly, yang materialnya per
                // komponen dan bukan satu nilai tunggal.
                //
                // Massanya tetap dilaporkan, karena ia mungkin benar. Tapi ia
                // ditandai butuh tinjauan manusia, dan itu disengaja: angka
                // massa dipakai untuk keputusan teknik, jadi kalau harus salah,
                // lebih baik salah ke arah "jangan percaya buta".
                checks.Add(new VerificationCheck
                {
                    Id = "material_assigned",
                    Verdict = Verdict.Warn,
                    Required = false,
                    Message = "Material tidak bisa ditanyakan untuk tipe dokumen ini - "
                              + "assembly punya material per komponen, bukan satu nilai. "
                              + "Massa dilaporkan apa adanya tapi BELUM diverifikasi; "
                              + "periksa densityKgM3 terhadap material yang kamu harapkan.",
                });

                manualReview = true;
            }
            else if (reading.DensityAssigned == false)
            {
                checks.Add(new VerificationCheck
                {
                    Id = "material_assigned",
                    Verdict = Verdict.Warn,
                    Required = false,
                    Message = "Material belum di-assign, jadi massa tidak bisa dihitung. "
                              + "Angka massa TIDAK dikarang dari densitas default.",
                });

                if (reading.MassGrams is not null)
                {
                    // Pembaca memberi massa padahal material tidak ada. Angkanya
                    // sudah ditahan di atas, tapi ia TIDAK boleh hilang diam-diam:
                    // itu bug di lapisan pembacaan, dan kalau tidak dilaporkan ia
                    // akan bertahan lama persis seperti sebelumnya.
                    checks.Add(new VerificationCheck
                    {
                        Id = "mass_consistent_with_material",
                        Verdict = Verdict.Blocked,
                        Required = true,
                        Observed = reading.MassGrams,
                        Message = "Massa diberikan padahal material belum di-assign - "
                                  + "hampir pasti dihitung dari densitas default. "
                                  + "Angkanya ditahan, dan ini bug di lapisan pembacaan.",
                    });
                }
            }
            else if (reading.MassGrams is null)
            {
                // Densitas ada tapi massa tidak terbaca: itu tidak konsisten.
                checks.Add(new VerificationCheck
                {
                    Id = "mass_readable",
                    Verdict = Verdict.Blocked,
                    Required = true,
                    Message = "Material di-assign tapi massa tidak terbaca - pembacaan tidak bisa dipercaya.",
                });
            }
            else
            {
                checks.Add(VerificationCheck.Pass("material_assigned", reading.MaterialName));
                checks.Add(VerificationCheck.Pass("mass_readable", reading.MassGrams));
            }

            return Sukses(data, checks, manualReview);
        }

        // --- inspect_sketches -------------------------------------------------

        /// <summary>
        /// Status sketsa.
        ///
        /// Over-defined dan dangling menghasilkan FAIL, bukan WARN. Keduanya
        /// berarti sketsanya rusak, dan membangun fitur di atasnya menghasilkan
        /// geometri yang salah atau rebuild yang gagal belakangan.
        /// </summary>
        public static ToolResult Sketches(IReadOnlyList<SketchReading> readings)
        {
            if (readings is null) throw new ArgumentNullException(nameof(readings));

            var data = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sketches"] = readings.Select(s => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = s.Name,
                    ["constrainedStatus"] = s.ConstrainedStatus.ToString(),
                    ["entityCount"] = s.EntityCount,
                    ["dimensionCount"] = s.DimensionCount,
                    ["consumedByFeature"] = s.ConsumedByFeature,
                    ["hasOpenContour"] = s.HasOpenContour,
                }).ToList(),
                ["sketchCount"] = readings.Count,
                ["fullyDefinedCount"] = readings.Count(s => s.ConstrainedStatus == SketchConstraintStatus.FullyDefined),
                ["fieldsNotPopulated"] = FieldBelumDibaca.ListSketsa,
            };

            var checks = new List<VerificationCheck>();

            var rusak = readings
                .Where(s => s.ConstrainedStatus == SketchConstraintStatus.OverDefined
                            || s.ConstrainedStatus == SketchConstraintStatus.NoSolution
                            || s.ConstrainedStatus == SketchConstraintStatus.InvalidSolution)
                .ToList();

            if (rusak.Count > 0)
            {
                checks.Add(VerificationCheck.Fail(
                    "sketches_healthy",
                    $"{rusak.Count} sketsa rusak: "
                    + string.Join(", ", rusak.Select(s => $"{s.Name} ({s.ConstrainedStatus})")),
                    observed: rusak.Count,
                    expected: 0));
            }
            else
            {
                checks.Add(VerificationCheck.Pass("sketches_healthy", 0));
            }

            // Status yang tidak bisa dibaca bukan berarti sehat.
            var takDiketahui = readings.Count(s => s.ConstrainedStatus == SketchConstraintStatus.Unknown);
            if (takDiketahui > 0)
            {
                checks.Add(VerificationCheck.Blocked(
                    "sketch_status_readable",
                    $"{takDiketahui} sketsa tidak bisa dibaca status constraint-nya."));
            }

            var kurangDefinisi = readings.Count(s => s.ConstrainedStatus == SketchConstraintStatus.UnderDefined);
            if (kurangDefinisi > 0)
            {
                checks.Add(new VerificationCheck
                {
                    Id = "sketches_fully_defined",
                    Verdict = Verdict.Warn,
                    Required = false,
                    Observed = kurangDefinisi,
                    Message = $"{kurangDefinisi} sketsa under-defined. "
                              + "Itu sah saat perancangan, tapi model jadi tidak sepenuhnya parametrik.",
                });
            }

            return Sukses(data, checks);
        }

        // --- get_selection ----------------------------------------------------

        /// <summary>
        /// Apa yang sedang dipilih pengguna.
        ///
        /// Mengembalikan referensi entitas SEMANTIK yang bisa langsung dipakai
        /// tool lain - bukan Face1/Edge1. Lihat CONTRIBUTING.md aturan 4.
        ///
        /// Seleksi kosong adalah fakta sah, bukan kegagalan.
        /// </summary>
        public static ToolResult Selection(IReadOnlyList<SelectionReading> readings)
        {
            if (readings is null) throw new ArgumentNullException(nameof(readings));

            var items = readings.Select(s =>
            {
                var item = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["kind"] = s.Kind,
                    ["typeName"] = s.TypeName,
                    ["name"] = s.Name,
                    ["centroid"] = s.Centroid is null ? null : Vektor(s.Centroid),
                    ["normal"] = s.Normal is null ? null : Vektor(s.Normal),
                    ["entityRef"] = EntityRef(s),
                };

                return item;
            }).ToList();

            var data = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["count"] = readings.Count,
                ["items"] = items,
            };

            var checks = new List<VerificationCheck>
            {
                VerificationCheck.Pass("selection_read", readings.Count),
            };

            // Entitas yang tidak bisa dirujuk secara stabil harus terlihat -
            // kalau tidak, AI akan mengira bisa memakainya.
            var takBisaDirujuk = items.Count(i => i["entityRef"] is null);
            if (takBisaDirujuk > 0)
            {
                checks.Add(new VerificationCheck
                {
                    Id = "selection_referenceable",
                    Verdict = Verdict.Warn,
                    Required = false,
                    Observed = takBisaDirujuk,
                    Message = $"{takBisaDirujuk} entitas terpilih tidak bisa dirujuk secara stabil "
                              + "dan tidak boleh dipakai sebagai argumen tool.",
                });
            }

            return Sukses(data, checks);
        }

        /// <summary>
        /// Membangun EntityRef sesuai schemas/common.schema.json.
        /// Mengembalikan null kalau entitasnya tidak bisa dirujuk secara stabil.
        /// </summary>
        internal static Dictionary<string, object?>? EntityRef(SelectionReading s)
        {
            switch (s.Kind)
            {
                case "plane":
                case "feature":
                case "sketch":
                    if (string.IsNullOrWhiteSpace(s.Name))
                    {
                        return null;
                    }

                    return new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["kind"] = s.Kind,
                        ["by"] = "name",
                        ["name"] = s.Name,
                    };

                case "face":
                    if (s.Normal is null || s.Centroid is null)
                    {
                        return null;
                    }

                    return new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["kind"] = "face",
                        ["by"] = "normal_and_point",
                        ["normal"] = Vektor(s.Normal),
                        ["nearPoint"] = Vektor(s.Centroid),
                    };

                case "edge":
                    if (s.Centroid is null)
                    {
                        return null;
                    }

                    return new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["kind"] = "edge",
                        ["by"] = "midpoint",
                        ["nearPoint"] = Vektor(s.Centroid),
                    };

                default:
                    return null;
            }
        }

        private static Dictionary<string, object?> Vektor(Vector3Reading v)
            => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["x"] = Bulatkan(v.X),
                ["y"] = Bulatkan(v.Y),
                ["z"] = Bulatkan(v.Z),
            };

        /// <summary>
        /// Membulatkan ke mikrometer. Konversi meter ke mm menghasilkan ekor
        /// floating point yang panjang, dan angka seperti 99.99999999999999
        /// membuat model berpikir ada ketidaktepatan padahal tidak ada.
        /// </summary>
        private static double Bulatkan(double nilai) => Math.Round(nilai, 6);

        private static ToolResult Sukses(
            IReadOnlyDictionary<string, object?> data,
            IReadOnlyList<VerificationCheck> checks,
            bool manualReviewRequired = false)
            => new ToolResult
            {
                Ok = true,
                CapabilityId = CapabilityId,
                Data = data,
                // Read-only: tidak ada yang ditulis, jadi tidak ada yang perlu
                // dibuktikan lewat baca-ulang.
                Verification = VerificationGate.Evaluate(
                    checks,
                    manualReviewRequired: manualReviewRequired,
                    requiresEvidence: false),
            };
    }
}
