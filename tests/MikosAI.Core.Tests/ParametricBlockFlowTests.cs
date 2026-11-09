using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MikosAI.Core.Capabilities;
using MikosAI.Core.Execution;
using MikosAI.Core.Plan;
using MikosAI.Core.Tools;
using MikosAI.Core.Mcp;
using Xunit;

namespace MikosAI.Core.Tests
{
    public class ParametricBlockFlowTests
    {
        [Theory]
        [InlineData("buat balok 120 x 50 x 12 mm",120,50,12)]
        [InlineData("Buatkan sebuah balok ukuran 100 × 80 × 10 mm.",100,80,10)]
        [InlineData("create a block 12.5*8*1 mm",12.5,8,1)]
        [InlineData("bikin balok 12,5 x 8 x 1 mm",12.5,8,1)]
        public void parses_only_explicit_three_dimensions(string text,double w,double h,double d)
        { var r=BlockChatRequest.Parse(text);Assert.True(r.Handled&&r.Valid);Assert.Equal(w,r.Width);Assert.Equal(h,r.Height);Assert.Equal(d,r.Depth); }

        [Theory]
        [InlineData("buat balok")][InlineData("buat balok 100 x 80 mm")][InlineData("buat balok 10 x 8 x 1 cm")]
        [InlineData("buat balok -1 x 2 x 3 mm")][InlineData("buat balok 100 x 80 x 10 mm lalu lubangi")]
        [InlineData("buat balok 0 x 80 x 10 mm")]
        public void incomplete_or_unsupported_request_never_guesses(string text)
        { var r=BlockChatRequest.Parse(text);Assert.True(r.Handled);Assert.False(r.Valid);Assert.NotEmpty(r.Message); }

        [Fact]
        public void read_only_chat_keeps_provider_route() => Assert.False(BlockChatRequest.Parse("dokumen apa yang aktif?").Handled);

        private static ToolCatalog Catalog(bool expose=true)
        {
            var yaml=File.ReadAllText(RepoFiles.CapabilitiesYaml);
            if(!expose) yaml=yaml.Replace("status: pilot","status: not_implemented");
            return ToolCatalog.Load(RepoFiles.ToolsJson,CapabilityLedger.Parse(yaml));
        }
        private static JsonObject Args(string? id=null) => new JsonObject{["width"]=120,["height"]=50,["depth"]=12,["operationId"]=id??Guid.NewGuid().ToString("N")};

