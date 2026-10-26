using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.Modules.Messaging.Features.ReplayKafkaRange;

/// <summary>一次调用的有界重放入口；与先创建再执行的页面流程共用同一持久操作协议。</summary>
internal sealed class KafkaRangeReplayOperationsService(KafkaReplayOperationService operations)
{
    public async Task<Result<KafkaRangeReplayResponse>> ReplayAsync(KafkaRangeReplayRequest request, CancellationToken token)
    {
        var created = await operations.CreateAsync(new(Guid.CreateVersion7(), request.TopicCode, request.FromTimestampUtc,
            request.ToTimestampUtc, request.FromOffset?.ToString(CultureInfo.InvariantCulture), request.ToOffset?.ToString(CultureInfo.InvariantCulture),
            request.Partitions, request.ReplayConsumerName, request.MaxMessages, request.Reason), token).ConfigureAwait(false);
        if (!created.IsSuccess) return Result<KafkaRangeReplayResponse>.Failure(created.Error!);
        var executed = await operations.ExecuteAsync(created.Value!.OperationId, new(request.Reason), token).ConfigureAwait(false);
        if (!executed.IsSuccess) return Result<KafkaRangeReplayResponse>.Failure(executed.Error!);
        var operation = executed.Value!;
        return Result<KafkaRangeReplayResponse>.Success(new(checked((int)(operation.ConfirmedMessages + operation.RejectedAttempts)),
            checked((int)operation.ProcessedMessages), checked((int)operation.AlreadyProcessedMessages), checked((int)operation.RejectedAttempts),
            operation.State == KafkaReplayOperationStates.Partial && operation.RejectedAttempts == 0, operation.OperationId, operation.State));
    }
}
