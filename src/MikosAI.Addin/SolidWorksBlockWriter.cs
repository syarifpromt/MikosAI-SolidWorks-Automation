using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MikosAI.Core.Plan;
using MikosAI.Core.Threading;
using MikosAI.Core.Tools;
using SolidWorks.Interop.sldworks;

namespace MikosAI.Addin
{
    /// <summary>Native acceptance pilot. Tidak didaftarkan sebagai tool MCP.</summary>
    internal sealed class SolidWorksBlockWriter
    {
        private readonly ISldWorks _app;
        private readonly HostDiagnostics _diagnostics;
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly SolidWorksReader _reader;
        internal readonly List<object> Trace = new List<object>();
        internal string? CreatedDocumentTitle;

        internal SolidWorksBlockWriter(ISldWorks app, HostDiagnostics diagnostics,
            IMainThreadDispatcher dispatcher, SolidWorksReader reader)
        { _app=app; _diagnostics=diagnostics; _dispatcher=dispatcher; _reader=reader; }

        private void PastikanThreadUtama()
        {
            if (_dispatcher.IsOnMainThread) return;
            var error = new MainThreadViolationException(nameof(SolidWorksBlockWriter));
            _diagnostics.AddError("pelanggaran thread utama writer", error);
            throw error;
        }

