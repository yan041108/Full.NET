namespace Full.NET.Modules.DataApproval.Domain;

/// <summary>DataApproval 请求批准后业务应用状态键。</summary>
public static class DataApprovalApplicationStatusKeys
{
    /// <summary>尚未进入应用阶段或无需应用。</summary>
    public const string None = "none";

    /// <summary>工作流已批准，等待应用或正在应用。</summary>
    public const string PendingApply = "pending_apply";

    /// <summary>变更已成功应用到目标实体。</summary>
    public const string Applied = "applied";

    /// <summary>应用失败但允许自动或人工重试。</summary>
    public const string FailedRetryable = "failed_retryable";

    /// <summary>应用失败且不再自动重试。</summary>
    public const string FailedTerminal = "failed_terminal";
}
