using System.Text.Json;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.Modules.Messaging.Features.ReplayKafkaRange;

internal sealed class KafkaRangeReplayOperationsService(
    IKafkaReplayService replayService,
    KafkaReplayExecutionPolicy executionPolicy,
    ICommandTransaction transaction,
    ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> domainAuditWriter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
                new
                {
                    request.TopicCode,
                    request.Partitions,
                    request.FromTimestampUtc,
                    request.ToTimestampUtc,
                    request.FromOffset,
                    request.ToOffset,
                    request.ReplayConsumerName,
                    request.MaxMessages,
                    reason = replayRequest.Reason,
                },
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
                    new
                    {
                        request.TopicCode,
                        request.ReplayConsumerName,
                        reasonCode = "cancelled_or_timed_out",
                    })
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await WriteTerminalAuditAsync(
                    operationId,
                    MessagingDomainAuditOutcomes.Failure,
                    new
                    {
                        request.TopicCode,
                        request.ReplayConsumerName,
                        reasonCode = "execution_failed",
                        exceptionType = exception.GetType().Name,
                    })
                .ConfigureAwait(false);
            throw;
        }

        // 成功审计失败表示结果不确定，不能被下方 execution_failed 捕获后误写失败终态。
        await WriteTerminalAuditAsync(
                operationId,
                MessagingDomainAuditOutcomes.Success,
                new
                {
                    request.TopicCode,
                    request.ReplayConsumerName,
                    replay.ScannedMessages,
                    replay.ProcessedMessages,
                    replay.AlreadyProcessedMessages,
                    replay.RejectedMessages,
                    replay.LimitReached,
                })
            .ConfigureAwait(false);

        return Result<KafkaRangeReplayResponse>.Success(new KafkaRangeReplayResponse(
            replay.ScannedMessages,
            replay.ProcessedMessages,
            replay.AlreadyProcessedMessages,
            replay.RejectedMessages,
            replay.LimitReached));
    }

    private async Task WriteTerminalAuditAsync(
        Guid operationId,
        string outcome,
        object summary)
    {
        using var auditCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WriteAuditAsync(
                operationId,
                outcome,
                summary,
                auditCancellation.Token)
            .ConfigureAwait(false);
    }

    private Task<bool> WriteAuditAsync(
        Guid operationId,
        string outcome,
        object summary,
        CancellationToken cancellationToken) =>
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
                        DiffSummaryJson: JsonSerializer.Serialize(summary, JsonOptions)),
                    token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
}
