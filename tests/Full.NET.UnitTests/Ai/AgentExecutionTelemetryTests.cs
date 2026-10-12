using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Full.NET.AI.Providers.Http;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Full.NET.Agents.Workflows;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实运行边界的观测只携带固定分类和 Trace 关联，不读取模型或工具正文。</summary>
[TestClass, DoNotParallelize]
public sealed class AgentExecutionTelemetryTests
{
    [TestMethod]
    public async Task Telemetry_red_tool_loop_correlates_model_and_tool_async()
    {
        using var capture = new AgentTelemetryCapture();
        var runId = Guid.CreateVersion7(); var operationId = Guid.CreateVersion7();
        var client = Client(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("secret-call", "ai.tools.ping", new Dictionary<string, object?>())])),
            new(new ChatMessage(ChatRole.Assistant, "secret-answer")));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement("secret-output"), null));
        var resolved = 0;
        var result = await new AgentToolLoop().RunAsync(new(runId, AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1,
            "secret-prompt", client, executor, Registry(), _ => { resolved++; return operationId; }));
        Assert.AreEqual("secret-answer", result.Text); Assert.AreEqual(1, resolved);
        capture.Verify(runId, "tool_loop", "completed", 4);
        var tool = capture.Spans.Single(s => s.GetTagItem("ai.stage") as string == "tool");
        Assert.AreEqual(operationId.ToString("D"), tool.GetTagItem("ai.operation.id"));
    }

    [TestMethod]
    public async Task Telemetry_red_workflow_correlates_nodes_async()
    {
        using var capture = new AgentTelemetryCapture();
        var runId = Guid.CreateVersion7(); var state = AgentWorkflowState.Create(Guid.CreateVersion7());
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new AgentModelResult("secret-title", JsonSerializer.SerializeToElement("secret-session"), 1, 1),
            new AgentModelResult("{\"decision\":\"approved\"}", JsonSerializer.SerializeToElement("secret-session"), 1, 1));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement("secret-output"), null));
        var result = await new AgentWorkflowRunner().RunAsync(new(runId, AgentWorkflowRegistry.ChatRenameWorkflowKey,
            AgentWorkflowRegistry.ChatRenameWorkflowVersion, state, Substitute.For<IChatClient>(), runner, executor,
            new AgentToolRegistry([new("ai.chat.sessions.list", 1, "read", "read", true, null),
                new("ai.chat.sessions.rename", 1, "write", "write", true, null)]), _ => Guid.CreateVersion7()));
        Assert.AreEqual(AgentWorkflowRunStatus.Completed, result.Status);
        capture.Verify(runId, "workflow", "completed", 9);
    }

    internal static IChatClient Client(params ChatResponse[] responses)
    {
        var client = Substitute.For<IChatClient>(); var index = 0;
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => responses[index++]); return client;
    }
    internal static AgentToolRegistry Registry() => new([new("ai.tools.ping", 1, "read", "none", true, null)]);

    [TestMethod]
    [DataRow("failed", "ai.tool.failed")]
    [DataRow("cancelled", "ai.tool.cancelled")]
    [DataRow("denied", "ai.tool.permission_required")]
    public async Task Tool_result_classification_preserves_reported_failure_async(string status, string code)
    {
        using var capture = new AgentTelemetryCapture(); var runId = Guid.CreateVersion7();
        var client = Client(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call", "ai.tools.ping", new Dictionary<string, object?>())])),
            new(new ChatMessage(ChatRole.Assistant, "secret-answer")));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(new ToolExecutionResult(status, null, code));
        var result = await new AgentToolLoop().RunAsync(new(runId, AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1,
            "secret-prompt", client, executor, Registry(), _ => Guid.CreateVersion7()));
        Assert.AreEqual("secret-answer", result.Text); capture.Verify(runId, "tool_loop", "completed", 4);
        Assert.AreEqual(status, capture.Spans.Single(s => s.GetTagItem("ai.stage") as string == "tool").GetTagItem("ai.outcome"));
    }

    [TestMethod]
    [DataRow("ai.tool.approval_required", "approval_required")]
    [DataRow("ai.tool.reconciliation_required", "reconciliation_required")]
    public async Task Tool_pause_does_not_invoke_next_model_async(string code, string outcome)
    {
        using var capture = new AgentTelemetryCapture(); var runId = Guid.CreateVersion7();
        var client = Client(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call", "ai.tools.ping", new Dictionary<string, object?>())])));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(new ToolExecutionResult("denied", null, code));
        await new AgentToolLoop().RunAsync(new(runId, AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1,
            "secret-prompt", client, executor, Registry(), _ => Guid.CreateVersion7()));
        capture.Verify(runId, "tool_loop", outcome, 3);
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Completed_cursor_restore_does_not_create_model_or_tool_attempts_async()
    {
        using var capture = new AgentTelemetryCapture(); var runId = Guid.CreateVersion7(); string? checkpoint = null;
        var client = Client(new ChatResponse(new ChatMessage(ChatRole.Assistant, "secret-answer")));
        var executor = Substitute.For<IAgentToolExecutor>();
        var request = new AgentToolLoopRequest(runId, AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1,
            "secret-prompt", client, executor, Registry(), _ => Guid.CreateVersion7(), PersistCheckpoint:
            (payload, _) => { checkpoint = payload; return Task.FromResult(true); });
        await new AgentToolLoop().RunAsync(request); Assert.IsNotNull(checkpoint);
        capture.Spans.Clear(); capture.Measurements.Clear();
        var restored = await new AgentToolLoop().RunAsync(request with { ResumeCheckpoint = checkpoint });
        Assert.AreEqual("secret-answer", restored.Text); capture.Verify(runId, "tool_loop", "completed", 1);
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("sample", false)][DataRow("started", false)][DataRow("stopped", false)]
    [DataRow("sample", true)][DataRow("started", true)][DataRow("stopped", true)]
    public async Task Trace_callback_failure_keeps_original_model_result_or_error_async(string callback, bool fails)
    {
        using var capture = new AgentTelemetryCapture(throwTrace: callback);
        var client = Substitute.For<IChatClient>(); var original = new IOException("secret-error");
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => fails ? Task.FromException<ChatResponse>(original) : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "secret-answer"))));
        var task = new AgentToolLoop().RunAsync(new(Guid.CreateVersion7(), AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1,
            "secret-prompt", client, Substitute.For<IAgentToolExecutor>(), Registry(), _ => Guid.CreateVersion7()));
        if (fails) Assert.AreSame(original, await Assert.ThrowsExactlyAsync<IOException>(() => task));
        else Assert.AreEqual("secret-answer", (await task).Text);
        Assert.AreSame(capture.Root, Activity.Current);
        Assert.AreEqual(4, capture.Measurements.Count);
    }

    [TestMethod]
    [DataRow("attempts")][DataRow("duration")]
    public async Task Metric_callback_failure_keeps_other_measurement_async(string callback)
    {
        using var capture = new AgentTelemetryCapture(throwMeasurement: callback);
        var runId = Guid.CreateVersion7();
        var result = await new AgentToolLoop().RunAsync(new(runId, AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1,
            "secret-prompt", Client(new ChatResponse(new ChatMessage(ChatRole.Assistant, "secret-answer"))),
            Substitute.For<IAgentToolExecutor>(), Registry(), _ => Guid.CreateVersion7()));
        Assert.AreEqual("secret-answer", result.Text); capture.Verify(runId, "tool_loop", "completed", 2);
    }

    [TestMethod]
    [DataRow("run")][DataRow("workflow")][DataRow("tool_loop")][DataRow("node")][DataRow("model")][DataRow("tool")]
    public void Known_stage_and_trace_only_identifiers_are_fixed(string stage)
    {
        using var capture = new AgentTelemetryCapture(); var runId = Guid.CreateVersion7(); var operationId = Guid.CreateVersion7();
        using (var observation = AgentExecutionTelemetry.Start(runId, stage, operationId, 63)) { observation.Outcome = "succeeded"; }
        capture.Verify(runId, stage, "succeeded", 1);
        Assert.AreEqual(operationId.ToString("D"), capture.Spans[0].GetTagItem("ai.operation.id"));
        Assert.AreEqual(63, capture.Spans[0].GetTagItem("ai.step.index"));
    }

    [TestMethod]
    [DataRow(-1)][DataRow(64)][DataRow(int.MaxValue)]
    public void Unknown_tags_and_invalid_step_index_do_not_escape(int index)
    {
        using var capture = new AgentTelemetryCapture();
        using (var observation = AgentExecutionTelemetry.Start(Guid.Empty, "secret-stage", Guid.Empty, index)) { observation.Outcome = "secret-outcome"; }
        Assert.AreEqual(1, capture.Spans.Count); Assert.HasCount(2, capture.Spans[0].TagObjects.ToArray());
        Assert.AreEqual("other", capture.Spans[0].GetTagItem("ai.stage")); Assert.AreEqual("failed", capture.Spans[0].GetTagItem("ai.outcome"));
        Assert.IsTrue(capture.Measurements.All(m => m.Tags.Length == 2)); Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod]
    public void Disposal_records_once_and_no_sampling_still_records_metrics()
    {
        using var capture = new AgentTelemetryCapture(sampleNone: true);
        var observation = AgentExecutionTelemetry.Start(Guid.CreateVersion7(), "model"); observation.Outcome = "succeeded";
        observation.Dispose(); observation.Dispose();
        Assert.HasCount(0, capture.Spans); Assert.HasCount(2, capture.Measurements); Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod]
    public async Task Cancellation_during_model_wait_records_cancelled_without_next_step_async()
    {
        using var capture = new AgentTelemetryCapture(); using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(async call => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>()); return new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty)); });
        var runId = Guid.CreateVersion7();
        var pending = new AgentToolLoop().RunAsync(new(runId, AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1,
            "secret-prompt", client, Substitute.For<IAgentToolExecutor>(), Registry(), _ => Guid.CreateVersion7()), cancellation.Token);
        await entered.Task; Assert.HasCount(0, capture.Measurements); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => pending);
        capture.Verify(runId, "tool_loop", "cancelled", 2);
        Assert.AreEqual("cancelled", capture.Spans.Single(s => s.GetTagItem("ai.stage") as string == "model").GetTagItem("ai.outcome"));
    }

    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task Node_persist_rejection_reports_reconciliation_without_extra_dispatch_async(bool receiptFailure)
    {
        using var capture = new AgentTelemetryCapture(); var runId = Guid.CreateVersion7(); var resolved = 0;
        var executor = Substitute.For<IAgentToolExecutor>(); var runner = Substitute.For<IAgentModelRunner>(); var commits = 0;
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement("secret-output"), null));
        var request = new AgentWorkflowRunRequest(runId, AgentWorkflowRegistry.ChatRenameWorkflowKey,
            AgentWorkflowRegistry.ChatRenameWorkflowVersion, AgentWorkflowState.Create(Guid.CreateVersion7()),
            Substitute.For<IChatClient>(), runner, executor,
            new AgentToolRegistry([new("ai.chat.sessions.list", 1, "read", "read", true, null)]),
            _ => { resolved++; return Guid.CreateVersion7(); }) { PersistNodeAsync = (_, _, _) => Task.FromResult(receiptFailure && ++commits == 1) };
        var result = await new AgentWorkflowRunner().RunAsync(request);
        Assert.AreEqual(AgentWorkflowRunStatus.ReconciliationRequired, result.Status); Assert.AreEqual(1, resolved);
        capture.Verify(runId, "workflow", "reconciliation_required", receiptFailure ? 3 : 2);
        Assert.AreEqual("reconciliation_required", capture.Spans.Single(s => s.GetTagItem("ai.stage") as string == "node").GetTagItem("ai.outcome"));
        await executor.Received(receiptFailure ? 1 : 0).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("failed", "ai.tool.failed")][DataRow("cancelled", "ai.tool.cancelled")][DataRow("denied", "ai.tool.permission_required")]
    public async Task Workflow_tool_result_classification_preserves_reported_outcome_async(string status, string code)
    {
        using var capture = new AgentTelemetryCapture(); var runId = Guid.CreateVersion7();
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(new ToolExecutionResult(status, null, code));
        var runner = Substitute.For<IAgentModelRunner>();
        var result = await new AgentWorkflowRunner().RunAsync(new(runId, AgentWorkflowRegistry.ChatRenameWorkflowKey,
            AgentWorkflowRegistry.ChatRenameWorkflowVersion, AgentWorkflowState.Create(Guid.CreateVersion7()),
            Substitute.For<IChatClient>(), runner, executor,
            new AgentToolRegistry([new("ai.chat.sessions.list", 1, "read", "read", true, null)]), _ => Guid.CreateVersion7()));
        Assert.AreEqual(AgentWorkflowRunStatus.Failed, result.Status); capture.Verify(runId, "workflow", "failed", 3);
        Assert.AreEqual(status, capture.Spans.Single(s => s.GetTagItem("ai.stage") as string == "tool").GetTagItem("ai.outcome"));
        Assert.AreEqual(status, capture.Spans.Single(s => s.GetTagItem("ai.stage") as string == "node").GetTagItem("ai.outcome"));
        await runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task Parent_current_changed_failure_keeps_result_or_original_error_async(bool fails)
    {
        using var capture = new AgentTelemetryCapture(); var original = new IOException("secret-error");
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => fails ? Task.FromException<ChatResponse>(original) : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "secret-answer"))));
        EventHandler<ActivityChangedEventArgs> callback = (_, change) =>
        { if (ReferenceEquals(change.Current, capture.Root)) throw new IOException("secret-listener"); };
        Activity.CurrentChanged += callback;
        try
        {
            var task = new AgentToolLoop().RunAsync(new(Guid.CreateVersion7(), AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1,
                "secret-prompt", client, Substitute.For<IAgentToolExecutor>(), Registry(), _ => Guid.CreateVersion7()));
            if (fails) Assert.AreSame(original, await Assert.ThrowsExactlyAsync<IOException>(() => task));
            else Assert.AreEqual("secret-answer", (await task).Text);
            Assert.AreSame(capture.Root, Activity.Current);
        }
        finally { Activity.CurrentChanged -= callback; }
    }

    [TestMethod]
    public void Publication_recursion_preserves_outer_execution_measurements()
    {
        using var capture = new AgentTelemetryCapture(); var armed = false; var recursive = 0;
        using var publisher = new MeterListener { InstrumentPublished = (instrument, _) =>
        {
            if (!armed || instrument.Meter.Name != AgentTelemetryCapture.Name || instrument.Name != "fullnet.ai.execution.attempts") return;
            recursive++; using var nested = AgentExecutionTelemetry.Start(Guid.CreateVersion7(), "tool"); nested.Outcome = "succeeded";
        } };
        publisher.Start(); armed = true;
        using (var observation = AgentExecutionTelemetry.Start(Guid.CreateVersion7(), "model")) { observation.Outcome = "succeeded"; }
        if (Environment.GetEnvironmentVariable("FULLNET_AI_EXECUTION_FRESH_PROBE") == "1") Assert.AreEqual(1, recursive);
        Assert.HasCount(2, capture.Measurements); Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod]
    public async Task Provider_http_is_child_of_model_in_same_run_trace_async()
    {
        using var capture = new AgentTelemetryCapture(includeProvider: true); var runId = Guid.CreateVersion7();
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                using var invoker = new HttpMessageInvoker(new AiProviderTelemetryHandler("ollama", "chat") { InnerHandler = new HeadersHandler() });
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/?secret=query");
                using var response = await invoker.SendAsync(request, call.Arg<CancellationToken>());
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "secret-answer"));
            });
        await new AgentToolLoop().RunAsync(new(runId, AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1,
            "secret-prompt", client, Substitute.For<IAgentToolExecutor>(), Registry(), _ => Guid.CreateVersion7()));
        Assert.HasCount(3, capture.Spans); Assert.HasCount(4, capture.Measurements);
        var provider = capture.Spans.Single(s => s.Source.Name == AiProviderHttpClients.TelemetryName);
        var model = capture.Spans.Single(s => s.GetTagItem("ai.stage") as string == "model");
        Assert.AreEqual(model.SpanId, provider.ParentSpanId); Assert.AreEqual(capture.Root.TraceId, provider.TraceId);
        Assert.AreEqual(runId.ToString("D"), model.GetTagItem("ai.run.id")); Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod]
    public async Task Loop_step_budget_records_rejection_without_extra_model_call_async()
    {
        using var capture = new AgentTelemetryCapture(); var runId = Guid.CreateVersion7();
        var responses = Enumerable.Range(0, 8).Select(i => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-" + i, "ai.tools.ping", new Dictionary<string, object?>())]))).ToArray();
        var client = Client(responses); var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null));
        await Assert.ThrowsExactlyAsync<AgentToolLoopBudgetExceededException>(() => new AgentToolLoop().RunAsync(new(runId,
            AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1, "secret-prompt", client, executor, Registry(), _ => Guid.CreateVersion7())));
        capture.Verify(runId, "tool_loop", "budget_rejected", 17);
        await client.Received(8).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    private sealed class HeadersHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task Invalid_rename_proposal_records_refused_node_without_write_async(bool tooLong)
    {
        using var capture = new AgentTelemetryCapture(); var runId = Guid.CreateVersion7();
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new AgentModelResult(tooLong ? new string('x', 257) : " ", JsonSerializer.SerializeToElement("secret-session"), 1, 1),
            new AgentModelResult("{\"decision\":\"approved\"}", JsonSerializer.SerializeToElement("secret-session"), 1, 1));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement("secret-output"), null));
        var result = await new AgentWorkflowRunner().RunAsync(new(runId, AgentWorkflowRegistry.ChatRenameWorkflowKey,
            AgentWorkflowRegistry.ChatRenameWorkflowVersion, AgentWorkflowState.Create(Guid.CreateVersion7()),
            Substitute.For<IChatClient>(), runner, executor,
            new AgentToolRegistry([new("ai.chat.sessions.list", 1, "read", "read", true, null),
                new("ai.chat.sessions.rename", 1, "write", "write", true, null)]), _ => Guid.CreateVersion7()));
        Assert.AreEqual(AgentWorkflowRunStatus.Failed, result.Status); capture.Verify(runId, "workflow", "refused", 8);
        Assert.AreEqual("refused", capture.Spans.Single(s => s.GetTagItem("ai.stage") as string == "node"
            && Equals(s.GetTagItem("ai.step.index"), 3)).GetTagItem("ai.outcome"));
        await executor.DidNotReceive().ExecuteAsync(Arg.Is<ToolInvocation>(i => i != null && i.ToolName == "ai.chat.sessions.rename"), Arg.Any<CancellationToken>());
    }
}

