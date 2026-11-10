using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MikosAI.Core.Execution;
using MikosAI.Core.Plan;
using MikosAI.Core.Threading;
using MikosAI.Core.Tools;

namespace MikosAI.Addin
{
    /// <summary>Satu session per run: executor native dan acceptance fresh-read sesudah reopen.</summary>
    internal sealed class NativeBlockSession : IBlockExecutionSession
    {
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly Func<SolidWorksBlockWriter> _writer;
        private readonly Func<SolidWorksReader?> _reader;
        private readonly double _width,_height,_depth;
        private readonly HoleSpec? _holes;
        private readonly string _path;
        private readonly bool _review;
        private readonly HostDiagnostics _diagnostics;
        private SolidWorksBlockWriter? _usedWriter;
        private ToolResult? _result;
        internal NativeBlockSession(IMainThreadDispatcher dispatcher,Func<SolidWorksBlockWriter> writer,Func<SolidWorksReader?> reader,
            double width,double height,double depth,HoleSpec? holes,string path,bool review,HostDiagnostics diagnostics)
        { _dispatcher=dispatcher;_writer=writer;_reader=reader;_width=width;_height=height;_depth=depth;_holes=holes;_path=path;_review=review;_diagnostics=diagnostics; }

        public Task<ToolResult> ExecuteAsync(string name,JsonObject arguments,CancellationToken cancellationToken=default)
            => _dispatcher.InvokeAsync(()=>
            {
                if(name!=ParametricBlockFlow.ToolName) return ToolResult.Failure(ParametricBlockFlow.CapabilityId,ErrorKind.ToolUnavailable,"Tool di luar session balok.");
                SolidWorksBlockWriter? writer=null;
                try
                {
                    writer=_writer();
                    _usedWriter=writer;
                    var raw=writer.Run(BlockPilotPlan.Create(_width,_height,_depth,_holes),_path,null);
                    var json=JsonSerializer.SerializeToElement(raw);
                    var data=json.EnumerateObject().ToDictionary(p=>p.Name,p=>(object?)p.Value.Clone());
                    data["width"]=_width;data["height"]=_height;data["depth"]=_depth;
                    var setelahBuka=json.GetProperty("afterReopen");
                    var checks=new List<VerificationCheck>{VerificationCheck.Pass("save_reopen_readback",setelahBuka.Clone())};
                    var fitur=new List<string>{json.GetProperty("sketchName").GetString()!,json.GetProperty("featureName").GetString()!};
                    if(_holes is not null)
                    {
                        // Yang dilaporkan sebagai `holes` adalah pembacaan SETELAH
                        // reopen, bukan data dari langkah hole_wizard. Writer
                        // mengisi keduanya; hanya yang kedua yang jujur disebut
                        // terverifikasi dari berkas tersimpan.
                        var lubang=setelahBuka.GetProperty("holes");
                        data["holes"]=lubang.Clone();
                        checks.Add(VerificationCheck.Pass("holes_after_reopen",lubang.Clone()));
                        fitur.Add(lubang.GetProperty("featureName").GetString()!);
                    }
                    _result=new ToolResult{Ok=true,CapabilityId=ParametricBlockFlow.CapabilityId,IsMutation=true,ModelChanged=true,Data=data,
                        Verification=VerificationGate.Evaluate(checks,manualReviewRequired:_review),
                        Evidence=new ToolEvidence{DocumentPath=_path,SolidWorksRevision=json.GetProperty("solidworksRevision").GetString(),
                            FeatureNames=fitur.ToArray()}};
                    return _result;
                }
                catch(Exception e)
                {
                    _result=ToolResult.Failure(ParametricBlockFlow.CapabilityId,ErrorKind.VerificationFailed,e.Message);
                    _result.IsMutation=true;
                    _result.ModelChanged=writer?.CreatedDocumentTitle!=null ? true : (bool?)null;
                    _result.ReadBackFailed=_result.ModelChanged is null ? "Perubahan dokumen belum bisa ditentukan: "+e.Message : null;
                    _result.Data=new Dictionary<string,object?>{["createdDocumentTitle"]=writer?.CreatedDocumentTitle,["steps"]=writer?.Trace.ToArray()};
                    return _result;
                }
            },cancellationToken);

