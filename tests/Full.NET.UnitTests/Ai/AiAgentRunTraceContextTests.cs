using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>运行只捕获可信活动的有限 W3C 上下文，不保存正文、Baggage 或任意追踪扩展。</summary>
[TestClass, DoNotParallelize]
public sealed class AiAgentRunTraceContextTests
{
    [TestMethod] [DataRow("recorded")] [DataRow("unrecorded")] [DataRow("hierarchical")] [DataRow("none")]
    public async Task Run_trace_context_creation_captures_only_w3c_context_async(string mode)
    {
        var parent = Activity.Current; Activity.Current = null;
        using var activity = mode == "none" ? null : new Activity("trusted-request")
            .SetIdFormat(mode == "hierarchical" ? ActivityIdFormat.Hierarchical : ActivityIdFormat.W3C).Start();
        try
        {
            if (activity is not null) { activity.ActivityTraceFlags = mode == "recorded" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;
                activity.TraceStateString = "secret=private"; activity.AddBaggage("secret-user", "private"); }
            var commands = Substitute.For<ICommandExecutor>();
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Guid.CreateVersion7());
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var store = new AiAgentRunStore(Substitute.For<IQueryExecutor>(), commands, Substitute.For<ICommandTransaction>(), ids, clock,
                Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
            await store.CreateOrGetAsync(new(Guid.CreateVersion7(), new string('a',64), "host", null, Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), "stamp", "host", "host", "single", 1, "{}", DateTimeOffset.UtcNow.AddMinutes(5)));
            var values = (IReadOnlyDictionary<string, object?>)commands.ReceivedCalls().Single().GetArguments()[1]!;
            Assert.IsTrue(values.TryGetValue("TraceParent", out var trace));
            Assert.AreEqual(mode is "recorded" or "unrecorded" ? activity!.Id : null, trace);
        }
        finally { Activity.Current = parent; }
    }

    [TestMethod] [DataRow("00")] [DataRow("01")]
    public void Run_trace_context_reads_only_ids_and_sampling(string flags)
    {
        Assert.IsTrue(AiAgentRunTraceContext.TryRead("00-0123456789abcdef0123456789abcdef-0123456789abcdef-" + flags, out var context));
        Assert.AreEqual("0123456789abcdef0123456789abcdef", context.TraceId.ToHexString());
        Assert.AreEqual("0123456789abcdef", context.SpanId.ToHexString()); Assert.IsTrue(context.IsRemote);
        Assert.AreEqual(flags == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None, context.TraceFlags);
        Assert.IsNull(context.TraceState);
    }

    [TestMethod]
    [DataRow(null)] [DataRow("")] [DataRow("secret")]
    [DataRow("00-00000000000000000000000000000000-0123456789abcdef-01")]
    [DataRow("00-0123456789abcdef0123456789abcdef-0000000000000000-01")]
    [DataRow("01-0123456789abcdef0123456789abcdef-0123456789abcdef-01")]
    [DataRow("ff-0123456789abcdef0123456789abcdef-0123456789abcdef-01")]
    [DataRow("00-0123456789ABCDEF0123456789abcdef-0123456789abcdef-01")]
    [DataRow("00-0123456789abcdef0123456789abcdef-0123456789abcdef-02")]
    [DataRow("00-0123456789abcdef0123456789abcdef-0123456789abcdef-ff")]
    [DataRow("00-0123456789abcdef0123456789abcdef-0123456789abcdef-01 ")]
    [DataRow("00-0123456789abcdef0123456789abcdef_0123456789abcdef-01")]
    public void Run_trace_context_invalid_or_legacy_diagnostic_values_are_ignored(string? value)
    {
        Assert.IsFalse(AiAgentRunTraceContext.TryRead(value, out var context)); Assert.AreEqual(default, context);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Run_trace_context_link_preserves_worker_parent_and_drops_extensions(bool recorded)
    {
        using var capture = new AgentTelemetryCapture();
        var origin = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
            recorded ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None, "secret=private", true);
        using (var observation = AgentExecutionTelemetry.Start(Guid.CreateVersion7(), "run"))
        { observation.LinkContext(origin); observation.LinkContext(new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded)); }
        var span = capture.Spans.Single(); var link = span.Links.Single();
        Assert.AreEqual(origin.TraceId, link.Context.TraceId); Assert.AreEqual(origin.SpanId, link.Context.SpanId);
        Assert.AreEqual(origin.TraceFlags, link.Context.TraceFlags); Assert.IsNull(link.Context.TraceState);
        Assert.IsFalse(link.Tags?.Any() ?? false); Assert.IsFalse(span.Baggage.Any());
        Assert.AreEqual(capture.Root.TraceId, span.TraceId); Assert.AreEqual(capture.Root.SpanId, span.ParentSpanId);
        Assert.AreSame(capture.Root, Activity.Current);
        foreach (var measurement in capture.Measurements) Assert.IsFalse(measurement.Tags.Any(t => t.Key.Contains("trace", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Run_trace_context_empty_then_valid_link_remains_available()
    {
        using var capture = new AgentTelemetryCapture(); using (var observation = AgentExecutionTelemetry.Start(Guid.CreateVersion7(), "run"))
        { observation.LinkContext(default); observation.LinkContext(new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None)); }
        Assert.HasCount(1, capture.Spans.Single().Links);
    }

    [TestMethod]
    public void Run_trace_context_disposed_observation_ignores_late_link()
    {
        using var capture = new AgentTelemetryCapture(); var observation = AgentExecutionTelemetry.Start(Guid.CreateVersion7(), "run");
        observation.Dispose(); observation.LinkContext(new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None));
        Assert.IsFalse(capture.Spans.Single().Links.Any()); Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod]
    public async Task Run_trace_context_creation_freezes_origin_before_query_callback_async()
    {
        using var origin = new Activity("trusted-origin").SetIdFormat(ActivityIdFormat.W3C).Start();
        Activity? changed = null;
        var (store, queries, commands) = Store();
        queries.QuerySingleOrDefaultAsync<Guid>(AiAgentRunSql.FindByClientRequest, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { changed = new Activity("query-callback").SetIdFormat(ActivityIdFormat.W3C).Start(); return Guid.Empty; });
        try
        {
            await store.CreateOrGetAsync(Draft());
            var values = (IReadOnlyDictionary<string, object?>)commands.ReceivedCalls().Single().GetArguments()[1]!;
            Assert.AreEqual(origin.Id, values["TraceParent"]);
        }
        finally { changed?.Dispose(); Activity.Current = origin; }
    }

    [TestMethod]
    public async Task Run_trace_context_idempotent_retry_does_not_write_new_origin_async()
    {
        using var retry = new Activity("retry-request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var (store, queries, commands) = Store(); var existing = Guid.CreateVersion7();
        queries.QuerySingleOrDefaultAsync<Guid>(AiAgentRunSql.FindByClientRequest, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(existing);
        Assert.AreEqual(existing, await store.CreateOrGetAsync(Draft())); Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }

    private static AgentRunDraft Draft() => new(Guid.CreateVersion7(), new string('a',64), "host", null, Guid.CreateVersion7(),
        Guid.CreateVersion7(), Guid.CreateVersion7(), "stamp", "host", "host", "single", 1, "{}", DateTimeOffset.UtcNow.AddMinutes(5));
    private static (AiAgentRunStore Store, IQueryExecutor Queries, ICommandExecutor Commands) Store()
    {
        var queries = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Guid.CreateVersion7()); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return (new(queries, commands, Substitute.For<ICommandTransaction>(), ids, clock, Options.Create(new DatabaseOptions())), queries, commands);
    }
}
