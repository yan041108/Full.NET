using System.Diagnostics;
using Full.NET.Agents.Runtime;
using Full.NET.AI.Abstractions.Budgets;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>TEXT真实批次观测复用已有授权、解析和收费夹具，不替换业务执行。</summary>
public sealed partial class AiKnowledgeIndexEmbeddingTests
{
    [TestMethod, DoNotParallelize]
    [DataRow(false, "success")]
    [DataRow(true, "success")]
    [DataRow(false, "denied")]
    [DataRow(true, "denied")]
    [DataRow(false, "duplicate")]
    [DataRow(true, "duplicate")]
    [DataRow(false, "cancel")]
    [DataRow(true, "cancel")]
    [DataRow(false, "provider")]
    [DataRow(true, "provider")]
    [DataRow(false, "budget")]
    [DataRow(true, "budget")]
    [DataRow(false, "settle")]
    [DataRow(true, "settle")]
    [DataRow(false, "listener")]
    [DataRow(true, "listener")]
    public async Task Index_telemetry_text_execution_retains_business_results(bool scoped, string fault)
    {
        using var f = new Fixture(scoped); Func<Task<object?>> execute = async () => await f.Execute();
        if (fault == "denied") f.DeniedPermission = "ai.knowledge_documents.index_enqueue";
        if (fault == "duplicate") f.Duplicate = true;
        if (fault == "cancel") f.Generator.BeforeReturn = token => throw new OperationCanceledException(token);
        var marker = new IOException("禁止进入观测的来源正文和凭据");
        if (fault == "provider") f.Generator.BeforeReturn = _ => throw marker;
        IndexTelemetryTestChecks.ConfigureBudget(f.Budget, fault, marker);
        using var capture = new AgentTelemetryCapture(throwMeasurement: fault == "listener" ? "attempts" : null);
        await IndexTelemetryTestChecks.ExecuteAsync(execute, capture, "text", fault, f.TaskId, f.OperationId, f.Budget, () => f.Generator.Calls, marker);
    }

    [TestMethod, DoNotParallelize] [DataRow(false)] [DataRow(true)]
    public async Task Index_telemetry_text_precancel_has_no_observation_or_dispatch(bool scoped)
    {
        using var f = new Fixture(scoped); using var capture = new AgentTelemetryCapture();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await f.Execute(token: new CancellationToken(true)));
        Assert.AreEqual(0, capture.Spans.Count); Assert.AreEqual(0, capture.Measurements.Count);
        Assert.AreEqual(0, f.Generator.Calls); Assert.IsFalse(f.Budget.ReceivedCalls().Any());
        Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod, DoNotParallelize]
    [DataRow("duration")] [DataRow("sample")] [DataRow("started")] [DataRow("stopped")]
    public async Task Index_telemetry_text_listener_fault_does_not_change_charge_or_dispatch(string fault)
    {
        using var f = new Fixture(false); Func<Task<object?>> execute = async () => await f.Execute();
        using var capture = new AgentTelemetryCapture(throwMeasurement: fault == "duration" ? "duration" : null,
            throwTrace: fault == "duration" ? null : fault);
        Assert.IsNotNull(await execute()); Assert.AreEqual(1, f.Generator.Calls);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "succeeded", CancellationToken.None);
        IndexTelemetryTestChecks.VerifyMeasurements(capture, "knowledge_index_text", "completed", includesChunk: true);
        IndexTelemetryTestChecks.VerifyPrivacy(capture); Assert.AreSame(capture.Root, Activity.Current);
    }
}