        public Task<IReadOnlyList<VerificationCheck>> EvaluateAsync(IReadOnlyList<AcceptanceCriterion> criteria,CancellationToken cancellationToken=default)
            => _dispatcher.InvokeAsync<IReadOnlyList<VerificationCheck>>(()=>
            {
                var reader=_reader();
                if(reader is null || _result?.Ok!=true)
                    return new[]{VerificationCheck.Blocked("acceptance_readback","Tidak ada hasil native yang bisa dibaca ulang.")};
                var errorsBefore=_diagnostics.Errors.Count;
                var doc=reader.BacaDokumen();
                if(!string.Equals(doc.Path,_path,StringComparison.OrdinalIgnoreCase))
                    return new[]{VerificationCheck.Blocked("acceptance_document_identity","Dokumen aktif berbeda dari part milik run.")};
                var tree=reader.BacaFitur();
                var sketches=reader.BacaSketsa(null);
                var box=reader.BacaKotakPembatas();
                var mass=reader.BacaPropertiMassa();
                if(errorsBefore!=_diagnostics.Errors.Count)
                    return new[]{VerificationCheck.Blocked("acceptance_readback","Pembacaan acceptance parsial; lihat diagnostik host.")};
                // Hanya profil dan boss. Nama fitur lubang ikut tercatat di
                // evidence, tapi dimensinya milik basis data Hole Wizard, bukan
                // dimensi desain yang kita tetapkan.
                var names=_result.Evidence!.FeatureNames!.Take(2).ToArray();
                var dimensions=_usedWriter!.ReadOwnedDimensions(_path,names);
                var profile=JsonSerializer.SerializeToElement(dimensions[names[0]]);
                var boss=JsonSerializer.SerializeToElement(dimensions[names[1]]);
                var profileValues=profile.EnumerateArray().OrderBy(x=>x.GetProperty("valueMm").GetDouble()).ToArray();
                var expectedValues=new[]{_width,_height}.OrderBy(x=>x).ToArray();
                var dimensionsMatch=profileValues.Length==2 && Enumerable.Range(0,2).All(i=>profileValues[i].GetProperty("drivenState").GetInt32()==2 && Math.Abs(profileValues[i].GetProperty("valueMm").GetDouble()-expectedValues[i])<=1e-6)
                    && boss.GetArrayLength()==1 && boss[0].GetProperty("drivenState").GetInt32()==2 && Math.Abs(boss[0].GetProperty("valueMm").GetDouble()-_depth)<=1e-6;
                var checks=new List<VerificationCheck>();
                checks.Add(dimensionsMatch ? VerificationCheck.Pass("fresh_driving_dimensions",dimensions)
                    : VerificationCheck.Fail("fresh_driving_dimensions","Dimensi desain berubah setelah operasi native.",dimensions));
                foreach(var criterion in criteria)
                {
                    bool? valid=criterion.Check switch
                    {
                        "rebuild_clean"=>!tree.RebuildRequired && tree.Features.Count>0 && tree.Features.All(f=>!f.HasError),
                        "all_sketches_fully_defined"=>sketches.Count==1 && sketches.All(s=>s.ConstrainedStatus==SketchConstraintStatus.FullyDefined),
                        // Dengan lubang, volume TIDAK dipatok angkanya - diameter
                        // counterbore dari basis data, bukan dari kita. Yang
                        // dipatok arahnya: berkurang, tapi tidak habis.
                        "nonzero_volume"=>box.BodyCount==1 && (_holes is null
                            ? Math.Abs(mass.VolumeMm3-_width*_height*_depth)<=1e-3
                            : mass.VolumeMm3>0 && mass.VolumeMm3<_width*_height*_depth-1e-6),
                        "bounding_box_within"=>box.BodyCount==1 && Math.Abs(box.Min.X)<=1e-6 && Math.Abs(box.Min.Y)<=1e-6 && Math.Abs(box.Min.Z)<=1e-6 &&
                            Math.Abs(box.Max.X-_width)<=1e-6 && Math.Abs(box.Max.Y-_height)<=1e-6 && Math.Abs(box.Max.Z-_depth)<=1e-6,
                        _=>(bool?)null,
                    };
                    object observed=criterion.Check switch
                    {
                        "rebuild_clean"=>new{tree.RebuildRequired,errorFeatures=tree.Features.Where(f=>f.HasError).Select(f=>f.Name).ToArray()},
                        "all_sketches_fully_defined"=>sketches.Select(s=>new{s.Name,status=s.ConstrainedStatus.ToString(),s.DrivingDimensionCount}).ToArray(),
                        "nonzero_volume"=>new{mass.VolumeMm3,box.BodyCount},
                        _=>new{box.Min,box.Max,box.BodyCount},
                    };
                    checks.Add(valid is null ? VerificationCheck.Blocked(criterion.Id,"Acceptance belum didukung: "+criterion.Check,criterion.Required)
                        : valid==true ? VerificationCheck.Pass(criterion.Id,observed,criterion.Required)
                        : VerificationCheck.Fail(criterion.Id,"Baca ulang acceptance gagal: "+criterion.Check,required:criterion.Required));
                }
                return checks;
            },cancellationToken);
    }
}
