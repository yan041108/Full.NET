namespace Full.NET.Modules.DataApproval.Domain;

/// <summary>DataApproval 请求工作流启动/关联恢复状态键。</summary>
public static class DataApprovalRecoveryStatusKeys
{
    /// <summary>无需恢复或已成功关联工作流。</summary>
    public const string None = "none";

    /// <summary>等待启动工作流并关联实例。</summary>
    public const string PendingLink = "pending_link";

    /// <summary>最近一次恢复尝试失败，允许自动或人工重试。</summary>
    public const string FailedRetryable = "failed_retryable";

    /// <summary>恢复失败且不再自动重试。</summary>
    public const string FailedTerminal = "failed_terminal";
}
