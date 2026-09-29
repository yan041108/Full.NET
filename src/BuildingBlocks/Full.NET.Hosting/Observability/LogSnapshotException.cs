namespace Full.NET.Hosting.Observability;

/// <summary>
/// 只输出已审定的异常类型摘要，避免旧 Sink 再调用原始异常的 ToString 泄漏消息或栈。
/// </summary>
internal sealed class LogSnapshotException(string safeSummary) : Exception
{
    public override string ToString() => safeSummary;
}