        internal object Run(BlockPilotPlan plan, string path, string? template)
        {
            PastikanThreadUtama();
            if (plan is null) throw new ArgumentNullException(nameof(plan));
            path = Path.GetFullPath(path);
            if (!path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase) || File.Exists(path))
                throw new ArgumentException("Output harus .SLDPRT baru; pilot tidak menimpa berkas.");
            template = string.IsNullOrWhiteSpace(template) ? _app.GetUserPreferenceStringValue(8) : template;
            if (!File.Exists(template)) throw new InvalidOperationException("Default template part tidak ditemukan; berikan -TemplatePath.");
            // Part baru: dokumen pengguna yang sedang terbuka tidak diedit/ditutup.
            if ((object)_app.NewDocument(template!, 0, 0, 0) is not IModelDoc2 model)
                throw new InvalidOperationException("NewDocument tidak menghasilkan part.");
            CreatedDocumentTitle = model.GetTitle();
            if (!Equals((object)_app.ActiveDoc,model))
                throw new InvalidOperationException("Part baru belum menjadi dokumen aktif; dokumen pengguna tidak disentuh.");
            if (model.GetType()!=1 || _reader.BacaKotakPembatas().BodyCount!=0 || _reader.BacaSketsa(null).Count!=0)
                throw new InvalidOperationException("Template pilot harus part kosong, tanpa body atau sketsa praisi.");
            var dimensionNames = new List<string>();
            string sketchName = string.Empty;
            string bossName = string.Empty;
            Dictionary<string, object?> Snapshot()
            {
                var errorsBefore=_diagnostics.Errors.Count;
                var sketches = _reader.BacaSketsa(null);
                var snapshot = new Dictionary<string, object?>
                {
                    ["features"] = _reader.BacaFitur().Features.Select(f => new { f.Name, f.TypeName }).ToArray(),
                    ["sketches"] = sketches.Select(s => new { s.Name, s.EntityCount, s.DimensionCountRead, status=s.ConstrainedStatus.ToString() }).ToArray(),
                    ["volumeMm3"] = _reader.BacaPropertiMassa().VolumeMm3,
                };
                if (_diagnostics.Errors.Count!=errorsBefore || sketches.Any(s=>s.DimensionCountRead is null))
                    throw new InvalidOperationException("Snapshot baca ulang tidak lengkap.");
                return snapshot;
            }
            void Step(string tool, Func<object> action)
            {
                var beforeSnapshot=Snapshot();
                var before = JsonSerializer.Serialize(beforeSnapshot);
                object? data = null;
                string? failure = null;
                try { data = action(); }
                catch (Exception e) { failure=e.Message; throw; }
                finally
                {
                    bool? changed = null;
                    string? readBackFailed = null;
                    Dictionary<string,object?>? afterSnapshot=null;
                    try { afterSnapshot=Snapshot(); changed=before != JsonSerializer.Serialize(afterSnapshot); }
                    catch (Exception e) { readBackFailed=e.Message; }
                    Trace.Add(new {tool, modelChanged=changed, readBackFailed, beforeSnapshot, afterSnapshot, data, error=failure});
                }
            }
            IFeature FindFeature(string name)
            {
                IFeature? Search(IFeature? current, bool sub)
                {
                    for(var guard=0; current!=null && guard<5000; guard++)
                    {
                        if (current.Name == name) return current;
                        var found=Search(current.GetFirstSubFeature() as IFeature, true);
                        if (found!=null) return found;
                        current=(sub ? current.GetNextSubFeature() : current.GetNextFeature()) as IFeature;
                    }
                    return null;
                }
                return Search(model.FirstFeature() as IFeature, false) ?? throw new InvalidOperationException("Fitur tidak ditemukan: "+name);
            }
            ISketch ActiveSketch() => (object)model.GetActiveSketch2() is ISketch s ? s : throw new InvalidOperationException("Tidak ada sketsa aktif.");
            bool Near(double a, double b) => Math.Abs(a-b) <= 1e-9;
            // FullName menyertakan nama dokumen, yang berubah ketika SaveAs.
            // Simpan nama dimensi+fitur yang benar-benar dibaca, tanpa suffix dokumen.
            string DimensionKey(string fullName) => string.Join("@",fullName.Split('@').Take(2));
            void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
            object[] DimensionReadings(IFeature feature)
            {
                var rows=new List<object>();
                var display=feature.GetFirstDisplayDimension() as IDisplayDimension;
                for(var guard=0; display!=null && guard<1000; guard++)
                {
                    var dim=(IDimension)display.GetDimension2(0);
                    if(DimensionKey(dim.FullName).EndsWith("@"+feature.Name,StringComparison.Ordinal))
                        rows.Add(new {name=DimensionKey(dim.FullName),fullName=dim.FullName,valueMm=dim.SystemValue*1000,drivenState=dim.DrivenState});
                    display=feature.GetNextDisplayDimension(display) as IDisplayDimension;
                }
                return rows.ToArray();
            }
            Step("create_sketch", () =>
            {
                var plane=FindFeature("Front Plane");
                Require(plane.GetTypeName2()=="RefPlane", "Front Plane bukan reference plane.");
                model.ClearSelection2(true);
                Require(plane.Select2(false,0), "Plane tidak bisa dipilih.");
                model.SketchManager.InsertSketch(true);
                var sketch=ActiveSketch();
                var feature=model.FirstFeature() as IFeature;
                for(var guard=0; feature!=null && !Equals((object)feature.GetSpecificFeature2(),sketch) && guard<5000; guard++)
                    feature=feature.GetNextFeature() as IFeature;
                Require(feature!=null && Equals((object)feature.GetSpecificFeature2(),sketch), "Node sketsa aktif tidak ditemukan.");
                Require(feature!=null, "Node sketsa baru tidak ditemukan.");
                feature!.Name="BlockProfile";
                sketchName=feature.Name;
                Require(sketchName=="BlockProfile", "Nama aktual sketsa berbeda dari plan pilot.");
                return new {sketchName,isOpen=true};
            });
            Step("add_rectangle", () =>
            {
                var sketch=ActiveSketch();
                var width=plan.WidthMm/1000;
                var height=plan.HeightMm/1000;
                var created=model.SketchManager.CreateCornerRectangle(0,0,0,width,height,0) as object[];
                Require(created!=null, "CreateCornerRectangle gagal.");
                var lines=created!.OfType<ISketchSegment>().Where(s=>!s.ConstructionGeometry).ToList();
                Require(lines.Count==4, "Persegi harus mempunyai empat garis non-konstruksi.");
                var horizontal=lines.First(s=>s is ISketchLine l && Near(((ISketchPoint)(object)l.GetStartPoint2()).Y,((ISketchPoint)(object)l.GetEndPoint2()).Y));
                var vertical=lines.First(s=>s is ISketchLine l && Near(((ISketchPoint)(object)l.GetStartPoint2()).X,((ISketchPoint)(object)l.GetEndPoint2()).X));
                var prompt=_app.GetUserPreferenceToggle(10); // swInputDimValOnCreate
                try
                {
                    _app.SetUserPreferenceToggle(10,false);
                    void Dimension(ISketchSegment segment, double value, bool isHorizontal)
                    {
                        model.ClearSelection2(true);
                        Require(segment.Select4(false,null), "Garis dimensi tidak dapat dipilih.");
                        object raw=isHorizontal ? (object)model.AddHorizontalDimension2(width/2,-height/4,0)
                            : (object)model.AddVerticalDimension2(-width/4,height/2,0);
                        Require(raw is IDisplayDimension, "Dimensi tidak terbentuk.");
                        var dim=(IDimension)((IDisplayDimension)raw).GetDimension2(0);
                        Require(dim.SetSystemValue3(value,1,null)==0, "SetSystemValue3 gagal.");
                        Require(dim.DrivenState==2 && Near(dim.SystemValue,value), "Dimensi desain tidak terbaca sebagai driving dengan nilai benar.");
                        dimensionNames.Add(DimensionKey(dim.FullName));
                    }
                    Dimension(horizontal,width,true);
                    Dimension(vertical,height,false);
                }
                finally { _app.SetUserPreferenceToggle(10,prompt); }
                if (sketch.GetConstrainedStatus()!=3)
                {
                    // Pilih origin eksternal dalam koordinat MODEL, bukan layar.
                    model.ClearSelection2(true);
                    Require(model.Extension.SelectByID2("","EXTSKETCHPOINT",0,0,0,false,0,null,0), "Origin belum bisa dipilih untuk jangkar Coincident.");
                    var origin=((ISelectionMgr)(object)model.SelectionManager).GetSelectedObject6(1,-1) as ISketchPoint;
                    Require(origin!=null && Near(origin.X,0) && Near(origin.Y,0) && Near(origin.Z,0), "Referensi origin tidak cocok.");
                    var corner=lines.Cast<ISketchLine>().SelectMany(l=>new[]{(ISketchPoint)(object)l.GetStartPoint2(),(ISketchPoint)(object)l.GetEndPoint2()})
                        .First(p=>Near(p.X,0)&&Near(p.Y,0));
                    Require(corner.Select4(true,null), "Titik sudut tidak bisa dipilih.");
                    model.SketchAddConstraints("sgCOINCIDENT");
                }
                model.ClearSelection2(true);
                Require(sketch.GetConstrainedStatus()==3, "Profil belum FullyDefined; tidak memakai Fix sebagai fallback.");
                return new {dimensionNames=dimensionNames.ToArray(), constrainedStatus="FullyDefined", dimensions=DimensionReadings(FindFeature(sketchName))};
            });
            Step("close_sketch", () =>
            {
                Require(ActiveSketch().GetConstrainedStatus()==3, "Profil belum FullyDefined.");
                model.SketchManager.InsertSketch(true);
                Require((object)model.GetActiveSketch2()==null, "Sketsa masih terbuka.");
                return new {sketchName,isOpen=false,sketchFullyDefined=true};
            });
            Step("extrude_boss", () =>
            {
                model.ClearSelection2(true);
                Require(FindFeature(sketchName).Select2(false,0), "Profil tidak dapat dipilih.");
                // Nama argumen mengikuti PIA yang direfleksikan; urutannya diuji.
                var feature=BlockExtrusion.Create(model.FeatureManager,plan.DepthMm/1000);
                Require(feature!=null,"FeatureExtrusion2 tidak menghasilkan fitur.");
                bossName=feature!.Name;
                Require(model.EditRebuild3(), "Rebuild gagal setelah extrude.");
                Require(feature.GetErrorCode2(out _)==0,"Fitur extrude melaporkan rebuild error/warning.");
                return new {featureName=bossName, dimensions=DimensionReadings(feature), measurements=Verify()};
            });
            string holeFeatureName = string.Empty;
            object? holeReadback = null;
            var volumeSebelumLubang = 0.0;

