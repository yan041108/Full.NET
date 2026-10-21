namespace Full.NET.Modules.DataApproval.Execution;

/// <summary>恢复扫描跨作用域保留进度，停用租户和坏记录不会阻塞后续请求。</summary>
internal sealed class DataApprovalRecoveryCursor<TProcessor>
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal DateTimeOffset SubmittedAtUtc { get; set; } = DateTimeOffset.UnixEpoch;
    internal Guid? Id { get; set; }
}