/// <summary>PDF真实批次观测复用已有授权、解析和收费夹具，不替换业务执行。</summary>
public sealed partial class AiKnowledgeIndexPdfPlanningServiceTests
{
    [TestMethod, DoNotParallelize]
    [DataRow(false, "success")]
    [DataRow(true, "success")]
    [DataRow(false, "denied")]
    [DataRow(true, "denied")]
    [DataRow(false, "duplicate")]
    [DataRow(true, "duplicate")]
    [DataRow(false, "cancel")]
    [DataRow(true, "cancel")]
    [DataRow(false, "provider")]
    [DataRow(true, "provider")]
    [DataRow(false, "budget")]
    [DataRow(true, "budget")]
    [DataRow(false, "settle")]
    [DataRow(true, "settle")]
    [DataRow(false, "listener")]
    [DataRow(true, "listener")]
    public async Task Index_telemetry_pdf_execution_retains_business_results(bool scoped, string fault)
    {
        using var f = new Fixture(scoped); var paid = f.Paid(); Func<Task<object?>> execute = async () => await paid.ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, default);
        if (fault == "denied") f.DeniedPermission = "ai.knowledge_documents.index_enqueue";
        if (fault == "duplicate") f.Duplicate = true;
        if (fault == "cancel") f.Generator.BeforeReturn = token => throw new OperationCanceledException(token);
        var marker = new IOException("禁止进入观测的来源正文和凭据");
        if (fault == "provider") f.Generator.BeforeReturn = _ => throw marker;
        IndexTelemetryTestChecks.ConfigureBudget(f.Budget, fault, marker);
        using var capture = new AgentTelemetryCapture(throwMeasurement: fault == "listener" ? "attempts" : null);
        await IndexTelemetryTestChecks.ExecuteAsync(execute, capture, "pdf", fault, f.TaskId, f.OperationId, f.Budget, () => f.Generator.Calls, marker);
    }

    [TestMethod, DoNotParallelize] [DataRow(false)] [DataRow(true)]
    public async Task Index_telemetry_pdf_precancel_has_no_observation_or_dispatch(bool scoped)
    {
        using var f = new Fixture(scoped); using var capture = new AgentTelemetryCapture();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, new CancellationToken(true)));
        Assert.AreEqual(0, capture.Spans.Count); Assert.AreEqual(0, capture.Measurements.Count);
        Assert.AreEqual(0, f.Generator.Calls); Assert.IsFalse(f.Budget.ReceivedCalls().Any());
        Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod, DoNotParallelize]
    [DataRow("duration")] [DataRow("sample")] [DataRow("started")] [DataRow("stopped")]
    public async Task Index_telemetry_pdf_listener_fault_does_not_change_charge_or_dispatch(string fault)
    {
        using var f = new Fixture(false); var paid = f.Paid(); Func<Task<object?>> execute = async () => await paid.ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, default);
        using var capture = new AgentTelemetryCapture(throwMeasurement: fault == "duration" ? "duration" : null,
            throwTrace: fault == "duration" ? null : fault);
        Assert.IsNotNull(await execute()); Assert.AreEqual(1, f.Generator.Calls);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "succeeded", CancellationToken.None);
        IndexTelemetryTestChecks.VerifyMeasurements(capture, "knowledge_index_pdf", "completed", includesChunk: true);
        IndexTelemetryTestChecks.VerifyPrivacy(capture); Assert.AreSame(capture.Root, Activity.Current);
    }
}