            if (plan.Holes is not null)
            {
                Step("hole_wizard", () =>
                {
                    volumeSebelumLubang = _reader.BacaPropertiMassa().VolumeMm3;
                    Require(volumeSebelumLubang > 0, "Volume sebelum lubang tidak terbaca.");

                    // PERMUKAAN ATAS, dipilih lewat koordinat MODEL - bukan
                    // lewat nama Face1 dan bukan lewat koordinat layar.
                    //
                    // Titik uji diletakkan di tengah permukaan atas: x dan y di
                    // tengah balok, z tepat di ketinggian extrude. SelectByID2
                    // memakai meter.
                    model.ClearSelection2(true);
                    var tengahX = plan.WidthMm / 2000.0;
                    var tengahY = plan.HeightMm / 2000.0;
                    var atasZ = plan.DepthMm / 1000.0;
                    Require(model.Extension.SelectByID2("", "FACE", tengahX, tengahY, atasZ, false, 0, null, 0),
                        "Permukaan atas balok tidak bisa dipilih pada koordinat model.");

                    var writer = new HoleWizardWriter(_app, _diagnostics);
                    var hasil = writer.Buat(model, plan.Holes!);
                    holeFeatureName = hasil.FeatureName;

                    // TRANSFORMASI BALIK KE KOORDINAT MODEL.
                    //
                    // GetSketchPoints mengembalikan titik dalam RUANG SKETSA.
                    // Di permukaan atas, ruang itu BERBEDA dari koordinat model
                    // - dan justru itu sebabnya lubang ditaruh di sana alih-alih
                    // di Front Plane, tempat keduanya kebetulan sama sehingga
                    // transformasi yang hilang pun akan lulus.
                    var sketsaPosisi = SketsaPosisi(FindFeature(holeFeatureName));
                    var posisiModel = KeKoordinatModel(sketsaPosisi, hasil.PositionsSketchSpaceMm);

                    Require(posisiModel.Count == plan.Holes!.Positions.Count,
                        $"Jumlah titik posisi terbaca {posisiModel.Count}, diminta {plan.Holes!.Positions.Count}.");

                    // Dibandingkan dalam koordinat MODEL, kerangka yang sama
                    // dengan permintaan - jadi perbandingannya aritmetika,
                    // bukan terjemahan.
                    var deviasi = new List<double>();
                    foreach (var diminta in plan.Holes!.Positions)
                    {
                        var terdekat = posisiModel
                            .Select(a => Math.Sqrt(Math.Pow(a.X - diminta.X, 2) + Math.Pow(a.Y - diminta.Y, 2) + Math.Pow(a.Z - diminta.Z, 2)))
                            .Min();
                        deviasi.Add(terdekat);
                        Require(terdekat <= 1e-3,
                            $"Posisi lubang meleset {terdekat:0.######} mm dari {diminta}. "
                            + "Kalau ini muncul hanya pada face non-Front, transformasi ruang sketsa yang salah.");
                    }

                    holeReadback = new
                    {
                        featureName = hasil.FeatureName,
                        standard = hasil.Standard,
                        fastenerType = hasil.FastenerType,
                        fastenerSize = hasil.FastenerSize,
                        counterBoreDiameterMm = hasil.CounterBoreDiameterMm,
                        counterBoreDepthMm = hasil.CounterBoreDepthMm,
                        thruHoleDiameterMm = hasil.ThruHoleDiameterMm,
                        sketchPointCount = hasil.SketchPointCount,
                        positionsSketchSpaceMm = hasil.PositionsSketchSpaceMm.Select(t => new { t.X, t.Y, t.Z }).ToArray(),
                        positionsModelMm = posisiModel.Select(t => new { t.X, t.Y, t.Z }).ToArray(),
                        positionDeviationsMm = deviasi.ToArray(),
                    };

                    return holeReadback;
                });
            }

