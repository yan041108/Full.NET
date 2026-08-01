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
/// 在单个请求作用域内保存 B1 Operation/Exception 槽位；Access 已迁入 Hosting B2 流。
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
