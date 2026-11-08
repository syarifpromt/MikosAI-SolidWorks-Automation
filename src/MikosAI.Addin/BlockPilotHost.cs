using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MikosAI.Core.Plan;

namespace MikosAI.Addin
{
    /// <summary>
    /// Acceptance harness manual: hanya request dari golden-balok.ps1.
    /// Tidak menambahkan tool AI atau mengubah ledger. Satu request dikonsumsi
    /// sekali, tidak di-retry, dan tidak menghapus model yang gagal.
    /// </summary>
    internal sealed class BlockPilotHost
    {
        private readonly Func<SolidWorksBlockWriter> _writer;
        private bool _running;
        internal BlockPilotHost(Func<SolidWorksBlockWriter> writer) { _writer=writer; }

        internal void Poll()
        {
            if (_running) return;
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MikosAI","block-pilot");
            var path=Path.Combine(folder,"request.json");
            if (!File.Exists(path)) return;
            _running=true;
            var processing=Path.Combine(folder,"processing.json");
            string? requestId=null;
            SolidWorksBlockWriter? writer=null;
            object result;
            try
            {
                if (File.Exists(processing)) return; // run terputus harus ditinjau, bukan diulang.
                File.Move(path,processing);
                var request=JsonNode.Parse(File.ReadAllText(processing))!.AsObject();
                requestId=request["requestId"]!.GetValue<string>();
                if (!Regex.IsMatch(requestId,"^[a-f0-9]{32}$")) throw new ArgumentException("Request ID tidak valid.");
                if (DateTimeOffset.Parse(request["expiresAtUtc"]!.GetValue<string>())<DateTimeOffset.UtcNow)
                    throw new InvalidOperationException("Request pilot kedaluwarsa; tidak mengubah model.");
                var plan=BlockPilotPlan.Read(request["plan"]!.AsObject());
                var output=request["outputPath"]!.GetValue<string>();
                if (!Path.IsPathRooted(output)) throw new ArgumentException("Output path harus absolut.");
                if (request["plan"]?["document"]?["savePath"]?.GetValue<string>()!=output)
                    throw new ArgumentException("Output path tidak sama dengan document.savePath pada plan.");
                writer=_writer();
                var data=writer.Run(plan,output,request["templatePath"]?.GetValue<string>());
                result=new {requestId,ok=true,data};
            }
            catch(Exception e)
            {
                result=new {requestId,ok=false,status="BLOCKED",error=e.Message,createdDocumentTitle=writer?.CreatedDocumentTitle,
                    steps=writer?.Trace.ToArray(),note="Berhenti; model gagal dibiarkan untuk diperiksa. Tidak ada rollback atau retry otomatis."};
            }
            finally { _running=false; }
            var resultPath=Path.Combine(folder,requestId!=null && Regex.IsMatch(requestId,"^[a-f0-9]{32}$") ? "result-"+requestId+".json" : "invalid-request-result.json");
            var temporaryResult=resultPath+".tmp";
            File.WriteAllText(temporaryResult,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
            File.Move(temporaryResult,resultPath); // polling client tidak membaca JSON setengah ditulis.
            // Arsipkan request agar run yang sama tidak dapat dieksekusi ulang.
            var archiveId=requestId!=null && Regex.IsMatch(requestId,"^[a-f0-9]{32}$") ? requestId : Guid.NewGuid().ToString("N");
            if (File.Exists(processing)) File.Move(processing,Path.Combine(folder,"completed-"+archiveId+".json"));
        }
    }
}
