using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Full.NET.Agents.Runtime;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>只在真实文本批次入口核对观测；原双库账本断言仍独立证明收费与隔离。</summary>
internal static class AiKnowledgeIndexTelemetryAssertions
{
    internal static async Task<AiKnowledgeIndexEmbeddingBatch?> VerifyAsync(Guid taskId,
        Func<Task<AiKnowledgeIndexEmbeddingBatch?>> execute, Func<Task<Guid?>> readOperation, Func<int> readSourceOpens)
    {
        var parent = Activity.Current;
        using var root = new Activity("index-execution-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var spans = new ConcurrentQueue<Activity>();
        var measures = new ConcurrentQueue<(string Name, double Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AgentExecutionTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = span => { if (span.TraceId == root.TraceId) spans.Enqueue(span); }
        };
        ActivitySource.AddActivityListener(listener);
        using var meter = new MeterListener
        {
            InstrumentPublished = (instrument, target) =>
            { if (instrument.Meter.Name == AgentExecutionTelemetry.Name) target.EnableMeasurementEvents(instrument); }
        };
        meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        meter.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        meter.Start();
        var expected = "failed"; Guid? returnedOperation = null; var opensBefore = readSourceOpens();
        try
        {
            var result = await execute(); returnedOperation = result?.OperationId;
            expected = result is null ? "not_processed" : "completed"; return result;
        }
        catch (AiBudgetException) { expected = "budget_rejected"; throw; }
        catch (UnauthorizedAccessException) { expected = "authorization_required"; throw; }
        catch (OperationCanceledException) { expected = "cancelled"; throw; }
        finally
        {
            // 先结束监听再读既有账本；观测不能成为操作或预算权威。
            meter.Dispose(); listener.Dispose();
            Assert.AreSame(root, Activity.Current);
            // 受控有效来源每次实际打开后只产生一个分块子阶段；提前拒绝不能伪造子阶段。
            var opened = readSourceOpens() - opensBefore;
            Assert.IsTrue(opened is 0 or 1); Assert.AreEqual(1 + opened, spans.Count);
            var span = spans.Single(s => Equals(s.GetTagItem("ai.stage"), "knowledge_index_text"));
            Assert.AreEqual("knowledge_index_text", span.GetTagItem("ai.stage"));
            Assert.AreEqual(root.SpanId, span.ParentSpanId);
            Assert.AreEqual(expected, span.GetTagItem("ai.outcome"));
            Assert.AreEqual(taskId.ToString("D"), span.GetTagItem("ai.knowledge.task.id"));
            Assert.IsNull(span.GetTagItem("ai.run.id"));
            // 早于准入的撤权可留下旧账本；只核对实际已解析的操作，不虚构关联。
            if (span.GetTagItem("ai.operation.id") is { } observedOperation)
                Assert.AreEqual((await readOperation())?.ToString("D"), observedOperation);
            if (returnedOperation is { } completedOperation)
                Assert.AreEqual(completedOperation.ToString("D"), span.GetTagItem("ai.operation.id"));
            Assert.IsFalse(span.Events.Any()); Assert.IsNull(span.StatusDescription);
            Assert.IsTrue(span.TagObjects.All(tag => tag.Key is "ai.stage" or "ai.outcome" or "ai.knowledge.task.id" or "ai.operation.id"));
            if (opened == 1)
            {
                var chunk = spans.Single(s => Equals(s.GetTagItem("ai.stage"), "knowledge_chunk_text"));
                Assert.AreEqual(span.SpanId, chunk.ParentSpanId); Assert.AreEqual(span.TraceId, chunk.TraceId);
                Assert.AreEqual("completed", chunk.GetTagItem("ai.outcome"));
                CollectionAssert.AreEquivalent(new[] { "ai.stage", "ai.outcome" }, chunk.TagObjects.Select(t => t.Key).ToArray());
                Assert.IsFalse(chunk.Events.Any()); Assert.IsNull(chunk.StatusDescription);
            }
            Assert.AreEqual((1 + opened) * 2, measures.Count);
            foreach (var measure in measures)
            {
                CollectionAssert.AreEquivalent(new[] { "ai.stage", "ai.outcome" }, measure.Tags.Select(t => t.Key).ToArray());
                var stage = measure.Tags.Single(tag => tag.Key == "ai.stage").Value;
                Assert.IsTrue(Equals(stage, "knowledge_index_text") || opened == 1 && Equals(stage, "knowledge_chunk_text"));
                Assert.AreEqual(Equals(stage, "knowledge_index_text") ? expected : "completed", measure.Tags.Single(tag => tag.Key == "ai.outcome").Value);
            }
            foreach (var stage in opened == 1 ? new[] { "knowledge_index_text", "knowledge_chunk_text" } : new[] { "knowledge_index_text" })
            {
                var group = measures.Where(m => Equals(m.Tags.Single(t => t.Key == "ai.stage").Value, stage)).ToArray();
                Assert.AreEqual(2, group.Length);
                Assert.AreEqual(1d, group.Single(m => m.Name == "fullnet.ai.execution.attempts").Value);
                Assert.IsTrue(group.Single(m => m.Name == "fullnet.ai.execution.duration").Value >= 0);
            }
            root.Stop(); Assert.AreSame(parent, Activity.Current);
        }
        void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            if (Activity.Current?.TraceId == root.TraceId) measures.Enqueue((instrument.Name, value, tags.ToArray()));
        }
    }
}
