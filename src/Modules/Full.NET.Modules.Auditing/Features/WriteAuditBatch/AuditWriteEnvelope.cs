using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Persistence;

namespace Full.NET.Modules.Auditing.Features.WriteAuditBatch;

/// <summary>B1 微批条目类型；Access 不属于 B1。</summary>
internal enum AuditMicroBatchKind
{
    Operation = 1,
    Exception = 2,
    Outbound = 3,
}

/// <summary>单次 B1 写入尝试结果。</summary>
internal readonly record struct AuditWriteResult(bool Succeeded, bool Poisoned = false);

/// <summary>
/// 跨请求微批信封：携带待写载荷与请求侧等待的完成源。
/// </summary>
internal sealed class AuditWriteEnvelope
{
    private AuditWriteEnvelope(
        AuditMicroBatchKind kind,
        OperationLogWriteModel? operation,
        ExceptionLogWriteModel? exception,
        OutboundCallLogRecord? outbound,
        int estimatedBytes)
    {
        Kind = kind;
        Operation = operation;
        Exception = exception;
        Outbound = outbound;
        EstimatedBytes = estimatedBytes;
        Completion = new TaskCompletionSource<AuditWriteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public AuditMicroBatchKind Kind { get; }

    public OperationLogWriteModel? Operation { get; }

    public ExceptionLogWriteModel? Exception { get; }

    public OutboundCallLogRecord? Outbound { get; }

    public int EstimatedBytes { get; }

    public TaskCompletionSource<AuditWriteResult> Completion { get; }

    public static AuditWriteEnvelope ForOperation(OperationLogWriteModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new AuditWriteEnvelope(
            AuditMicroBatchKind.Operation,
            model,
            exception: null,
            outbound: null,
            Estimate(model.ActionKey, model.RequestPath, model.TraceId, model.PermissionCode));
    }

    public static AuditWriteEnvelope ForException(ExceptionLogWriteModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new AuditWriteEnvelope(
            AuditMicroBatchKind.Exception,
            operation: null,
            model,
            outbound: null,
            Estimate(model.ExceptionType, model.Message, model.StackTrace, model.RequestPath));
    }

    public static AuditWriteEnvelope ForOutbound(OutboundCallLogRecord model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new AuditWriteEnvelope(
            AuditMicroBatchKind.Outbound,
            operation: null,
            exception: null,
            model,
            Estimate(
                model.ProviderKey,
                model.OperationKey,
                model.DestinationHostCategory,
                model.SafeErrorCode,
                model.TraceId));
    }

    private static int Estimate(params string?[] parts)
    {
        // 粗估载荷字节，用于 MaxBatchBytes 背压；不追求精确 UTF-8 计数。
        var total = 64;
        foreach (var part in parts)
        {
            total += part?.Length * 2 ?? 0;
        }

        return total;
    }
}