        [Fact]
        public async Task runs_real_planrunner_resolves_values_and_enforces_pilot_review()
        {
            var session=new Session();
            var flow=new ParametricBlockFlow(Catalog(),(w,h,d,p)=>session,id=>"new-"+id+".SLDPRT");
            var result=await flow.ExecuteAsync(Args());
            Assert.Equal(1,session.Executions);Assert.Equal(1,session.Evaluations);
            Assert.Equal(120,session.LastArgs!["width"]!.GetValue<double>());
            Assert.Equal(4,session.Criteria!.Count);
            Assert.Equal(Verdict.Pass,result.Verification!.Verdict);
            Assert.True(result.Verification.ManualReviewRequired);Assert.False(result.IsTaskSuccessful);
            Assert.True(result.ModelChanged);Assert.NotNull(result.Data!["plan"]);
        }
        [Fact]
        public async Task ledger_blocks_before_native_session_creation()
        {
            var creates=0;
            var flow=new ParametricBlockFlow(Catalog(false),(w,h,d,p)=>{creates++;return new Session();},id=>id+".SLDPRT");
            var result=await flow.ExecuteAsync(Args());
            Assert.Equal(0,creates);Assert.False(result.ModelChanged);Assert.Equal(ErrorKind.PolicyBlocked,result.Error!.Kind);
        }
        [Fact]
        public async Task duplicate_operation_is_shared_and_changed_arguments_are_rejected()
        {
            var session=new Session();var flow=new ParametricBlockFlow(Catalog(),(w,h,d,p)=>session,id=>id+".SLDPRT");
            var id=Guid.NewGuid().ToString("N");
            var results=await Task.WhenAll(flow.ExecuteAsync(Args(id)),flow.ExecuteAsync(Args(id)));
            Assert.Same(results[0],results[1]);Assert.Equal(1,session.Executions);
            var changed=Args(id);changed["depth"]=15;
            Assert.False((await flow.ExecuteAsync(changed)).Ok);Assert.Equal(1,session.Executions);
        }
        [Fact]
        public async Task native_failure_does_not_run_acceptance_or_retry()
        {
            var session=new Session{Fail=true};var flow=new ParametricBlockFlow(Catalog(),(w,h,d,p)=>session,id=>id+".SLDPRT");
            var args=Args();var first=await flow.ExecuteAsync(args);var second=await flow.ExecuteAsync(args);
            Assert.Same(first,second);Assert.Equal(1,session.Executions);Assert.Equal(0,session.Evaluations);
            Assert.Equal(Verdict.Fail,first.Verification!.Verdict);Assert.False(first.IsTaskSuccessful);
        }
        [Fact]
        public async Task acceptance_failure_cannot_become_success()
        {
            var session=new Session{BadAcceptance=true};var flow=new ParametricBlockFlow(Catalog(),(w,h,d,p)=>session,id=>id+".SLDPRT");
            var result=await flow.ExecuteAsync(Args());Assert.Equal(Verdict.Fail,result.Verification!.Verdict);Assert.False(result.IsTaskSuccessful);
        }
        [Fact]
        public async Task mcp_preserves_mutation_and_review_even_without_data()
        {
            var invalid=ToolResult.Failure(ParametricBlockFlow.CapabilityId,ErrorKind.InvalidArgument,"missing dimensions");
            invalid.IsMutation=true;invalid.ModelChanged=false;
            var envelope=McpRequestHandler.BuildToolCallResult(invalid);
            var wire=JsonNode.Parse(envelope["content"]![0]!["text"]!.GetValue<string>())!;
            Assert.False(wire["modelChanged"]!.GetValue<bool>());Assert.True(envelope["isError"]!.GetValue<bool>());
            var flow=new ParametricBlockFlow(Catalog(),(w,h,d,p)=>new Session(),id=>id);
            var pilot=McpRequestHandler.BuildToolCallResult(await flow.ExecuteAsync(Args()));
            var payload=JsonNode.Parse(pilot["content"]![0]!["text"]!.GetValue<string>())!;
            Assert.True(payload["modelChanged"]!.GetValue<bool>());
            Assert.True(payload["verification"]!["manualReviewRequired"]!.GetValue<bool>());
            Assert.False(payload["taskSuccessful"]!.GetValue<bool>());
        }
        [Theory]
        [InlineData(0)][InlineData(-1)][InlineData(10001)]
        public async Task rejects_bad_sizes_before_factory(double value)
        {
            var creates=0;var flow=new ParametricBlockFlow(Catalog(),(w,h,d,p)=>{creates++;return new Session();},id=>id);
            var args=Args();args["width"]=value;
            Assert.False((await flow.ExecuteAsync(args)).ModelChanged);Assert.Equal(0,creates);
        }
        private sealed class Session : IBlockExecutionSession
        {
            public int Executions,Evaluations;public bool Fail,BadAcceptance;public JsonObject? LastArgs;public IReadOnlyList<AcceptanceCriterion>? Criteria;
            public Task<ToolResult> ExecuteAsync(string name,JsonObject args,CancellationToken cancellationToken=default)
            {
                Executions++;LastArgs=args;
                var result=Fail ? ToolResult.Failure(ParametricBlockFlow.CapabilityId,ErrorKind.VerificationFailed,"native failed")
                    : new ToolResult{Ok=true,CapabilityId=ParametricBlockFlow.CapabilityId,Verification=VerificationGate.Evaluate(new[]{VerificationCheck.Pass("native")}),Evidence=new ToolEvidence{DocumentPath="new.SLDPRT"}};
                result.IsMutation=true;result.ModelChanged=true;return Task.FromResult(result);
            }
            public Task<IReadOnlyList<VerificationCheck>> EvaluateAsync(IReadOnlyList<AcceptanceCriterion> criteria,CancellationToken cancellationToken=default)
            { Evaluations++;Criteria=criteria;return Task.FromResult<IReadOnlyList<VerificationCheck>>(new[]{BadAcceptance?VerificationCheck.Fail("fresh","changed"):VerificationCheck.Pass("fresh")}); }
        }
    }
}
