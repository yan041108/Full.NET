using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Full.NET.Agents.Runtime;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实服务入口使用原业务标识；观测断言不替代双库账本或授权断言。</summary>
internal sealed class AiOperationStageTelemetryAssertions : IDisposable
{
    private readonly Activity root = new Activity("operation-stage-test").SetIdFormat(ActivityIdFormat.W3C).Start();
    private readonly ConcurrentQueue<Activity> spans = new();
    private readonly ConcurrentQueue<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> measures = new();
    private readonly ActivityListener listener;
    private readonly MeterListener meter = new();
    internal AiOperationStageTelemetryAssertions()
    {
        listener = new() { ShouldListenTo = source => source.Name == AgentExecutionTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = span => { if (span.TraceId == root.TraceId) spans.Enqueue(span); } };
        ActivitySource.AddActivityListener(listener);
        meter.InstrumentPublished = (instrument, target) => { if (instrument.Meter.Name == AgentExecutionTelemetry.Name) target.EnableMeasurementEvents(instrument); };
        meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        meter.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        meter.Start();
    }
    internal void Verify(string stage, string outcome, Guid? operation)
    {
        meter.Dispose(); listener.Dispose(); Assert.AreSame(root, Activity.Current);
        Assert.AreEqual(1, spans.Count); Assert.AreEqual(2, measures.Count); var span = spans.Single();
        Assert.AreEqual(stage, span.GetTagItem("ai.stage")); Assert.AreEqual(outcome, span.GetTagItem("ai.outcome"));
        Assert.AreEqual(operation?.ToString("D"), span.GetTagItem("ai.operation.id")); Assert.AreEqual(root.SpanId, span.ParentSpanId);
        CollectionAssert.AreEquivalent(operation.HasValue ? new[] { "ai.stage", "ai.outcome", "ai.operation.id" } : new[] { "ai.stage", "ai.outcome" }, span.TagObjects.Select(t => t.Key).ToArray());
        Assert.IsFalse(span.Events.Any()); Assert.IsNull(span.StatusDescription);
        foreach (var measure in measures)
        {
            CollectionAssert.AreEquivalent(new[] { "ai.stage", "ai.outcome" }, measure.Tags.Select(t => t.Key).ToArray());
            Assert.AreEqual(stage, measure.Tags.Single(t => t.Key == "ai.stage").Value);
            Assert.AreEqual(outcome, measure.Tags.Single(t => t.Key == "ai.outcome").Value);
        }
        Assert.AreEqual(1d, measures.Single(m => m.Name == "fullnet.ai.execution.attempts").Value);
        Assert.IsTrue(measures.Single(m => m.Name == "fullnet.ai.execution.duration").Value >= 0);
    }
    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    { if (Activity.Current?.TraceId == root.TraceId) measures.Enqueue((instrument.Name, value, tags.ToArray())); }
    public void Dispose() { meter.Dispose(); listener.Dispose(); root.Dispose(); }
}
