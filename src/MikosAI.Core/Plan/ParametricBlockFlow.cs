using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using MikosAI.Core.Execution;
using MikosAI.Core.Tools;

namespace MikosAI.Core.Plan
{
    public interface IBlockExecutionSession : IToolExecutor, IAcceptanceEvaluator { }

    /// <summary>Satu jalur produksi bersama untuk chat dan MCP; ledger tetap otoritas.</summary>
    public sealed class ParametricBlockFlow
    {
        public const string ToolName="create_parametric_block";
        public const string CapabilityId="parametric_block";
        private readonly ToolCatalog _catalog;
        private readonly Func<double,double,double,string,IBlockExecutionSession> _session;
        private readonly Func<string,string> _outputPath;
        private readonly SemaphoreSlim _serial=new SemaphoreSlim(1,1);
        private readonly Dictionary<string,(string fingerprint,Lazy<Task<ToolResult>> result)> _operations=new Dictionary<string,(string,Lazy<Task<ToolResult>>)>(StringComparer.Ordinal);
        public ParametricBlockFlow(ToolCatalog catalog,Func<double,double,double,string,IBlockExecutionSession> session,Func<string,string> outputPath)
        { _catalog=catalog; _session=session; _outputPath=outputPath; }

        public Task<ToolResult> ExecuteAsync(JsonObject args,CancellationToken cancellationToken=default)
        {
            ToolResult Reject(string message)
            {
                var r=ToolResult.Failure(CapabilityId,ErrorKind.InvalidArgument,message);
                r.IsMutation=true; r.ModelChanged=false; return r;
            }
            try
            {
                if (args.Any(p=>!new[]{"width","height","depth","operationId"}.Contains(p.Key)))
                    return Task.FromResult(Reject("Argumen tidak didukung; hanya width, height, depth dalam mm dan operationId."));
                double Number(string key) => System.Text.Json.JsonSerializer.SerializeToElement(args[key]).GetDouble();
                var w=Number("width"); var h=Number("height"); var d=Number("depth");
                if (!new[]{w,h,d}.AllValid()) return Task.FromResult(Reject("Ukuran harus antara 0,001 dan 10.000 mm."));
                var id=args["operationId"]?.GetValue<string>() ?? string.Empty;
                if (!System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-f0-9]{32}$")) return Task.FromResult(Reject("operationId harus UUID 32 karakter hex kecil; gunakan ID yang sama saat memeriksa hasil panggilan sebelumnya."));
                var fingerprint=FormattableString.Invariant($"{w:R}|{h:R}|{d:R}");
                Lazy<Task<ToolResult>> operation;
                lock(_operations)
                {
                    if (_operations.TryGetValue(id,out var existing))
                    {
                        if(existing.fingerprint!=fingerprint) return Task.FromResult(Reject("operationId sudah dipakai untuk ukuran berbeda."));
                        operation=existing.result;
                    }
                    else
                    {
                        if(_operations.Count>=128) return Task.FromResult(Reject("Batas 128 operasi per sesi tercapai; mulai sesi SolidWorks baru."));
                        operation=new Lazy<Task<ToolResult>>(()=>RunAsync(id,w,h,d,cancellationToken),LazyThreadSafetyMode.ExecutionAndPublication);
                        _operations.Add(id,(fingerprint,operation));
                    }
                }
                return operation.Value;
            }
            catch(Exception e) { return Task.FromResult(Reject("Argumen balok tidak lengkap atau tidak valid: "+e.Message)); }
        }

        private async Task<ToolResult> RunAsync(string id,double w,double h,double d,CancellationToken ct)
        {
            await _serial.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var path=_outputPath(id);
                var plan=BuildPlan(w,h,d,path);
                // Penolakan ledger/plan terjadi sebelum session/native writer dibuat.
                var validation=new PlanValidator(_catalog).Validate(plan);
                if(!validation.IsValid)
                {
                    var blocked=ToolResult.Failure(CapabilityId,ErrorKind.PolicyBlocked,"Rencana balok belum tersedia/valid: "+string.Join("; ",validation.Errors.Select(e=>e.Message).Concat(validation.ToolsNotAvailable)));
                    blocked.IsMutation=true; blocked.ModelChanged=false; return blocked;
                }
                var session=_session(w,h,d,path);
                var runner=new PlanRunner(new PlanValidator(_catalog),session,session);
                var result=await runner.RunAsync(plan,cancellationToken:ct).ConfigureAwait(false);
                result.Final.ManualReviewRequired |= _catalog.RequiresReview(ToolName);
                var step=result.Steps.SingleOrDefault()?.Result;
                var data=step?.Data==null ? new Dictionary<string,object?>() : step.Data.ToDictionary(p=>p.Key,p=>p.Value);
                data["operationId"]=id; data["runId"]=result.RunId; data["plan"]=plan;
                data["reviewRequired"]=result.Final.ManualReviewRequired;
                data["acceptanceChecks"]=result.AcceptanceChecks;
                return new ToolResult
                {
                    Ok=step?.Ok==true,CapabilityId=CapabilityId,IsMutation=true,ModelChanged=step?.ModelChanged,
                    ReadBackFailed=step?.ReadBackFailed ?? (step?.ModelChanged is null ? "Tidak ada snapshot perubahan lengkap pada run." : null),Data=data,Verification=result.Final,Error=step?.Error,
                    Evidence=step?.Evidence,DurationMs=result.DurationMs,
                };
            }
            catch(OperationCanceledException) { throw; }
            catch(Exception e)
            {
                var failed=ToolResult.Failure(CapabilityId,ErrorKind.Fatal,e.Message);
                failed.IsMutation=true;failed.ModelChanged=null;failed.ReadBackFailed="Tidak bisa memastikan perubahan akibat kegagalan flow: "+e.Message;
                return failed;
            }
            finally { _serial.Release(); }
        }

        public static JsonObject BuildPlan(double w,double h,double d,string path)
            => new JsonObject
            {
                ["schemaVersion"]="1.0",["title"]=$"Balok {w} × {h} × {d} mm",
                ["units"]=new JsonObject{["length"]="mm",["angle"]="deg"},
                ["document"]=new JsonObject{["type"]="part",["savePath"]=path},
                ["parameters"]=new JsonObject{
                    ["width"]=new JsonObject{["value"]=w,["source"]="user_stated"},
                    ["height"]=new JsonObject{["value"]=h,["source"]="user_stated"},
                    ["depth"]=new JsonObject{["value"]=d,["source"]="user_stated"}},
                ["steps"]=new JsonArray{new JsonObject{["id"]="block",["tool"]=ToolName,["arguments"]=new JsonObject{["width"]="$width",["height"]="$height",["depth"]="$depth"}}},
                ["acceptance"]=new JsonArray{
                    new JsonObject{["id"]="rebuild",["check"]="rebuild_clean",["required"]=true},
                    new JsonObject{["id"]="constraints",["check"]="all_sketches_fully_defined",["required"]=true},
                    new JsonObject{["id"]="volume",["check"]="nonzero_volume",["required"]=true},
                    new JsonObject{["id"]="size",["check"]="bounding_box_within",["required"]=true}},
                ["openQuestions"]=new JsonArray(),
            };
    }
}
