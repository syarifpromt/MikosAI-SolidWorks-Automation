using System;
using System.Collections.Generic;
using MikosAI.Core.Tools;
using MikosAI.Core.Threading;
using SolidWorks.Interop.sldworks;

namespace MikosAI.Addin
{
    /// <summary>
    /// Menerjemahkan API SolidWorks menjadi data polos Core.
    ///
    /// Kelas ini SENGAJA tipis dan bodoh. Semua aturan - sketsa over-defined itu
    /// FAIL, massa tanpa material tidak boleh dikarang, bodyCount nol itu fakta
    /// sah - tinggal di <see cref="ReadOnlyToolShaper"/> di Core, tempat
    /// semuanya bisa diuji tanpa membuka CAD.
    ///
    /// Yang tersisa di sini hanyalah penerjemahan, yang memang hanya bisa
    /// diverifikasi di mesin bermesin SolidWorks.
    ///
    /// SATUAN: API SolidWorks memakai METER. Konversi ke milimeter terjadi di
    /// sini dan HANYA di sini.
    ///
    /// ATURAN: setiap method harus dipanggil dari thread utama SolidWorks,
    /// lewat MainThreadDispatcher. Lihat CONTRIBUTING.md aturan 1.
    /// </summary>
    internal sealed class SolidWorksReader
    {
        private const double MeterKeMm = 1000.0;

        private readonly ISldWorks _app;
        private readonly HostDiagnostics _diagnostics;
        private readonly IMainThreadDispatcher _dispatcher;

        internal SolidWorksReader(
            ISldWorks app,
            HostDiagnostics diagnostics,
            IMainThreadDispatcher dispatcher)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>
        /// Menegakkan aturan nomor 1 CONTRIBUTING.md lewat KODE.
        /// Lihat SolidWorksApi.PastikanThreadUtama untuk alasannya.
        /// </summary>
        private void PastikanThreadUtama(string namaMethod)
        {
            if (_dispatcher.IsOnMainThread)
            {
                return;
            }

            var pelanggaran = new MikosAI.Core.Threading.MainThreadViolationException(
                $"SolidWorksReader.{namaMethod}");

            _diagnostics.AddError("pelanggaran thread utama", pelanggaran);

            throw pelanggaran;
        }

        private IModelDoc2? DokumenAktif()
        {
            try
            {
                return _app.ActiveDoc as IModelDoc2;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("ActiveDoc", exception);
                return null;
            }
        }

        // --- get_active_document ---------------------------------------------

        internal DocumentReading BacaDokumen()
        {
            PastikanThreadUtama(nameof(BacaDokumen));

            var hasil = new DocumentReading();

            try
            {
                hasil.SolidWorksRevision = _app.RevisionNumber();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("RevisionNumber", exception);
            }

            var model = DokumenAktif();
            if (model is null)
            {
                return hasil;
            }

            hasil.HasDocument = true;

            Coba("GetTitle", () => hasil.Title = model.GetTitle());
            Coba("GetPathName", () =>
            {
                var path = model.GetPathName();
                hasil.Path = string.IsNullOrEmpty(path) ? null : path;
            });
            Coba("GetType", () => hasil.DocumentType = JenisDokumen(model.GetType()));
            Coba("GetSaveFlag", () => hasil.IsDirty = model.GetSaveFlag());
            Coba("ConfigurationManager", () =>
            {
                hasil.ActiveConfiguration = model.ConfigurationManager?.ActiveConfiguration?.Name;
            });

            return hasil;
        }

        /// <summary>
        /// swDocumentTypes_e: 1=part, 2=assembly, 3=drawing.
        ///
        /// Angka ditulis langsung alih-alih memakai enum swconst, supaya stub
        /// compile-check tidak perlu memuat seluruh enum SolidWorks.
        /// </summary>
        private static string JenisDokumen(int kode)
        {
            switch (kode)
            {
                case 1: return "part";
                case 2: return "assembly";
                case 3: return "drawing";
                default: return "unknown";
            }
        }

        // --- list_features ----------------------------------------------------

