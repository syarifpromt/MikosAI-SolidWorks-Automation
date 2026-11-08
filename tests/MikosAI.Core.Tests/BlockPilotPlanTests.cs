using System;
using System.IO;
using System.Text.Json.Nodes;
using MikosAI.Core.Plan;
using Xunit;

namespace MikosAI.Core.Tests
{
    public class BlockPilotPlanTests
    {
        private static JsonObject Plan() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoFiles.Root,"examples","block-pilot.plan.json")))!.AsObject();
        [Fact]
        public void resolves_actual_plan_and_parameter_changes()
        {
            var plan=Plan();
            plan["parameters"]!["width"]!["value"]=120;
            plan["parameters"]!["height"]!["value"]=50;
            plan["parameters"]!["depth"]!["value"]=12;
            var result=BlockPilotPlan.Read(plan);
            Assert.Equal(120,result.WidthMm);
            Assert.Equal(50,result.HeightMm);
            Assert.Equal(12,result.DepthMm);
            Assert.Equal(120,result.Arguments[1]["width"]!.GetValue<double>());
            Assert.Equal("$width",plan["steps"]![1]!["arguments"]!["width"]!.GetValue<string>());
        }
        [Theory]
        [InlineData(0)] [InlineData(-1)] [InlineData(10001)]
        public void rejects_invalid_size_before_document_creation(double width)
        {
            var p=Plan(); p["parameters"]!["width"]!["value"]=width;
            Assert.Throws<ArgumentException>(()=>BlockPilotPlan.Read(p));
        }
        [Fact]
        public void rejects_other_operation_and_unanchored_or_undimensioned_profile()
        {
            var p=Plan(); p["steps"]![3]!["tool"]="extrude_cut";
            Assert.Throws<ArgumentException>(()=>BlockPilotPlan.Read(p));
            p=Plan(); p["steps"]![1]!["arguments"]!["addDimensions"]=false;
            Assert.Throws<ArgumentException>(()=>BlockPilotPlan.Read(p));
            p=Plan(); p["steps"]![1]!["arguments"]!["corner"]!["x"]=10;
            Assert.Throws<ArgumentException>(()=>BlockPilotPlan.Read(p));
        }
        [Fact]
        public void rejects_unimplemented_extrude_options()
        {
            var p=Plan(); p["steps"]![3]!["arguments"]!["direction"]="both";
            Assert.Throws<ArgumentException>(()=>BlockPilotPlan.Read(p));
            p=Plan(); p["steps"]![3]!["arguments"]!["draftAngle"]=5;
            Assert.Throws<ArgumentException>(()=>BlockPilotPlan.Read(p));
        }

        [Fact]
        public void never_ignores_blocking_question_or_unknown_acceptance()
        {
            var p=Plan(); p["openQuestions"]!.AsArray().Add(new JsonObject{["question"]="Ukuran?",["blocking"]=true});
            Assert.Throws<ArgumentException>(()=>BlockPilotPlan.Read(p));
            p=Plan(); p["acceptance"]![0]!["check"]="custom_check";
            Assert.Throws<ArgumentException>(()=>BlockPilotPlan.Read(p));
        }
    }
}
