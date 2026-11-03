using System.Diagnostics;
using Full.NET.Agents.Runtime;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>校对阶段观测保留同一取消和错误；不为定位超时创建业务身份或扩大标签。</summary>
[TestClass, DoNotParallelize]
public sealed class AiKnowledgeCorrectionStageTelemetryTests
{
    [TestMethod]
    [DataRow("knowledge_correction_batch")]
    [DataRow("knowledge_correction_restore")]
    [DataRow("knowledge_correction_embedding")]
    [DataRow("knowledge_correction_persist")]
    [DataRow("knowledge_correction_proof")]
    [DataRow("knowledge_correction_current")]
    [DataRow("knowledge_correction_authority")]
    public async Task Stage_preserves_original_cancellation_and_parent_without_identity(string stage)
    {
        using var capture = new Capture();
        using var stop = new CancellationTokenSource();
        var original = new OperationCanceledException(stop.Token);
        var received = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            AiKnowledgeTelemetry.RunAsync<bool>(stage, () => Task.FromException<bool>(original), stop.Token));
        Assert.AreSame(original, received);
        Assert.IsFalse(stop.IsCancellationRequested);
        var span = capture.Spans.Single();
        Assert.AreEqual(stage, span.GetTagItem("ai.stage"));
        Assert.AreEqual("cancelled", span.GetTagItem("ai.outcome"));
        Assert.AreEqual(capture.Parent.SpanId, span.ParentSpanId);
        CollectionAssert.AreEquivalent(new[] { "ai.stage", "ai.outcome" }, span.TagObjects.Select(x => x.Key).ToArray());
        Assert.AreSame(capture.Parent, Activity.Current);
    }

    [TestMethod]
    public async Task Nested_proofs_are_children_and_io_failure_is_not_reported_completed()
    {
        using var capture = new Capture();
        var original = new IOException("not-exported");
        var received = await Assert.ThrowsExactlyAsync<IOException>(() =>
            AiKnowledgeTelemetry.RunAsync("knowledge_correction_batch", () =>
                AiKnowledgeTelemetry.RunAsync<bool>("knowledge_correction_proof", () => Task.FromException<bool>(original), default), default));
        Assert.AreSame(original, received);
        var proof = capture.Spans.Single(x => Equals(x.GetTagItem("ai.stage"), "knowledge_correction_proof"));
        var batch = capture.Spans.Single(x => Equals(x.GetTagItem("ai.stage"), "knowledge_correction_batch"));
        Assert.AreEqual(batch.SpanId, proof.ParentSpanId);
        Assert.AreEqual("failed", proof.GetTagItem("ai.outcome"));
        Assert.AreEqual("failed", batch.GetTagItem("ai.outcome"));
        Assert.IsTrue(capture.Spans.All(x => x.StatusDescription is null && !x.Events.Any()));
    }

    private sealed class Capture : IDisposable
    {
        internal readonly List<Activity> Spans = [];
        internal readonly Activity Parent = new Activity("correction-test").Start();
        private readonly ActivityListener listener;
        internal Capture()
        {
            listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == AgentExecutionTelemetry.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = span => Spans.Add(span)
            };
            ActivitySource.AddActivityListener(listener);
        }
        public void Dispose() { listener.Dispose(); Parent.Dispose(); }
    }
}
