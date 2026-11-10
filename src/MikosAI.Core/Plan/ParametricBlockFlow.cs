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
        private readonly Func<double,double,double,HoleSpec?,string,IBlockExecutionSession> _session;
        private readonly Func<string,string> _outputPath;
        private readonly SemaphoreSlim _serial=new SemaphoreSlim(1,1);
        private readonly Dictionary<string,(string fingerprint,Lazy<Task<ToolResult>> result)> _operations=new Dictionary<string,(string,Lazy<Task<ToolResult>>)>(StringComparer.Ordinal);
        public ParametricBlockFlow(ToolCatalog catalog,Func<double,double,double,HoleSpec?,string,IBlockExecutionSession> session,Func<string,string> outputPath)
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
                if (args.Any(p=>!new[]{"width","height","depth","operationId","holes"}.Contains(p.Key)))
                    return Task.FromResult(Reject("Argumen tidak didukung; hanya width, height, depth dalam mm, operationId, dan holes opsional."));
                double Number(string key) => System.Text.Json.JsonSerializer.SerializeToElement(args[key]).GetDouble();
                var w=Number("width"); var h=Number("height"); var d=Number("depth");
                if (!new[]{w,h,d}.AllValid()) return Task.FromResult(Reject("Ukuran harus antara 0,001 dan 10.000 mm."));
                HoleSpec? holes=null;
                if (args["holes"] is JsonObject lubang)
                {
                    var tolak=PeriksaLubang(lubang,w,h,d,out holes);
                    if (tolak!=null) return Task.FromResult(Reject(tolak));
                }
                else if (args.ContainsKey("holes"))
                    return Task.FromResult(Reject("holes harus berupa objek."));
                var id=args["operationId"]?.GetValue<string>() ?? string.Empty;
                if (!System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-f0-9]{32}$")) return Task.FromResult(Reject("operationId harus UUID 32 karakter hex kecil; gunakan ID yang sama saat memeriksa hasil panggilan sebelumnya."));
                // Sidik jari memuat LUBANG juga. Tanpa itu, operationId yang sama
                // bisa dipakai ulang untuk plat dengan lubang berbeda dan
                // mendapat hasil plat lama - jawaban yang salah dengan ID yang
                // benar.
                var fingerprint=FormattableString.Invariant($"{w:R}|{h:R}|{d:R}")+"|"+SidikLubang(holes);
                Lazy<Task<ToolResult>> operation;
                lock(_operations)
                {
                    if (_operations.TryGetValue(id,out var existing))
                    {
                        if(existing.fingerprint!=fingerprint) return Task.FromResult(Reject("operationId sudah dipakai untuk ukuran atau lubang yang berbeda."));
                        operation=existing.result;
                    }
                    else
                    {
                        if(_operations.Count>=128) return Task.FromResult(Reject("Batas 128 operasi per sesi tercapai; mulai sesi SolidWorks baru."));
                        operation=new Lazy<Task<ToolResult>>(()=>RunAsync(id,w,h,d,holes,cancellationToken),LazyThreadSafetyMode.ExecutionAndPublication);
                        _operations.Add(id,(fingerprint,operation));
                    }
                }
                return operation.Value;
            }
            catch(Exception e) { return Task.FromResult(Reject("Argumen balok tidak lengkap atau tidak valid: "+e.Message)); }
        }

        private async Task<ToolResult> RunAsync(string id,double w,double h,double d,HoleSpec? holes,CancellationToken ct)
        {
            await _serial.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var path=_outputPath(id);
                var plan=BuildPlan(w,h,d,path,holes);
                // Penolakan ledger/plan terjadi sebelum session/native writer dibuat.
                var validation=new PlanValidator(_catalog).Validate(plan);
                if(!validation.IsValid)
                {
                    var blocked=ToolResult.Failure(CapabilityId,ErrorKind.PolicyBlocked,"Rencana balok belum tersedia/valid: "+string.Join("; ",validation.Errors.Select(e=>e.Message).Concat(validation.ToolsNotAvailable)));
                    blocked.IsMutation=true; blocked.ModelChanged=false; return blocked;
                }
                var session=_session(w,h,d,holes,path);
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

        public static JsonObject BuildPlan(double w,double h,double d,string path) => BuildPlan(w,h,d,path,null);

        public static JsonObject BuildPlan(double w,double h,double d,string path,HoleSpec? holes)
        {
            var plan=BuildPlanBalok(w,h,d,path);
            if (holes is not null)
            {
                // Lubang masuk ke ARGUMEN LANGKAH, bukan disisipkan di luar
                // plan. PlanValidator memeriksa argumen terhadap inputSchema
                // tool, jadi enum standar dan tipe lubang di kontrak ikut
                // ditegakkan - dan plan yang tercatat sama dengan yang
                // dijalankan.
                var posisi=new JsonArray();
                foreach(var t in holes.Positions) posisi.Add(new JsonObject{["x"]=t.X,["y"]=t.Y,["z"]=t.Z});
                plan["steps"]![0]!["arguments"]!["holes"]=new JsonObject{
                    ["holeType"]=holes.HoleType,["standard"]=holes.Standard,["fastenerSize"]=holes.FastenerSize,["positions"]=posisi};
                plan["title"]=$"Plat {w} × {h} × {d} mm, {holes.Positions.Count} lubang {holes.FastenerSize} {holes.HoleType}";
            }
            return plan;
        }

        /// <summary>
        /// Memeriksa lubang SEBELUM SolidWorks disentuh. null berarti sah.
        ///
        /// Posisi wajib berada di permukaan ATAS: z = depth, dan x, y di dalam
        /// balok. Lubang di luar face bukan permintaan yang bisa dipenuhi -
        /// lebih baik ditolak di depan daripada dibuat di tempat lain lalu
        /// ketahuan saat baca ulang.
        ///
        /// Yang TIDAK diperiksa: apakah counterbore menjebol tepi plat. Itu
        /// bergantung pada diameter dari basis data Hole Wizard, yang belum
        /// diketahui sebelum fiturnya dibuat.
        /// </summary>
        internal static string? PeriksaLubang(JsonObject lubang,double w,double h,double d,out HoleSpec? spec)
        {
            spec=null;
            if (lubang.Any(p=>!new[]{"holeType","standard","fastenerSize","positions"}.Contains(p.Key)))
                return "holes hanya menerima holeType, standard, fastenerSize, dan positions.";

            var tipe=lubang["holeType"]?.GetValue<string>();
            var standar=lubang["standard"]?.GetValue<string>();
            var ukuran=lubang["fastenerSize"]?.GetValue<string>();

            // Dibuka SEMPIT, sama dengan kontrak: hanya yang sudah SW_PASS.
            if (tipe!="counterbore")
                return $"holeType '{tipe}' belum dibuka lewat tool ini. Yang sudah terbukti di SolidWorks nyata: counterbore.";
            if (standar is null)
                return "standard wajib dinyatakan - tidak ada default. Yang dibuka: ISO.";
            if (standar!="ISO")
                return $"standard '{standar}' belum terbukti lewat jalur ini. Yang dibuka: ISO.";

            var titik=new List<Titik3D>();
            if (lubang["positions"] is not JsonArray daftar || daftar.Count==0)
                return "positions wajib berisi minimal satu titik {x, y, z} dalam koordinat model.";
            if (daftar.Count>64) return "Maksimal 64 lubang per fitur.";
            foreach(var n in daftar)
            {
                double Ambil(string k) => n?[k] is JsonNode v ? System.Text.Json.JsonSerializer.SerializeToElement(v).GetDouble() : double.NaN;
                var x=Ambil("x"); var y=Ambil("y"); var z=Ambil("z");
                if (double.IsNaN(x)||double.IsNaN(y)||double.IsNaN(z))
                    return "Setiap posisi wajib punya x, y, dan z dalam mm.";
                if (Math.Abs(z-d)>1e-6)
                    return $"Posisi ({x}, {y}, {z}) tidak berada di permukaan atas: z harus {d} (= depth).";
                if (x<=0||x>=w||y<=0||y>=h)
                    return $"Posisi ({x}, {y}, {z}) di luar permukaan atas. Balok menempati x 0..{w}, y 0..{h}.";
                titik.Add(new Titik3D(x,y,z));
            }
            if (titik.Select(t=>FormattableString.Invariant($"{t.X:R}|{t.Y:R}")).Distinct().Count()!=titik.Count)
                return "Ada posisi lubang yang kembar.";

            try { spec=HoleSpec.Buat(tipe,standar,ukuran,titik); }
            catch(ArgumentException e) { return e.Message; }
            return null;
        }

        private static string SidikLubang(HoleSpec? holes)
            => holes is null ? "-" : holes.HoleType+"|"+holes.Standard+"|"+holes.FastenerSize+"|"+
               string.Join(";",holes.Positions.Select(t=>FormattableString.Invariant($"{t.X:R},{t.Y:R},{t.Z:R}")));

        private static JsonObject BuildPlanBalok(double w,double h,double d,string path)
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
