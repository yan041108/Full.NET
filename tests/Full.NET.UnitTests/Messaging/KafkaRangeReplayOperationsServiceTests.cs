using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ReplayKafkaRange;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaRangeReplayOperationsServiceTests
{
    [TestMethod]
    public async Task ReplayAsync_persists_requested_audit_before_broker_side_effect_and_success_after()
    {
        var sequence = new List<string>();
        var service = new KafkaRangeReplayOperationsService(
            new RecordingReplayService(sequence),
            EnabledPolicy(),
            new PassthroughTransaction(),
            new RecordingAuditWriter(sequence));

        var result = await service.ReplayAsync(
            new KafkaRangeReplayRequest(
                "messaging.orders.v1",
                null,
                null,
                10,
                12,
                [0],
                "fullnet.messaging.orders",
                100,
                "repair projection gap"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { "audit:requested", "replay", "audit:success" },
            sequence);
        Assert.AreEqual(3, result.Value?.ProcessedMessages);
    }

    [TestMethod]
    public async Task ReplayAsync_rejects_invalid_request_before_audit_or_broker_access()
    {
        var sequence = new List<string>();
        var service = new KafkaRangeReplayOperationsService(
            new RecordingReplayService(sequence),
            EnabledPolicy(),
            new PassthroughTransaction(),
            new RecordingAuditWriter(sequence));

        var result = await service.ReplayAsync(
            new KafkaRangeReplayRequest(
                "messaging.orders.v1",
                null,
                null,
                null,
                null,
                [0],
                "fullnet.messaging.orders",
                100,
                "invalid missing range"),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(MessagingErrorCodes.KafkaReplayRequestInvalid, result.Error?.Code);
        Assert.IsEmpty(sequence);
    }

    [TestMethod]
    public async Task ReplayAsync_rejects_disabled_operation_before_audit_or_broker_access()
    {
        var sequence = new List<string>();
        var service = new KafkaRangeReplayOperationsService(
            new RecordingReplayService(sequence),
            new KafkaReplayExecutionPolicy(false, 1_000, TimeSpan.FromSeconds(45)),
            new PassthroughTransaction(),
            new RecordingAuditWriter(sequence));

        var result = await service.ReplayAsync(ValidRequest(), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(MessagingErrorCodes.KafkaReplayUnavailable, result.Error?.Code);
        Assert.IsEmpty(sequence);
    }

    [TestMethod]
    public async Task ReplayAsync_rejects_range_above_synchronous_limit()
    {
        var sequence = new List<string>();
        var service = new KafkaRangeReplayOperationsService(
            new RecordingReplayService(sequence),
            new KafkaReplayExecutionPolicy(true, 10, TimeSpan.FromSeconds(45)),
            new PassthroughTransaction(),
            new RecordingAuditWriter(sequence));

        var result = await service.ReplayAsync(ValidRequest(), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            MessagingErrorCodes.KafkaReplaySynchronousLimitExceeded,
            result.Error?.Code);
        Assert.IsEmpty(sequence);
    }

    [TestMethod]
    public async Task ReplayAsync_persists_failure_terminal_audit_after_broker_side_effect_starts()
    {
        var sequence = new List<string>();
        var service = new KafkaRangeReplayOperationsService(
            new ThrowingReplayService(sequence),
            EnabledPolicy(),
            new PassthroughTransaction(),
            new RecordingAuditWriter(sequence));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ReplayAsync(ValidRequest(), CancellationToken.None));

        CollectionAssert.AreEqual(
            new[] { "audit:requested", "replay", "audit:failure" },
            sequence);
    }

    [TestMethod]
    public async Task ReplayAsync_persists_failure_terminal_audit_after_execution_timeout()
    {
        var sequence = new List<string>();
        var service = new KafkaRangeReplayOperationsService(
            new BlockingReplayService(sequence),
            new KafkaReplayExecutionPolicy(true, 1_000, TimeSpan.FromMilliseconds(25)),
            new PassthroughTransaction(),
            new RecordingAuditWriter(sequence));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ReplayAsync(ValidRequest(), CancellationToken.None));

        CollectionAssert.AreEqual(
            new[] { "audit:requested", "replay", "audit:failure" },
            sequence);
    }

    [TestMethod]
    public async Task ReplayAsync_does_not_write_execution_failure_when_success_audit_fails()
    {
        var sequence = new List<string>();
        var service = new KafkaRangeReplayOperationsService(
            new RecordingReplayService(sequence),
            EnabledPolicy(),
            new PassthroughTransaction(),
            new FailingSuccessAuditWriter(sequence));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ReplayAsync(ValidRequest(), CancellationToken.None));

        CollectionAssert.AreEqual(
            new[] { "audit:requested", "replay", "audit:success" },
            sequence);
    }

    private static KafkaReplayExecutionPolicy EnabledPolicy() =>
        new(true, 1_000, TimeSpan.FromSeconds(45));

    private static KafkaRangeReplayRequest ValidRequest() =>
        new(
            "messaging.orders.v1",
            null,
            null,
            10,
            12,
            [0],
            "fullnet.messaging.orders",
            100,
            "repair projection gap");

    private sealed class RecordingReplayService(List<string> sequence) : IKafkaReplayService
    {
        public Task<KafkaReplayResult> ReplayAsync(
            KafkaReplayRequest request,
            CancellationToken cancellationToken)
        {
            sequence.Add("replay");
            return Task.FromResult(new KafkaReplayResult(3, 3, 0, 0, false));
        }
    }

    private sealed class RecordingAuditWriter(List<string> sequence)
        : ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>
    {
        public Task WriteAsync(
            MessagingDomainAuditWrite auditWrite,
            CancellationToken cancellationToken)
        {
            sequence.Add($"audit:{auditWrite.Outcome}");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSuccessAuditWriter(List<string> sequence)
        : ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>
    {
        public Task WriteAsync(
            MessagingDomainAuditWrite auditWrite,
            CancellationToken cancellationToken)
        {
            sequence.Add($"audit:{auditWrite.Outcome}");
            return string.Equals(
                auditWrite.Outcome,
                MessagingDomainAuditOutcomes.Success,
                StringComparison.Ordinal)
                ? Task.FromException(new InvalidOperationException("success audit unavailable"))
                : Task.CompletedTask;
        }
    }

    private sealed class ThrowingReplayService(List<string> sequence) : IKafkaReplayService
    {
        public Task<KafkaReplayResult> ReplayAsync(
            KafkaReplayRequest request,
            CancellationToken cancellationToken)
        {
            sequence.Add("replay");
            throw new InvalidOperationException("simulated replay failure");
        }
    }

    private sealed class BlockingReplayService(List<string> sequence) : IKafkaReplayService
    {
        public async Task<KafkaReplayResult> ReplayAsync(
            KafkaReplayRequest request,
            CancellationToken cancellationToken)
        {
            sequence.Add("replay");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class PassthroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
