using Full.NET.Modules.DataApproval.Contracts;

namespace Full.NET.Modules.DataApproval.Domain;

/// <summary>判定 DataApproval 请求是否处于可恢复的业务应用状态。</summary>
internal static class DataApprovalApplicationRules
{
    /// <summary>是否允许对 in_review 请求执行业务应用。</summary>
    /// <param name="statusKey">请求状态键。</param>
    /// <param name="applicationStatusKey">业务应用状态键。</param>
    /// <returns>允许应用时返回 true。</returns>
    internal static bool CanApply(string statusKey, string applicationStatusKey) =>
        string.Equals(statusKey, DataApprovalStatusKeys.InReview, StringComparison.Ordinal)
        && applicationStatusKey is DataApprovalApplicationStatusKeys.None
            or DataApprovalApplicationStatusKeys.PendingApply
            or DataApprovalApplicationStatusKeys.FailedRetryable;

    /// <summary>是否应在 UI 展示人工重试应用入口。</summary>
    /// <param name="statusKey">请求状态键。</param>
    /// <param name="applicationStatusKey">业务应用状态键。</param>
    /// <returns>可人工重试应用时返回 true。</returns>
    internal static bool CanManualRetryApply(string statusKey, string applicationStatusKey) =>
        CanApply(statusKey, applicationStatusKey)
        && applicationStatusKey is DataApprovalApplicationStatusKeys.PendingApply
            or DataApprovalApplicationStatusKeys.FailedRetryable;
}
