using Full.NET.Modules.DataApproval.Contracts;

namespace Full.NET.Modules.DataApproval.Domain;

/// <summary>判定 DataApproval 请求是否处于可恢复的工作流关联状态。</summary>
internal static class DataApprovalRecoveryRules
{
    /// <summary>是否允许对 pending 且未关联工作流的请求执行恢复。</summary>
    /// <param name="statusKey">请求状态键。</param>
    /// <param name="workflowInstanceId">已关联工作流实例；未关联时为 null。</param>
    /// <param name="recoveryStatusKey">恢复状态键。</param>
    /// <returns>允许恢复时返回 true。</returns>
    internal static bool CanRecoverWorkflowLink(
        string statusKey,
        Guid? workflowInstanceId,
        string recoveryStatusKey) =>
        workflowInstanceId is null
        && string.Equals(statusKey, DataApprovalStatusKeys.Pending, StringComparison.Ordinal)
        && recoveryStatusKey is DataApprovalRecoveryStatusKeys.PendingLink
            or DataApprovalRecoveryStatusKeys.FailedRetryable
            or DataApprovalRecoveryStatusKeys.None;

    /// <summary>是否应在 UI 展示人工重试入口。</summary>
    /// <param name="statusKey">请求状态键。</param>
    /// <param name="workflowInstanceId">已关联工作流实例；未关联时为 null。</param>
    /// <param name="recoveryStatusKey">恢复状态键。</param>
    /// <returns>可人工重试时返回 true。</returns>
    internal static bool CanManualRetry(
        string statusKey,
        Guid? workflowInstanceId,
        string recoveryStatusKey) =>
        CanRecoverWorkflowLink(statusKey, workflowInstanceId, recoveryStatusKey)
        && recoveryStatusKey is not DataApprovalRecoveryStatusKeys.None;
}
