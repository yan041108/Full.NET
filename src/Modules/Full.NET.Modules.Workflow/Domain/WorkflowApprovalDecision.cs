namespace Full.NET.Modules.Workflow.Domain;

/// <summary>多人审批步骤在一次投票后的收敛结果。</summary>
internal enum WorkflowApprovalOutcome
{
    /// <summary>票数尚未收敛，继续等待其他办理人。</summary>
    Waiting,

    /// <summary>赞成票已经达到审批门槛。</summary>
    Approved,

    /// <summary>剩余票全部赞成也无法达到审批门槛。</summary>
    Rejected,
}

/// <summary>根据数据库权威票数判断多人审批是否已经收敛。</summary>
internal static class WorkflowApprovalDecision
{
    /// <summary>解析一次投票后的步骤结果。</summary>
    /// <param name="requiredApprovalCount">批准步骤所需的赞成票数。</param>
    /// <param name="approvedCount">当前已持久化的赞成票数。</param>
    /// <param name="pendingCount">当前仍未投票的席位数。</param>
    /// <returns>批准、驳回或继续等待。</returns>
    public static WorkflowApprovalOutcome Resolve(
        int requiredApprovalCount,
        int approvedCount,
        int pendingCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredApprovalCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(approvedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(pendingCount);

        if (approvedCount >= requiredApprovalCount)
        {
            return WorkflowApprovalOutcome.Approved;
        }

        // 只要赞成票加全部未决票仍不够门槛，就不再等待无意义的后续投票。
        return approvedCount + pendingCount < requiredApprovalCount
            ? WorkflowApprovalOutcome.Rejected
            : WorkflowApprovalOutcome.Waiting;
    }
}
