using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;

namespace Full.NET.Modules.Auditing.Features.WriteAuditBatch;

[Flags]
internal enum AuditWriteKinds
{
    None = 0,
    Operation = 1,
    Exception = 2,
}

/// <summary>
/// 决定请求内应捕获的 Audit 类型；生产默认全部捕获，Benchmark 可替换该策略做单变量归因。
/// </summary>
internal interface IAuditWriteCapturePolicy
{
    bool ShouldCapture(AuditWriteKinds kind);
}

internal sealed class CaptureAllAuditWritesPolicy : IAuditWriteCapturePolicy
{
    public bool ShouldCapture(AuditWriteKinds kind) => true;
}

/// <summary>
/// 请求作用域内的 B1 审计日志写入缓冲。保存 Operation/Exception 两类写入槽位（每类每请求至多一条，重复 Capture 直接抛异常），
/// 请求退出时产出不可变 AuditWriteBatch 快照走 B1 有界 Channel 批处理落库；
/// Access 日志已迁入 Hosting 层 B2 Fire-and-Forget 流，不经过本缓冲。
/// 三可靠性分类：B0 同事务域内写、B1 异步有界队列批量写、B2 尽力投递可丢失。
/// </summary>
internal sealed class AuditWriteBuffer
{
    private readonly IAuditWriteCapturePolicy _capturePolicy;
    private OperationLogWriteModel? _operation;
    private ExceptionLogWriteModel? _exception;

    public AuditWriteBuffer()
        : this(new CaptureAllAuditWritesPolicy())
    {
    }

    public AuditWriteBuffer(IAuditWriteCapturePolicy capturePolicy)
    {
        _capturePolicy = capturePolicy;
    }

    public void Capture(OperationLogWriteModel model)
    {
        if (!_capturePolicy.ShouldCapture(AuditWriteKinds.Operation))
        {
            return;
        }

        _operation = CaptureOnce(_operation, model, AuditWriteKinds.Operation);
    }

    public void Capture(ExceptionLogWriteModel model)
    {
        if (!_capturePolicy.ShouldCapture(AuditWriteKinds.Exception))
        {
            return;
        }

        _exception = CaptureOnce(_exception, model, AuditWriteKinds.Exception);
    }

    public AuditWriteBatch Snapshot() => new(_operation, _exception);

    private static T CaptureOnce<T>(T? existing, T model, AuditWriteKinds kind)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(model);
        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"A request cannot capture more than one {kind} audit record.");
        }

        return model;
    }
}

/// <summary>
/// 请求退出时的不可变快照：Operation/Exception 走 B1 Channel。
/// </summary>
internal sealed record AuditWriteBatch(
    OperationLogWriteModel? Operation,
    ExceptionLogWriteModel? Exception)
{
    public AuditWriteKinds Kinds =>
        (Operation is null ? AuditWriteKinds.None : AuditWriteKinds.Operation)
        | (Exception is null ? AuditWriteKinds.None : AuditWriteKinds.Exception);

    public int Count =>
        (Operation is null ? 0 : 1)
        + (Exception is null ? 0 : 1);
}
