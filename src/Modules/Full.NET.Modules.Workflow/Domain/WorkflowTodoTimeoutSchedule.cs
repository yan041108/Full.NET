namespace Full.NET.Modules.Workflow.Domain;

/// <summary>由发布版本策略在待办到达时计算的绝对 UTC 调度快照。</summary>
/// <param name="DueAtUtc">逾期时间。</param>
/// <param name="NextReminderAtUtc">首次催办时间。</param>
/// <param name="EscalateAtUtc">升级通知时间。</param>
/// <param name="MaxReminderCount">最大催办次数。</param>
/// <param name="ReminderIntervalMinutes">催办间隔分钟数。</param>
/// <param name="EscalationRecipientUserId">固定升级接收人。</param>
/// <param name="NextTimeoutSignalAtUtc">下一次需由 Worker 处理的时间。</param>
internal sealed record WorkflowTodoTimeoutSchedule(
    DateTimeOffset? DueAtUtc,
    DateTimeOffset? NextReminderAtUtc,
    DateTimeOffset? EscalateAtUtc,
    int MaxReminderCount,
    int ReminderIntervalMinutes,
    Guid? EscalationRecipientUserId,
    DateTimeOffset? NextTimeoutSignalAtUtc)
{
    /// <summary>把相对策略固化为绝对时间；未配置策略时全部保持为空。</summary>
    /// <param name="arrivedAtUtc">待办到达时间。</param>
    /// <param name="policy">发布版本中的闭合策略。</param>
    /// <returns>不可变调度快照。</returns>
    public static WorkflowTodoTimeoutSchedule Create(
        DateTimeOffset arrivedAtUtc,
        WorkflowTodoTimeoutPolicy? policy)
    {
        if (policy is null)
        {
            return new(null, null, null, 0, 0, null, null);
        }

        var due = arrivedAtUtc.AddMinutes(policy.DueAfterMinutes);
        var reminder = policy.MaxReminderCount > 0 ? due : (DateTimeOffset?)null;
        var escalation = policy.EscalationAfterMinutes is { } minutes
            ? arrivedAtUtc.AddMinutes(minutes)
            : (DateTimeOffset?)null;
        return new(due, reminder, escalation, policy.MaxReminderCount,
            policy.ReminderIntervalMinutes,
            policy.EscalationRecipientUserId, Min(reminder, escalation));
    }

    /// <summary>返回两个可空时间中的较早者。</summary>
    /// <param name="left">第一个时间。</param>
    /// <param name="right">第二个时间。</param>
    /// <returns>较早的非空时间。</returns>
    private static DateTimeOffset? Min(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null ? left : left <= right ? left : right;
}