internal sealed class AgentTelemetryCapture : IDisposable
{
    internal const string Name = "Full.NET.Ai.Execution";
    internal readonly Activity Root = new Activity("test.root").SetIdFormat(ActivityIdFormat.W3C).Start();
    internal readonly List<Activity> Spans = [];
    internal readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> Measurements = [];
    private readonly MeterListener meter = new(); private readonly ActivityListener trace;
    internal AgentTelemetryCapture(bool sampleNone = false, string? throwMeasurement = null, string? throwTrace = null, bool includeProvider = false)
    {
        meter.InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == Name) listener.EnableMeasurementEvents(instrument); };
        meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        { if (Activity.Current?.TraceId == Root.TraceId) { Measurements.Add((instrument.Name, value, tags.ToArray()));
            if (instrument.Name.EndsWith(throwMeasurement ?? "never", StringComparison.Ordinal)) throw new IOException("secret-listener"); } });
        meter.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        { if (Activity.Current?.TraceId == Root.TraceId) { Measurements.Add((instrument.Name, value, tags.ToArray()));
            if (instrument.Name.EndsWith(throwMeasurement ?? "never", StringComparison.Ordinal)) throw new IOException("secret-listener"); } });
        meter.Start();
        trace = new ActivityListener { ShouldListenTo = source => source.Name == Name || includeProvider && source.Name == AiProviderHttpClients.TelemetryName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => throwTrace == "sample" ? throw new IOException("secret-listener")
                : sampleNone ? ActivitySamplingResult.None : ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { if (throwTrace == "started") throw new IOException("secret-listener"); },
            ActivityStopped = activity => { if (activity.TraceId == Root.TraceId) Spans.Add(activity);
                if (throwTrace == "stopped") throw new IOException("secret-listener"); } };
        ActivitySource.AddActivityListener(trace);
    }
    internal void Verify(Guid runId, string rootStage, string outcome, int total)
    {
        Assert.HasCount(total, Spans); Assert.HasCount(total * 2, Measurements);
        var root = Spans.Single(s => s.GetTagItem("ai.stage") as string == rootStage);
        Assert.AreEqual(outcome, root.GetTagItem("ai.outcome")); Assert.AreEqual(Root.SpanId, root.ParentSpanId);
        foreach (var span in Spans)
        {
            Assert.AreEqual(runId.ToString("D"), span.GetTagItem("ai.run.id"));
            Assert.AreEqual(ActivityKind.Internal, span.Kind); Assert.IsNull(span.StatusDescription); Assert.IsFalse(span.Events.Any());
            Assert.IsTrue(span.ParentSpanId == Root.SpanId || Spans.Any(p => p.SpanId == span.ParentSpanId));
            Assert.IsFalse(span.TagObjects.Any(t => t.Value?.ToString()?.Contains("secret", StringComparison.Ordinal) == true));
        }
        foreach (var m in Measurements)
        {
            Assert.AreEqual(2, m.Tags.Length);
            CollectionAssert.AreEquivalent(new[] { "ai.stage", "ai.outcome" }, m.Tags.Select(t => t.Key).ToArray());
            Assert.IsTrue(m.Name == "fullnet.ai.execution.attempts" ? m.Value == 1 : m.Value >= 0);
        }
        Assert.AreSame(Root, Activity.Current);
    }
    public void Dispose() { trace.Dispose(); meter.Dispose(); Root.Dispose(); }
}
