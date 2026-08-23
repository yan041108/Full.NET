using System.Text.Json;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Serialization;

namespace Full.NET.Modules.Messaging.Features.ReplayKafkaRange;

internal sealed class KafkaRangeReplayOperationsService(
    IKafkaReplayService replayService,
    KafkaReplayExecutionPolicy executionPolicy,
    ICommandTransaction transaction,
    ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> domainAuditWriter)
{
    public async Task<Result<KafkaRangeReplayResponse>> ReplayAsync(
        KafkaRangeReplayRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!executionPolicy.Enabled)
        {
            return Result<KafkaRangeReplayResponse>.Failure(new Error(
                MessagingErrorCodes.KafkaReplayUnavailable,
                "Kafka range replay is disabled for this API deployment.",
                ErrorType.BusinessRule));
        }

        KafkaReplayRequest replayRequest;
        try
        {
            replayRequest = new KafkaReplayRequest(
                request.TopicCode,
                request.FromTimestampUtc,
                request.ToTimestampUtc,
                request.FromOffset,
                request.ToOffset,
                request.Partitions,
                request.ReplayConsumerName,
                request.MaxMessages,
                request.Reason);
        }
        catch (ArgumentException exception)
        {
            return Result<KafkaRangeReplayResponse>.Failure(new Error(
                MessagingErrorCodes.KafkaReplayRequestInvalid,
                exception.Message,
                ErrorType.Validation));
        }

        if (replayRequest.MaxMessages > executionPolicy.MaximumSynchronousMessages)
        {
            return Result<KafkaRangeReplayResponse>.Failure(new Error(
                MessagingErrorCodes.KafkaReplaySynchronousLimitExceeded,
                $"Synchronous Kafka replay is limited to {executionPolicy.MaximumSynchronousMessages} messages.",
                ErrorType.Validation));
        }

        var operationId = Guid.CreateVersion7();
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        executionCancellation.CancelAfter(executionPolicy.ExecutionTimeout);
        await WriteAuditAsync(
                operationId,
                MessagingDomainAuditOutcomes.Requested,
                new KafkaRangeReplayRequestedAuditDiff(
                    request.TopicCode,
                    request.Partitions,
                    request.FromTimestampUtc,
                    request.ToTimestampUtc,
                    request.FromOffset,
                    request.ToOffset,
                    request.ReplayConsumerName,
                    request.MaxMessages,
                    replayRequest.Reason),
                executionCancellation.Token)
            .ConfigureAwait(false);
        KafkaReplayResult replay;
        try
        {
            replay = await replayService
                .ReplayAsync(replayRequest, executionCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
        {
            await WriteTerminalAuditAsync(
                    operationId,
                    MessagingDomainAuditOutcomes.Failure,
                    new KafkaRangeReplayCancelledAuditDiff(
                        request.TopicCode,
                        request.ReplayConsumerName,
                        ReasonCode: "cancelled_or_timed_out"))
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await WriteTerminalAuditAsync(
                    operationId,
                    MessagingDomainAuditOutcomes.Failure,
                    new KafkaRangeReplayFailedAuditDiff(
                        request.TopicCode,
                        request.ReplayConsumerName,
                        ReasonCode: "execution_failed",
                        exception.GetType().Name))
                .ConfigureAwait(false);
            throw;
        }

        // 成功审计失败表示结果不确定，不能被下方 execution_failed 捕获后误写失败终态。
        await WriteTerminalAuditAsync(
                operationId,
                MessagingDomainAuditOutcomes.Success,
                new KafkaRangeReplaySuccessAuditDiff(
                    request.TopicCode,
                    request.ReplayConsumerName,
                    replay.ScannedMessages,
                    replay.ProcessedMessages,
                    replay.AlreadyProcessedMessages,
                    replay.RejectedMessages,
                    replay.LimitReached))
            .ConfigureAwait(false);

        return Result<KafkaRangeReplayResponse>.Success(new KafkaRangeReplayResponse(
            replay.ScannedMessages,
            replay.ProcessedMessages,
            replay.AlreadyProcessedMessages,
            replay.RejectedMessages,
            replay.LimitReached));
    }

    private async Task WriteTerminalAuditAsync<T>(
        Guid operationId,
        string outcome,
        T summary)
        where T : notnull
    {
        using var auditCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WriteAuditAsync(
                operationId,
                outcome,
                summary,
                auditCancellation.Token)
            .ConfigureAwait(false);
    }

    private Task<bool> WriteAuditAsync<T>(
        Guid operationId,
        string outcome,
        T summary,
        CancellationToken cancellationToken)
        where T : notnull =>
        transaction.ExecuteAsync(
            async token =>
            {
                await domainAuditWriter.WriteAsync(
                    new MessagingDomainAuditWrite(
                        MessagingDomainAuditActionKeys.KafkaRangeReplay,
                        operationId,
                        TenantId: null,
                        outcome,
                        ActorUserId: null,
                        ActorDisplayName: null,
                        DiffSummaryJson: SerializeKafkaRangeReplaySummary(summary)),
                    token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    private static string SerializeKafkaRangeReplaySummary<T>(T summary)
        where T : notnull =>
        summary switch
        {
            KafkaRangeReplayRequestedAuditDiff requested =>
                JsonSerializer.Serialize(
                    requested,
                    MessagingJsonSerializerContext.Default.KafkaRangeReplayRequestedAuditDiff),
            KafkaRangeReplayCancelledAuditDiff cancelled =>
                JsonSerializer.Serialize(
                    cancelled,
                    MessagingJsonSerializerContext.Default.KafkaRangeReplayCancelledAuditDiff),
            KafkaRangeReplayFailedAuditDiff failed =>
                JsonSerializer.Serialize(
                    failed,
                    MessagingJsonSerializerContext.Default.KafkaRangeReplayFailedAuditDiff),
            KafkaRangeReplaySuccessAuditDiff success =>
                JsonSerializer.Serialize(
                    success,
                    MessagingJsonSerializerContext.Default.KafkaRangeReplaySuccessAuditDiff),
            _ => throw new InvalidOperationException(
                "Unsupported Kafka range replay audit summary type."),
        };
}