/// <summary>DOCX真实批次观测复用已有授权、解析和收费夹具，不替换业务执行。</summary>
public sealed partial class AiKnowledgeIndexDocxPlanningServiceTests
{
    [TestMethod, DoNotParallelize]
    [DataRow(false, "success")]
    [DataRow(true, "success")]
    [DataRow(false, "denied")]
    [DataRow(true, "denied")]
    [DataRow(false, "duplicate")]
    [DataRow(true, "duplicate")]
    [DataRow(false, "cancel")]
    [DataRow(true, "cancel")]
    [DataRow(false, "provider")]
    [DataRow(true, "provider")]
    [DataRow(false, "budget")]
    [DataRow(true, "budget")]
    [DataRow(false, "settle")]
    [DataRow(true, "settle")]
    [DataRow(false, "listener")]
    [DataRow(true, "listener")]
    public async Task Index_telemetry_docx_execution_retains_business_results(bool scoped, string fault)
    {
        using var f = new Fixture(scoped); var paid = f.Paid(); Func<Task<object?>> execute = async () => await paid.ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, default);
        if (fault == "denied") f.DeniedPermission = "ai.knowledge_documents.index_enqueue";
        if (fault == "duplicate") f.Duplicate = true;
        if (fault == "cancel") f.Generator.BeforeReturn = token => throw new OperationCanceledException(token);
        var marker = new IOException("禁止进入观测的来源正文和凭据");
        if (fault == "provider") f.Generator.BeforeReturn = _ => throw marker;
        IndexTelemetryTestChecks.ConfigureBudget(f.Budget, fault, marker);
        using var capture = new AgentTelemetryCapture(throwMeasurement: fault == "listener" ? "attempts" : null);
        await IndexTelemetryTestChecks.ExecuteAsync(execute, capture, "docx", fault, f.TaskId, f.OperationId, f.Budget, () => f.Generator.Calls, marker);
    }

    [TestMethod, DoNotParallelize] [DataRow(false)] [DataRow(true)]
    public async Task Index_telemetry_docx_precancel_has_no_observation_or_dispatch(bool scoped)
    {
        using var f = new Fixture(scoped); using var capture = new AgentTelemetryCapture();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, new CancellationToken(true)));
        Assert.AreEqual(0, capture.Spans.Count); Assert.AreEqual(0, capture.Measurements.Count);
        Assert.AreEqual(0, f.Generator.Calls); Assert.IsFalse(f.Budget.ReceivedCalls().Any());
        Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod, DoNotParallelize]
    [DataRow("duration")] [DataRow("sample")] [DataRow("started")] [DataRow("stopped")]
    public async Task Index_telemetry_docx_listener_fault_does_not_change_charge_or_dispatch(string fault)
    {
        using var f = new Fixture(false); var paid = f.Paid(); Func<Task<object?>> execute = async () => await paid.ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, default);
        using var capture = new AgentTelemetryCapture(throwMeasurement: fault == "duration" ? "duration" : null,
            throwTrace: fault == "duration" ? null : fault);
        Assert.IsNotNull(await execute()); Assert.AreEqual(1, f.Generator.Calls);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "succeeded", CancellationToken.None);
        IndexTelemetryTestChecks.VerifyMeasurements(capture, "knowledge_index_docx", "completed", includesChunk: true);
        IndexTelemetryTestChecks.VerifyPrivacy(capture); Assert.AreSame(capture.Root, Activity.Current);
    }
}
/// <summary>封闭索引阶段可由既有公共Start识别；无业务标识也不能把格式降为other。</summary>
[TestClass]
public sealed class AiKnowledgeIndexExecutionTelemetryTests
{
    [TestMethod, DoNotParallelize] [DataRow("text")] [DataRow("pdf")] [DataRow("docx")]
    public void Index_telemetry_fixed_stages_are_accepted_without_payload(string format)
    {
        using var capture = new AgentTelemetryCapture(); var stage = "knowledge_index_" + format;
        using (var observation = AgentExecutionTelemetry.Start(Guid.Empty, stage)) observation.Outcome = "completed";
        Assert.AreEqual(stage, capture.Spans.Single().GetTagItem("ai.stage"));
        Assert.IsNull(capture.Spans.Single().GetTagItem("ai.run.id"));
        IndexTelemetryTestChecks.VerifyMeasurements(capture, stage, "completed"); IndexTelemetryTestChecks.VerifyPrivacy(capture);
    }
}

