using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ProjectWorkflowTerminalEvents;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>终态业务回写与通知分别提交；单个接收点失败不得阻断其他幂等消费者。</summary>
[TestClass]
public sealed class WorkflowTerminalEventFanoutTests
{
    [TestMethod]
    [DataRow("completed")]
    [DataRow("rejected")]
    [DataRow("cancelled")]
    public async Task Failed_notification_does_not_block_business_projection(string kind)
    {
        var calls = new List<string>();
        var failure = new InvalidOperationException("test.notification_unavailable");
        var handler = Handler(kind,
            new Probe((_, _) => { calls.Add("notification"); return Task.FromException(failure); }),
            new Probe((_, _) => { calls.Add("business"); return Task.CompletedTask; }));
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => DeliverAsync(handler));
        CollectionAssert.AreEqual(new[] { "notification", "business" }, calls);
        Assert.AreSame(failure, thrown, "失败仍须向 Outbox 传播，不能错误确认整条消息已消费。");
    }

    [TestMethod]
    [DataRow("completed")]
    [DataRow("rejected")]
    [DataRow("cancelled")]
    public async Task All_failures_are_retained_after_remaining_consumers_run(string kind)
    {
        var calls = new List<string>();
        var first = new InvalidOperationException("test.first");
        var second = new ArgumentException("test.second");
        var handler = Handler(kind,
            new Probe((_, _) => { calls.Add("first"); return Task.FromException(first); }),
            new Probe((_, _) => { calls.Add("second"); return Task.FromException(second); }),
            new Probe((_, _) => { calls.Add("last"); return Task.CompletedTask; }));
        var thrown = await Assert.ThrowsAsync<AggregateException>(() => DeliverAsync(handler));
        CollectionAssert.AreEqual(new[] { "first", "second", "last" }, calls);
        CollectionAssert.AreEqual(new Exception[] { first, second }, thrown.InnerExceptions.ToArray());
    }

    [TestMethod]
    [DataRow("completed")]
    [DataRow("rejected")]
    [DataRow("cancelled")]
    public async Task Cancellation_stops_before_next_consumer(string kind)
    {
        using var cancellation = new CancellationTokenSource();
        var secondCalled = false;
        var handler = Handler(kind,
            new Probe((_, _) => { cancellation.Cancel(); return Task.FromCanceled(cancellation.Token); }),
            new Probe((_, _) => { secondCalled = true; return Task.CompletedTask; }));
        await Assert.ThrowsAsync<OperationCanceledException>(() => DeliverAsync(handler, cancellation.Token));
        Assert.IsFalse(secondCalled);
    }

    [TestMethod]
    [DataRow("completed")]
    [DataRow("rejected")]
    [DataRow("cancelled")]
    public async Task Already_cancelled_delivery_does_not_invoke_consumers(string kind)
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var called = false;
        var handler = Handler(kind, new Probe((_, _) => { called = true; return Task.CompletedTask; }));
        await Assert.ThrowsAsync<OperationCanceledException>(() => DeliverAsync(handler, cancellation.Token));
        Assert.IsFalse(called);
    }

    [TestMethod]
    [DataRow("completed")]
    [DataRow("rejected")]
    [DataRow("cancelled")]
    public async Task Cancellation_after_last_consumer_cannot_acknowledge_delivery(string kind)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Handler(kind, new Probe((_, _) => { cancellation.Cancel(); return Task.CompletedTask; }));
        await Assert.ThrowsAsync<OperationCanceledException>(() => DeliverAsync(handler, cancellation.Token));
    }

    [TestMethod]
    [DataRow("completed")]
    [DataRow("rejected")]
    [DataRow("cancelled")]
    public async Task Successful_consumers_run_sequentially_with_same_context(string kind)
    {
        var seen = new List<IntegrationEventContext>();
        var context = Context(kind);
        var handler = Handler(kind, new Probe((value, _) => { seen.Add(value); return Task.CompletedTask; }),
            new Probe((value, _) => { seen.Add(value); return Task.CompletedTask; }));
        await handler.HandleAsync(context, ReadOnlyMemory<byte>.Empty, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new[] { context, context }, seen);
    }

    private static Task DeliverAsync(IIntegrationEventHandler handler, CancellationToken ct = default) =>
        handler.HandleAsync(new IntegrationEventContext(Guid.CreateVersion7(), handler.EventType, 1,
            Guid.CreateVersion7(), null, DateTimeOffset.UtcNow), ReadOnlyMemory<byte>.Empty, ct);

    private static IntegrationEventContext Context(string kind) => new(Guid.CreateVersion7(),
        $"fullnet.workflow.instance.{kind}", 1, Guid.CreateVersion7(), null, DateTimeOffset.UtcNow);

    private static IIntegrationEventHandler Handler(string kind, params Probe[] sinks)
    {
        var serializer = Substitute.For<IIntegrationEventSerializer>();
        var instance = Guid.CreateVersion7(); var user = Guid.CreateVersion7(); var occurred = DateTimeOffset.UtcNow;
        serializer.Deserialize<WorkflowInstanceCompletedIntegrationEvent>(Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(new WorkflowInstanceCompletedIntegrationEvent(instance, user, "demo.enterprise_request", Guid.CreateVersion7().ToString("D"), occurred));
        serializer.Deserialize<WorkflowInstanceRejectedIntegrationEvent>(Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(new WorkflowInstanceRejectedIntegrationEvent(instance, user, "demo.enterprise_request", Guid.CreateVersion7().ToString("D"), occurred));
        serializer.Deserialize<WorkflowInstanceCancelledIntegrationEvent>(Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(new WorkflowInstanceCancelledIntegrationEvent(instance, user, "demo.enterprise_request", Guid.CreateVersion7().ToString("D"), occurred));
        return kind switch
        {
            "completed" => new WorkflowInstanceCompletedIntegrationEventHandler(serializer, sinks),
            "rejected" => new WorkflowInstanceRejectedIntegrationEventHandler(serializer, sinks),
            _ => new WorkflowInstanceCancelledIntegrationEventHandler(serializer, sinks),
        };
    }

    private sealed class Probe(Func<IntegrationEventContext, CancellationToken, Task> consume) :
        IWorkflowInstanceCompletedSink, IWorkflowInstanceRejectedSink, IWorkflowInstanceCancelledSink
    {
        public Task HandleAsync(IntegrationEventContext context, WorkflowInstanceCompletedIntegrationEvent value, CancellationToken ct) => consume(context, ct);
        public Task HandleAsync(IntegrationEventContext context, WorkflowInstanceRejectedIntegrationEvent value, CancellationToken ct) => consume(context, ct);
        public Task HandleAsync(IntegrationEventContext context, WorkflowInstanceCancelledIntegrationEvent value, CancellationToken ct) => consume(context, ct);
    }

    public TestContext TestContext { get; set; } = null!;
}