        internal FeatureTreeReading BacaFitur()
        {
            PastikanThreadUtama(nameof(BacaFitur));

            var hasil = new FeatureTreeReading();
            var daftar = new List<FeatureReading>();

            var model = DokumenAktif();
            if (model is null)
            {
                return hasil;
            }

            Coba("GetRebuildState", () =>
            {
                // 0 = tidak perlu rebuild.
                hasil.RebuildRequired = model.Extension.NeedsRebuild2 != 0;
            });

            try
            {
                var fitur = model.FirstFeature() as IFeature;
                var penjaga = 0;

                while (fitur is not null && penjaga++ < 5000)
                {
                    var bacaan = new FeatureReading();

                    Coba("Feature.Name", () => bacaan.Name = fitur.Name ?? string.Empty);
                    Coba("Feature.GetTypeName2", () => bacaan.TypeName = fitur.GetTypeName2());
                    Coba("Feature.IsSuppressed", () => bacaan.IsSuppressed = fitur.IsSuppressed());

                    Coba("Feature.GetErrorCode2", () =>
                    {
                        // 0 = tidak ada error.
                        bacaan.HasError = fitur.GetErrorCode2(out _) != 0;
                    });

                    daftar.Add(bacaan);

                    fitur = fitur.GetNextFeature() as IFeature;
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menelusuri FeatureManager tree", exception);
            }

            hasil.Features = daftar;
            return hasil;
        }

        // --- get_bounding_box --------------------------------------------------

        internal BoundingBoxReading BacaKotakPembatas()
        {
            PastikanThreadUtama(nameof(BacaKotakPembatas));

            var hasil = new BoundingBoxReading();

            var model = DokumenAktif();
            if (model is null)
            {
                return hasil;
            }

            Coba("GetBodies2", () =>
            {
                var part = model as IPartDoc;
                if (part is null)
                {
                    return;
                }

                // swBodyType_e.swSolidBody = 0, visibleOnly = false.
                if (part.GetBodies2(0, false) is object[] bodies)
                {
                    hasil.BodyCount = bodies.Length;
                }
            });

            Coba("GetPartBox", () =>
            {
                var part = model as IPartDoc;
                if (part is null)
                {
                    return;
                }

                // GetPartBox mengembalikan 6 double dalam METER:
                // xMin, yMin, zMin, xMax, yMax, zMax.
                if (part.GetPartBox(true) is double[] kotak && kotak.Length >= 6)
                {
                    hasil.Min = new Vector3Reading
                    {
                        X = kotak[0] * MeterKeMm,
                        Y = kotak[1] * MeterKeMm,
                        Z = kotak[2] * MeterKeMm,
                    };
                    hasil.Max = new Vector3Reading
                    {
                        X = kotak[3] * MeterKeMm,
                        Y = kotak[4] * MeterKeMm,
                        Z = kotak[5] * MeterKeMm,
                    };
                }
            });

            return hasil;
        }

        // --- get_mass_properties ------------------------------------------------

        internal MassPropertiesReading BacaPropertiMassa()
        {
            PastikanThreadUtama(nameof(BacaPropertiMassa));

            var hasil = new MassPropertiesReading();

            var model = DokumenAktif();
            if (model is null)
            {
                return hasil;
            }

            // MATERIAL DITANYAKAN, BUKAN DISIMPULKAN.
            //
            // Versi sebelumnya menyimpulkan "material ada" dari status == 0 dan
            // massa > 0. Itu salah, dan salahnya tidak kelihatan: SolidWorks
            // SELALU mengembalikan massa, memakai densitas default 1000 kg/m3
            // kalau material belum di-assign, dan status tetap 0 karena
            // perhitungannya sendiri berhasil.
            //
            // Akibatnya balok 100266 mm3 dilaporkan 100,27 gram dengan
            // densityAssigned: true - angka air yang disajikan sebagai fakta,
            // tepat yang dilarang catatan di MassPropertiesReading.
            //
            // Satu-satunya cara menjawabnya adalah menanyakan nama materialnya.
            var jenis = 0;
            Coba("GetType untuk material", () => jenis = model.GetType());

            if (jenis == 1)   // part
            {
                Coba("GetMaterialPropertyName2", () =>
                {
                    var konfigurasi = model.ConfigurationManager?.ActiveConfiguration?.Name ?? string.Empty;

                    var nama = ((IPartDoc)model).GetMaterialPropertyName2(konfigurasi, out _);

                    hasil.MaterialName = string.IsNullOrWhiteSpace(nama) ? null : nama.Trim();
                    hasil.DensityAssigned = hasil.MaterialName is not null;
                });
            }
            else
            {
                // Assembly dan drawing tidak punya satu material tunggal.
                // null berarti "tidak bisa ditanyakan" - bukan "tidak ada".
                hasil.DensityAssigned = null;
            }

            Coba("GetMassProperties2", () =>
            {
                // Susunan: [cgX, cgY, cgZ, volume, area, massa, ...] dalam SI.
                var nilai = model.Extension.GetMassProperties2(1, out var status, false) as double[];

                if (nilai is null || nilai.Length < 6)
                {
                    return;
                }

                hasil.CenterOfMass = new Vector3Reading
                {
                    X = nilai[0] * MeterKeMm,
                    Y = nilai[1] * MeterKeMm,
                    Z = nilai[2] * MeterKeMm,
                };

                // m^3 -> mm^3, m^2 -> mm^2
                hasil.VolumeMm3 = nilai[3] * 1e9;
                hasil.SurfaceAreaMm2 = nilai[4] * 1e6;

                // status != 0 berarti perhitungannya sendiri gagal.
                //
                // Massa hanya dilaporkan kalau material TIDAK terbukti absen.
                // Untuk part tanpa material, DensityAssigned == false dan
                // massanya dibiarkan null - karena angka yang ada di nilai[5]
                // di situ dihitung dari densitas default, dan menyajikannya
                // sebagai fakta persis yang dilarang.
                //
                // DensityAssigned == null (assembly) TETAP melaporkan massanya:
                // ia mungkin benar, dan shaper menandainya butuh tinjauan
                // manusia alih-alih menyembunyikannya.
                if (status == 0 && nilai[5] > 0 && hasil.DensityAssigned != false)
                {
                    hasil.MassGrams = nilai[5] * 1000.0;   // kg -> gram

                    // Densitas yang BENAR-BENAR dipakai, supaya angkanya bisa
                    // diaudit. Inilah yang akan membongkar kekeliruan serupa
                    // berikutnya dalam sekali pandang: nilai 1000 kg/m3 pada
                    // part yang seharusnya baja langsung terlihat salah.
                    if (nilai[3] > 1e-12)
                    {
                        hasil.DensityKgM3 = nilai[5] / nilai[3];   // kg / m^3
                    }
                }
            });

            return hasil;
        }

        // --- inspect_sketches ----------------------------------------------------

        internal IReadOnlyList<SketchReading> BacaSketsa(string? namaSketsa)
        {
            PastikanThreadUtama(nameof(BacaSketsa));

            var daftar = new List<SketchReading>();

            var model = DokumenAktif();
            if (model is null)
            {
                return daftar;
            }

            try
            {
                var fitur = model.FirstFeature() as IFeature;
                var penjaga = 0;

                while (fitur is not null && penjaga++ < 5000)
                {
                    var tipe = string.Empty;
                    Coba("Feature.GetTypeName2", () => tipe = fitur.GetTypeName2() ?? string.Empty);

                    if (tipe == "ProfileFeature" || tipe == "3DProfileFeature")
                    {
                        var nama = string.Empty;
                        Coba("Feature.Name", () => nama = fitur.Name ?? string.Empty);

                        if (string.IsNullOrEmpty(namaSketsa)
                            || string.Equals(nama, namaSketsa, StringComparison.OrdinalIgnoreCase))
                        {
                            daftar.Add(BacaSatuSketsa(fitur, nama));
                        }
                    }

                    fitur = fitur.GetNextFeature() as IFeature;
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menelusuri sketsa", exception);
            }

            return daftar;
        }

        private SketchReading BacaSatuSketsa(IFeature fitur, string nama)
        {
            var bacaan = new SketchReading { Name = nama };

            Coba("Sketch.GetConstrainedStatus", () =>
            {
                if (fitur.GetSpecificFeature2() is ISketch sketsa)
                {
                    bacaan.ConstrainedStatus = StatusConstraint(sketsa.GetConstrainedStatus());

                    if (sketsa.GetSketchSegments() is object[] segmen)
                    {
                        bacaan.EntityCount = segmen.Length;
                    }

                    // Jumlah dimensi SENGAJA tidak dibaca di sini.
                    // ISketch tidak mengeksposnya langsung, dan menelusuri
                    // IDisplayDimension memperluas permukaan API tanpa memberi
                    // nilai yang sepadan di tahap ini. Dibiarkan 0, dan itu
                    // jujur - bukan angka karangan.
                }
            });

            return bacaan;
        }

        /// <summary>
        /// Memetakan swConstrainedStatus_e ke status domain kita.
        ///
        /// NILAI DI BAWAH DIREFLEKSIKAN DARI swconst ASLI, BUKAN DITEBAK:
        ///
        ///   swUnknownConstraint = 1
        ///   swUnderConstrained  = 2
        ///   swFullyConstrained  = 3
        ///   swOverConstrained   = 4
        ///   swNoSolution        = 5
        ///   swInvalidSolution   = 6
        ///   swAutosolveOff      = 7
        ///
        /// VERSI SEBELUMNYA BERGESER SATU - ia memetakan 1 ke UnderDefined,
        /// 2 ke FullyDefined, 3 ke OverDefined. Akibatnya:
        ///
        ///   sketsa FULLY DEFINED dilaporkan OverDefined -> FAIL palsu
        ///   sketsa UNDER DEFINED dilaporkan FullyDefined -> RUSAK TERLIHAT SEHAT
        ///
        /// Arah kedua itu yang berbahaya, dan itu alasan bug ini mahal: ia
        /// memberi lampu hijau pada sketsa yang belum terdefinisi, yaitu
        /// persis keadaan yang akan membuat tool tulis Fase 3 menghasilkan
        /// geometri yang bergeser tanpa ada yang memperingatkan.
        ///
        /// Ditemukan oleh golden test pada plat 100x80x10 yang jelas-jelas
        /// fully defined. Itu gunanya acuan yang jawabannya sudah diketahui
        /// lebih dulu.
        ///
        /// Nilai yang tidak dikenal menjadi Unknown, dan Core memperlakukan
        /// Unknown sebagai BLOCKED - bukan sehat.
        /// </summary>
        internal static SketchConstraintStatus StatusConstraint(int kode)
        {
            switch (kode)
            {
                case 1: return SketchConstraintStatus.Unknown;         // swUnknownConstraint
                case 2: return SketchConstraintStatus.UnderDefined;    // swUnderConstrained
                case 3: return SketchConstraintStatus.FullyDefined;    // swFullyConstrained
                case 4: return SketchConstraintStatus.OverDefined;     // swOverConstrained
                case 5: return SketchConstraintStatus.NoSolution;      // swNoSolution
                case 6: return SketchConstraintStatus.InvalidSolution; // swInvalidSolution

                // swAutosolveOff: SolidWorks sedang TIDAK menyelesaikan
                // constraint, jadi status apa pun tidak berarti apa-apa.
                // "Tidak tahu" adalah satu-satunya jawaban yang jujur, dan
                // Unknown membuatnya BLOCKED alih-alih lolos sebagai sehat.
                case 7: return SketchConstraintStatus.Unknown;

                default: return SketchConstraintStatus.Unknown;
            }
        }

        // --- get_selection ---------------------------------------------------------

        internal IReadOnlyList<SelectionReading> BacaSeleksi()
        {
            PastikanThreadUtama(nameof(BacaSeleksi));

            var daftar = new List<SelectionReading>();

            var model = DokumenAktif();
            if (model is null)
            {
                return daftar;
            }

            try
            {
                var mgr = model.SelectionManager as ISelectionMgr;
                if (mgr is null)
                {
                    return daftar;
                }

                var jumlah = mgr.GetSelectedObjectCount2(-1);

                for (var i = 1; i <= jumlah; i++)
                {
                    var bacaan = new SelectionReading();
                    var indeks = i;

                    Coba("SelectionMgr.GetSelectedObjectType3", () =>
                    {
                        var tipe = mgr.GetSelectedObjectType3(indeks, -1);
                        bacaan.Kind = JenisSeleksi(tipe);
                        bacaan.TypeName = tipe.ToString();
                    });

                    Coba("SelectionMgr.GetSelectedObject6", () =>
                    {
                        // Cast eksplisit ke object: PIA mendeklarasikan
                        // kembaliannya sebagai dynamic, dan membiarkannya
                        // dynamic menarik dependensi Microsoft.CSharp ke dalam
                        // add-in tanpa memberi manfaat apa pun.
                        object? objek = (object?)mgr.GetSelectedObject6(indeks, -1);
                        IsiDetailSeleksi(bacaan, objek);
                    });

                    daftar.Add(bacaan);
                }
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("membaca seleksi", exception);
            }

            return daftar;
        }

        /// <summary>swSelectType_e yang relevan untuk kita.</summary>
        private static string JenisSeleksi(int tipe)
        {
            switch (tipe)
            {
                case 1: return "edge";      // swSelEDGES
                case 2: return "face";      // swSelFACES
                case 3: return "vertex";    // swSelVERTICES
                case 4: return "plane";     // swSelDATUMPLANES
                case 9: return "sketch";    // swSelSKETCHES
                case 22: return "feature";  // swSelBODYFEATURES
                default: return "unknown";
            }
        }

        private void IsiDetailSeleksi(SelectionReading bacaan, object? objek)
        {
            if (objek is null)
            {
                return;
            }

            // Entitas bernama: bidang, fitur, sketsa.
            if (objek is IFeature fitur)
            {
                Coba("Feature.Name", () => bacaan.Name = fitur.Name);
                return;
            }

            if (objek is IFace2 permukaan)
            {
                Coba("Face2.Normal", () =>
                {
                    if (permukaan.Normal is double[] n && n.Length >= 3)
                    {
                        bacaan.Normal = new Vector3Reading { X = n[0], Y = n[1], Z = n[2] };
                    }
                });

                Coba("Face2.GetBox", () =>
                {
                    // Titik tengah kotak permukaan dipakai sebagai nearPoint.
                    // Bukan centroid sejati, tapi cukup untuk mencocokkan
                    // permukaan dan jauh lebih stabil daripada Face1.
                    if (permukaan.GetBox() is double[] kotak && kotak.Length >= 6)
                    {
                        bacaan.Centroid = new Vector3Reading
                        {
                            X = (kotak[0] + kotak[3]) / 2 * MeterKeMm,
                            Y = (kotak[1] + kotak[4]) / 2 * MeterKeMm,
                            Z = (kotak[2] + kotak[5]) / 2 * MeterKeMm,
                        };
                    }
                });

                return;
            }

            if (objek is IEdge tepi)
            {
                Coba("Edge.GetCurveParams2", () =>
                {
                    // Titik tengah tepi: rata-rata titik awal dan akhir.
                    //
                    // GetCurveParams2 mengembalikan array double, sedangkan
                    // GetCurveParams3 mengembalikan CurveParamData - tipe
                    // terstruktur yang tidak bisa dipola sebagai double[].
                    // Compiler PIA asli yang memberi tahu itu.
                    if (tepi.GetCurveParams2() is double[] p && p.Length >= 6)
                    {
                        bacaan.Centroid = new Vector3Reading
                        {
                            X = (p[0] + p[3]) / 2 * MeterKeMm,
                            Y = (p[1] + p[4]) / 2 * MeterKeMm,
                            Z = (p[2] + p[5]) / 2 * MeterKeMm,
                        };
                    }
                });
            }
        }

        /// <summary>
        /// Menjalankan satu pembacaan API dan mencatat kegagalannya.
        ///
        /// Satu properti yang tidak terbaca tidak boleh menggagalkan seluruh
        /// pembacaan - lebih baik hasil parsial yang jujur daripada tidak ada
        /// hasil sama sekali. Setiap kegagalan tetap muncul di host-status.json.
        /// </summary>
        private void Coba(string konteks, Action aksi)
        {
            try
            {
                aksi();
            }
            catch (Exception exception)
            {
                _diagnostics.AddError(konteks, exception);
            }
        }
    }
}