            ISketch SketsaPosisi(IFeature fitur)
            {
                var anak = fitur.GetFirstSubFeature() as IFeature;
                for (var penjaga = 0; anak != null && penjaga < 100; penjaga++)
                {
                    if (anak.GetSpecificFeature2() is ISketch sk) return sk;
                    anak = anak.GetNextSubFeature() as IFeature;
                }
                throw new InvalidOperationException(
                    "Sketsa posisi tidak ditemukan di bawah fitur lubang. "
                    + "ASUMSI YANG PATAH: Hole Wizard menyimpan sketsa posisinya sebagai sub-fitur.");
            }

            List<Titik3D> KeKoordinatModel(ISketch sketsa, IReadOnlyList<Titik3D> ruangSketsa)
            {
                var transform = sketsa.ModelToSketchTransform;
                Require(transform != null, "ModelToSketchTransform null.");
                // Inverse() mengembalikan Object, BUKAN MathTransform - cast
                // eksplisit, kalau tidak seluruh ekspresi menjadi dynamic dan
                // build gagal dengan CS0656 yang sebabnya tidak kelihatan.
                var balik = transform!.Inverse() as IMathTransform;
                Require(balik != null, "IMathTransform.Inverse mengembalikan null atau bukan IMathTransform.");
                var m = balik!.ArrayData as double[];
                Require(m != null && m!.Length >= 12,
                    "ArrayData transformasi tidak berbentuk matriks 16 elemen seperti yang diharapkan.");

                var hasil = new List<Titik3D>();
                foreach (var t in ruangSketsa)
                {
                    // ArrayData: 0..8 rotasi baris-utama, 9..11 translasi,
                    // 12 skala. Dikerjakan dalam METER lalu dikembalikan ke mm.
                    var x = t.X / 1000.0;
                    var y = t.Y / 1000.0;
                    var z = t.Z / 1000.0;
                    var skala = m![12] == 0 ? 1.0 : m[12];
                    var mx = (m[0] * x + m[3] * y + m[6] * z) * skala + m[9];
                    var my = (m[1] * x + m[4] * y + m[7] * z) * skala + m[10];
                    var mz = (m[2] * x + m[5] * y + m[8] * z) * skala + m[11];
                    hasil.Add(new Titik3D(mx * 1000.0, my * 1000.0, mz * 1000.0));
                }
                return hasil;
            }

