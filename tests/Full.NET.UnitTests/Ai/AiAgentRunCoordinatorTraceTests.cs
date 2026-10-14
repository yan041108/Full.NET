using System.Diagnostics;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实协调器从持久记录关联原请求，保持 Worker 父跨度、会话复核和租约 fencing。</summary>
public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod, DoNotParallelize] [DataRow("00")] [DataRow("01")]
    public async Task Run_trace_context_worker_links_persisted_origin_async(string flags)
    {
        const string prefix = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-";
        using var capture = new AgentTelemetryCapture();
        var fixture = await CreateFixtureAsync(configureRecord: record => record.TraceParent = prefix + flags);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        var span = capture.Spans.Single(s => Equals(s.GetTagItem("ai.stage"), "run")); var link = span.Links.Single();
        Assert.AreEqual("0123456789abcdef0123456789abcdef", link.Context.TraceId.ToHexString());
        Assert.AreEqual("0123456789abcdef", link.Context.SpanId.ToHexString());
        Assert.AreEqual(flags == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None, link.Context.TraceFlags);
        Assert.AreEqual(capture.Root.TraceId, span.TraceId); Assert.AreEqual(capture.Root.SpanId, span.ParentSpanId);
        AssertStatusCommit(fixture.Commands, "completed"); Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod, DoNotParallelize] [DataRow(null)] [DataRow("secret-invalid")]
    public async Task Run_trace_context_old_or_bad_metadata_does_not_change_execution_async(string? value)
    {
        using var capture = new AgentTelemetryCapture(); var fixture = await CreateFixtureAsync(configureRecord: record => record.TraceParent = value);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync()); AssertStatusCommit(fixture.Commands, "completed");
        Assert.IsFalse(capture.Spans.Any(s => s.Links.Any()));
        Assert.IsFalse(capture.Spans.SelectMany(s => s.TagObjects).Any(t => Equals(t.Value, value)));
    }

    [TestMethod, DoNotParallelize]
    public async Task Run_trace_context_fenced_claim_does_not_read_or_link_a_run_async()
    {
        using var capture = new AgentTelemetryCapture(); var fixture = await CreateFixtureAsync(configureCommands: commands =>
            commands.ExecuteAsync(AiAgentRunSql.AcquireLeaseSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0));
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync()); Assert.IsFalse(capture.Spans.Any(s => s.Links.Any()));
        Assert.AreEqual(0, fixture.Factory.ReceivedCalls().Count());
    }

    [TestMethod, DoNotParallelize]
    public async Task Run_trace_context_origin_does_not_bypass_session_revocation_async()
    {
        using var capture = new AgentTelemetryCapture(); var fixture = await CreateFixtureAsync(bindingValid: false,
            configureRecord: record => record.TraceParent = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync()); AssertStatusCommit(fixture.Commands, "authorization_required");
        Assert.HasCount(1, capture.Spans.Single(s => Equals(s.GetTagItem("ai.stage"), "run")).Links);
        Assert.AreEqual(0, fixture.Factory.ReceivedCalls().Count());
    }

    [TestMethod, DoNotParallelize]
    [DataRow("attempts")] [DataRow("duration")] [DataRow("sample")] [DataRow("started")] [DataRow("stopped")]
    public async Task Run_trace_context_listener_failure_does_not_change_completion_async(string fault)
    {
        using var capture = new AgentTelemetryCapture(throwMeasurement: fault is "attempts" or "duration" ? fault : null,
            throwTrace: fault is "sample" or "started" or "stopped" ? fault : null);
        var fixture = await CreateFixtureAsync(configureRecord: record => record.TraceParent = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync()); AssertStatusCommit(fixture.Commands, "completed");
        Assert.AreSame(capture.Root, Activity.Current);
    }
}
