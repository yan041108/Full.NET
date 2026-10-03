using System.Text;
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
    private int _queueBudgetReleased;

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

    public void ReleaseQueueBudgetOnce(AuditQueueByteBudget budget)
    {
        if (Interlocked.Exchange(ref _queueBudgetReleased, 1) == 0)
        {
            budget.Release(EstimatedBytes);
        }
    }

    public static AuditWriteEnvelope ForOperation(OperationLogWriteModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Details is { } details
            && (string.IsNullOrWhiteSpace(details.ContextJson)
                || details.ContextJson.Length > 8192
                || Encoding.UTF8.GetByteCount(details.ContextJson) > 8192
                || details.ExpiresAtUtc.Offset != TimeSpan.Zero))
        {
            // 详情无效时只降级为摘要，不能让旁路数据阻断 B1 原有记录。
            model = model with { Details = null };
        }

        return new AuditWriteEnvelope(
            AuditMicroBatchKind.Operation,
            model,
            exception: null,
            outbound: null,
            Estimate(
                model.ActionKey,
                model.HttpMethod,
                model.RequestPath,
                model.TraceId,
                model.ClientIpFingerprint,
                model.PermissionCode,
                model.RequiredPermissions.IsDefaultOrEmpty
                    ? null
                    : string.Join('|', model.RequiredPermissions),
                model.Details?.ContextJson));
    }

    public static AuditWriteEnvelope ForException(ExceptionLogWriteModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new AuditWriteEnvelope(
            AuditMicroBatchKind.Exception,
            operation: null,
            model,
            outbound: null,
            Estimate(
                model.ExceptionType,
                model.Message,
                model.StackTrace,
                model.HttpMethod,
                model.RequestPath,
                model.TraceId,
                model.ClientIpFingerprint));
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
        // 字符数不能代表 UTF-8 字节；固定费用覆盖行对象和数值字段的基础开销。
        long total = 128;
        foreach (var part in parts)
        {
            if (part is null)
            {
                continue;
            }

            if (part.Length >= (int.MaxValue - total) / 2)
            {
                return int.MaxValue;
            }

            total += Math.Max(part.Length * 2, Encoding.UTF8.GetByteCount(part));
            if (total >= int.MaxValue)
            {
                return int.MaxValue;
            }
        }

        return (int)total;
    }
}