            object Verify()
            {
                var box=_reader.BacaKotakPembatas();
                var mass=_reader.BacaPropertiMassa();
                var sketch=(ISketch)(object)FindFeature(sketchName).GetSpecificFeature2();
                Require(box.BodyCount==1,"Body count harus satu.");
                Require(Math.Abs(box.Max.X-box.Min.X-plan.WidthMm)<=1e-6 && Math.Abs(box.Max.Y-box.Min.Y-plan.HeightMm)<=1e-6 &&
                    Math.Abs(box.Max.Z-box.Min.Z-plan.DepthMm)<=1e-6,"Ukuran body tidak cocok.");
                Require(Math.Abs(box.Min.X)<=1e-6&&Math.Abs(box.Min.Y)<=1e-6&&Math.Abs(box.Min.Z)<=1e-6,"Posisi body tidak cocok dengan origin dan normal Front Plane.");
                if (plan.Holes is null)
                    Require(Math.Abs(mass.VolumeMm3-plan.WidthMm*plan.HeightMm*plan.DepthMm)<=1e-3,"Volume body tidak cocok.");
                else
                {
                    // Volume setelah lubang TIDAK dipatok angkanya: diameter
                    // counterbore untuk ISO M6 ditentukan basis data Hole
                    // Wizard, bukan oleh kami, dan menuliskan angkanya berarti
                    // menyalin keluaran tool lalu menyebutnya harapan.
                    //
                    // Yang bisa dipertanggungjawabkan hanya arahnya.
                    Require(mass.VolumeMm3 < plan.WidthMm*plan.HeightMm*plan.DepthMm - 1e-6,
                        "Volume tidak berkurang setelah lubang - lubangnya tidak memotong apa pun.");
                    Require(mass.VolumeMm3 > 0, "Volume nol setelah lubang.");
                }
                Require(sketch.GetConstrainedStatus()==3,"Constraint profil berubah setelah operasi.");
                var dims=DimensionReadings(FindFeature(sketchName));
                Require(dims.Length==2,"Profil harus mempunyai dua dimensi desain.");
                foreach(var pair in dimensionNames.Select((name,i)=>new{name,value=i==0?plan.WidthMm:plan.HeightMm}))
                {
                    var values=dims.Select(x=>JsonSerializer.SerializeToElement(x)).ToList();
                    Require(values.Any(x=>x.GetProperty("name").GetString()==pair.name && x.GetProperty("drivenState").GetInt32()==2 &&
                        Math.Abs(x.GetProperty("valueMm").GetDouble()-pair.value)<=1e-6),"Dimensi profil tidak cocok setelah baca ulang: "+pair.name);
                }
                var boss=FindFeature(bossName);
                Require(boss.GetErrorCode2(out _)==0,"Extrude mempunyai rebuild error.");
                var depthDimensions=DimensionReadings(boss).Select(x=>JsonSerializer.SerializeToElement(x)).ToList();
                Require(depthDimensions.Any(x=>x.GetProperty("drivenState").GetInt32()==2 && Math.Abs(x.GetProperty("valueMm").GetDouble()-plan.DepthMm)<=1e-6),"Dimensi depth extrude tidak cocok.");
                return new {bodyCount=box.BodyCount,min=box.Min,max=box.Max,volumeMm3=mass.VolumeMm3,profileDimensions=dims,depthDimensions};
            }
            var beforeSave=Verify();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var saveErrors=0;
            var saveWarnings=0;
            var saved=model.Extension.SaveAs(path,0,1,null,ref saveErrors,ref saveWarnings);
            Require(saved && saveErrors==0 && saveWarnings==0 && File.Exists(path) && !model.GetSaveFlag(),"Simpan part gagal atau mempunyai warning.");
            var title=model.GetTitle();
            CreatedDocumentTitle=title;
            _app.CloseDoc(title);
            Require((object)_app.GetOpenDocumentByName(path)==null,"Part pilot belum tertutup.");
            var openErrors=0;
            var openWarnings=0;
            var reopened=_app.OpenDoc6(path,1,1,"",ref openErrors,ref openWarnings) as IModelDoc2;
            Require(reopened!=null && openErrors==0 && openWarnings==0,"Buka ulang part gagal atau mempunyai warning.");
            model=reopened!;
            Require(Equals((object)_app.ActiveDoc,model),"Part yang dibuka ulang belum aktif.");
            Require(model.EditRebuild3(),"Rebuild setelah buka ulang gagal.");
            var afterReopen=Verify();
            return new {status="SW_PASS",scope=plan.Holes is null ? "satu balok pilot Front Plane" : "balok + lubang Hole Wizard pada permukaan atas",
                documentPath=path, holeFeatureName, holes=holeReadback, volumeSebelumLubangMm3=volumeSebelumLubang,
                solidworksRevision=_app.RevisionNumber(),sketchName,featureName=bossName,beforeSave,afterReopen,
                saveErrors,saveWarnings,openErrors,openWarnings,steps=Trace.ToArray()};
        }

