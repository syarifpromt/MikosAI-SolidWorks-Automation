using System;
using System.Collections.Generic;
using System.Linq;
using MikosAI.Core.Execution;
using MikosAI.Core.Tools;
using Xunit;

namespace MikosAI.Core.Tests
{
    public class SketchConnectivityTests
    {
        private static SketchConnectivity.Endpoint P(string id, double x = 0, double y = 0)
            => new SketchConnectivity.Endpoint { Id = id, X = x, Y = y };

        private static SketchReading Read(SketchConnectivity.Endpoint[] endpoints, params string[] contacts)
        {
            var reading = new SketchReading { ConstrainedStatus = SketchConstraintStatus.FullyDefined };
            SketchConnectivity.Populate(reading, endpoints, contacts);
            return reading;
        }

        [Fact]
        public void shared_endpoint_counts_branch_once()
        {
            var r = Read(new[] { P("a"), P("t"), P("t"), P("b"), P("t"), P("c") });
            Assert.Equal(3, r.OpenEndpointCount);
            Assert.Equal(1, r.BranchPointCount);
        }

        [Fact]
        public void body_contact_keeps_four_open_endpoints()
        {
            var r = Read(new[] { P("a"), P("b"), P("c"), P("d") }, "c", "c");
            Assert.Equal(4, r.OpenEndpointCount);
            Assert.Equal(0, r.BranchPointCount);
            Assert.Equal(1, r.OpenEndpointOnSegmentCount);
            Assert.Equal(0, r.JoinedEndpointOnSegmentCount);
            Assert.Equal(1, r.DeclaredEndpointOnSegmentCount);
        }

        [Fact]
        public void closed_loop_contact_is_not_lost_or_counted_twice()
        {
            var r = Read(new[] { P("a"), P("b"), P("b"), P("c"), P("c"), P("a") }, "c");
            Assert.Equal(0, r.OpenEndpointCount);
            Assert.Equal(0, r.BranchPointCount);
            Assert.Equal(0, r.OpenEndpointOnSegmentCount);
            Assert.Equal(1, r.JoinedEndpointOnSegmentCount);
        }

        [Fact]
        public void two_closed_loops_have_no_open_endpoints()
        {
            var r = Read(new[] { P("a"), P("b"), P("b"), P("a"), P("c"), P("d"), P("d"), P("c") });
            Assert.Equal(0, r.OpenEndpointCount);
            Assert.Equal(0, r.BranchPointCount);
        }

        [Fact]
        public void nearby_distinct_points_are_not_merged()
        {
            var r = Read(new[] { P("a", 0), P("b", 5), P("c", 0.0000005), P("d", 10) });
            Assert.Equal(4, r.OpenEndpointCount);
            Assert.Equal(2, r.NearlyCoincidentEndpointCount);
            Assert.Equal(SketchConnectivity.ToleranceMm, r.EndpointToleranceMm);
        }

        [Fact]
        public void contact_outside_endpoints_is_rejected()
            => Assert.Throws<ArgumentException>(() => Read(new[] { P("a"), P("b") }, "missing"));

        [Fact]
        public void populated_counts_are_exposed_and_leave_missing_fields()
        {
            var r = Read(new[] { P("a", 0), P("b", 10) });
            r.SegmentsNotAnalyzed = Array.Empty<string>();
            r.Limits = Array.Empty<string>();
            var result = ReadOnlyToolShaper.Sketches(new[] { r });
            var row = ((IEnumerable<object>)result.Data!["sketches"]!).Cast<IReadOnlyDictionary<string, object?>>().Single();
            Assert.Equal(2, row["openEndpointCount"]);
            var missing = (IReadOnlyList<string>)result.Data["fieldsNotPopulated"]!;
            Assert.DoesNotContain("openEndpointCount", missing);
            Assert.DoesNotContain("limits", missing);
            Assert.Contains("undeclared_point_on_segment", (IEnumerable<string>)result.Data["analysisLimits"]!);
        }

        [Fact]
        public void invalid_partition_blocks_verification()
        {
            var r = Read(new[] { P("a"), P("b") }, "a");
            r.JoinedEndpointOnSegmentCount = 1;
            var result = ReadOnlyToolShaper.Sketches(new[] { r });
            Assert.Contains(result.Verification!.Checks,
                c => c.Id == "endpoint_contact_counts_consistent" && c.Verdict == Verdict.Blocked);
        }
    }
}