/// <summary>只检查实际阶段与有限标签；不把Trace或方法返回当成账本/索引ready证明。</summary>
internal static class IndexTelemetryTestChecks
{
    internal static void ConfigureBudget(IAiOperationBudgetStore budget, string fault, IOException marker)
    {
        if (fault == "budget") budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<AiOperationReservation>(new AiBudgetException("ai.budget.limit_exceeded")));
        if (fault == "settle") budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw marker);
    }
    internal static async Task ExecuteAsync(Func<Task<object?>> execute, AgentTelemetryCapture capture, string kind, string fault,
        Guid task, Guid operation, IAiOperationBudgetStore budget, Func<int> calls, IOException marker)
    {
        object? result = null;
        if (fault is "provider" or "settle") Assert.AreSame(marker, await Assert.ThrowsExactlyAsync<IOException>(execute));
        else if (fault == "budget") await Assert.ThrowsExactlyAsync<AiBudgetException>(execute);
        else if (fault == "cancel") await Assert.ThrowsAsync<OperationCanceledException>(execute);
        else result = await execute();
        if (fault is "success" or "listener") Assert.IsNotNull(result); else Assert.IsNull(result);
        Assert.AreEqual(fault is "denied" or "duplicate" or "budget" ? 0 : 1, calls());
        var outcome = fault is "denied" or "duplicate" ? "not_processed" : fault == "budget" ? "budget_rejected"
            : fault is "provider" or "settle" ? "failed" : fault == "cancel" ? "cancelled" : "completed";
        var span = capture.Spans.Single(s => Equals(s.GetTagItem("ai.stage"), "knowledge_index_" + kind));
        Assert.HasCount(fault == "denied" ? 1 : 2, capture.Spans);
        if (fault != "denied")
        {
            var chunk = capture.Spans.Single(s => Equals(s.GetTagItem("ai.stage"), "knowledge_chunk_" + kind));
            Assert.AreEqual(span.SpanId, chunk.ParentSpanId); Assert.AreEqual(span.TraceId, chunk.TraceId);
            Assert.AreEqual("completed", chunk.GetTagItem("ai.outcome"));
            CollectionAssert.AreEquivalent(new[] { "ai.stage", "ai.outcome" }, chunk.TagObjects.Select(t => t.Key).ToArray());
        }
        Assert.AreEqual(outcome, span.GetTagItem("ai.outcome")); Assert.AreEqual(task.ToString("D"), span.GetTagItem("ai.knowledge.task.id"));
        Assert.AreEqual(fault is "denied" or "budget" ? null : operation.ToString("D"), span.GetTagItem("ai.operation.id"));
        Assert.AreEqual(capture.Root.SpanId, span.ParentSpanId);
        if (fault is "denied" or "duplicate" or "budget")
            await budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        else await budget.Received(1).SettleAsync(operation, fault is "provider" or "cancel" ? new(null, null) : new(7, 0),
            fault == "provider" ? "failed" : fault == "cancel" ? "cancelled" : "succeeded", CancellationToken.None);
        VerifyMeasurements(capture, "knowledge_index_" + kind, outcome, includesChunk: fault != "denied"); VerifyPrivacy(capture);
        Assert.AreSame(capture.Root, Activity.Current);
    }
    internal static void VerifyMeasurements(AgentTelemetryCapture capture, string stage, string outcome, bool includesChunk = false)
    {
        var chunkStage = stage.Replace("knowledge_index_", "knowledge_chunk_", StringComparison.Ordinal);
        Assert.HasCount(includesChunk ? 4 : 2, capture.Measurements);
        foreach (var item in capture.Measurements)
        {
            CollectionAssert.AreEquivalent(new[] { "ai.stage", "ai.outcome" }, item.Tags.Select(t => t.Key).ToArray());
            var observedStage = item.Tags.Single(t => t.Key == "ai.stage").Value;
            Assert.IsTrue(Equals(observedStage, stage) || includesChunk && Equals(observedStage, chunkStage));
            Assert.AreEqual(Equals(observedStage, stage) ? outcome : "completed", item.Tags.Single(t => t.Key == "ai.outcome").Value);
            Assert.IsTrue(item.Name == "fullnet.ai.execution.attempts" ? item.Value == 1 : item.Value >= 0);
        }
        foreach (var expectedStage in includesChunk ? new[] { stage, chunkStage } : new[] { stage })
        {
            var group = capture.Measurements.Where(m => Equals(m.Tags.Single(t => t.Key == "ai.stage").Value, expectedStage)).ToArray();
            Assert.HasCount(2, group);
            CollectionAssert.AreEquivalent(new[] { "fullnet.ai.execution.attempts", "fullnet.ai.execution.duration" }, group.Select(m => m.Name).ToArray());
        }
    }
    internal static void VerifyPrivacy(AgentTelemetryCapture capture)
    {
        foreach (var span in capture.Spans)
        {
            Assert.IsNull(span.GetTagItem("ai.run.id")); Assert.IsFalse(span.Events.Any()); Assert.IsNull(span.StatusDescription);
            Assert.IsTrue(span.TagObjects.All(t => t.Key is "ai.stage" or "ai.outcome" or "ai.operation.id" or "ai.knowledge.task.id"));
        }
    }
}