        internal IReadOnlyDictionary<string,object> ReadOwnedDimensions(string expectedPath,string[] featureNames)
        {
            PastikanThreadUtama();
            if((object)_app.ActiveDoc is not IModelDoc2 model || !string.Equals(model.GetPathName(),expectedPath,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Dokumen acceptance berbeda dari part milik run.");
            var result=new Dictionary<string,object>(StringComparer.Ordinal);
            var pending=new Stack<IFeature>();
            if(model.FirstFeature() is IFeature first) pending.Push(first);
            var visited=new HashSet<object>();
            while(pending.Count>0 && visited.Count<5000)
            {
                var feature=pending.Pop();
                if(!visited.Add(feature)) continue;
                if(feature.GetNextFeature() is IFeature next) pending.Push(next);
                if(feature.GetFirstSubFeature() is IFeature sub) pending.Push(sub);
                if(feature.GetNextSubFeature() is IFeature sibling) pending.Push(sibling);
                if(!featureNames.Contains(feature.Name)) continue;
                var dimensions=new List<object>();
                var display=feature.GetFirstDisplayDimension() as IDisplayDimension;
                for(var guard=0;display!=null && guard<1000;guard++)
                {
                    var dim=(IDimension)display.GetDimension2(0);
                    var key=string.Join("@",dim.FullName.Split('@').Take(2));
                    if(key.EndsWith("@"+feature.Name,StringComparison.Ordinal))
                        dimensions.Add(new{name=key,valueMm=dim.SystemValue*1000,drivenState=dim.DrivenState});
                    display=feature.GetNextDisplayDimension(display) as IDisplayDimension;
                }
                result[feature.Name]=dimensions.ToArray();
            }
            if(featureNames.Any(name=>!result.ContainsKey(name))) throw new InvalidOperationException("Fitur acceptance tidak ditemukan.");
            return result;
        }
    }
}
