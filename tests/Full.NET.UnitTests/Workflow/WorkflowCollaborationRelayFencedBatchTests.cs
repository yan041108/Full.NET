using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.UnitTests.Workflow;

/// <summary>单流 producer fence 不能阻断同批其他耐久协作候选。</summary>
[TestClass]
public sealed class WorkflowCollaborationRelayFencedBatchTests
{
    [TestMethod]
    public async Task Fenced_candidate_does_not_starve_later_candidate()
    {
        var first = Candidate(); var second = Candidate(); var seen = new List<Guid>(); var deferred = new List<string>();
        var completed = await WorkflowCollaborationRelay.ProcessBatchAsync([first, second], (row, _) =>
        {
            seen.Add(row.Id);
            return row.Id == first.Id ? Task.FromException<bool>(new EventDeliveryProducerFencedException("first", 1)) : Task.FromResult(true);
        }, CancellationToken.None, exception => deferred.Add(exception.EventType));
        Assert.AreEqual(1, completed);
        CollectionAssert.AreEqual(new[] { first.Id, second.Id }, seen);
        CollectionAssert.AreEqual(new[] { "first" }, deferred);
    }

    [TestMethod]
    public async Task Other_failures_are_not_suppressed()
    {
        var seen = 0;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => WorkflowCollaborationRelay.ProcessBatchAsync(
            [Candidate(), Candidate()], (_, _) => { seen++; throw new InvalidOperationException("unexpected"); }, CancellationToken.None));
        Assert.AreEqual(1, seen);
    }

    [TestMethod]
    public async Task Cancellation_is_not_suppressed_when_fence_is_observed()
    {
        using var cancellation = new CancellationTokenSource(); var seen = 0;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => WorkflowCollaborationRelay.ProcessBatchAsync(
            [Candidate(), Candidate()], (_, _) =>
            {
                seen++; cancellation.Cancel();
                return Task.FromException<bool>(new EventDeliveryProducerFencedException("first", 1));
            }, cancellation.Token));
        Assert.AreEqual(1, seen);
    }

    private static WorkflowCollaborationRelayCandidate Candidate() => new(Guid.CreateVersion7(), "host", "tenant", DateTimeOffset.UtcNow);
}
