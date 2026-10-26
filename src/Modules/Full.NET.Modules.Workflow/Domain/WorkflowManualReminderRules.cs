namespace Full.NET.Modules.Workflow.Domain;

/// <summary>手动催办是有界知会，不推进流程，也不消费或重置自动SLA提醒计数。</summary>
internal static class WorkflowManualReminderRules
{
    public const int MaximumRequestsPerStep = 20;
    public const int MaximumRecipients = 20;
    public const int MinimumIntervalSeconds = 300;

    public static bool CanRequest(int acceptedCount, DateTimeOffset? lastAcceptedAtUtc, DateTimeOffset now) =>
        acceptedCount is >= 0 and < MaximumRequestsPerStep &&
        (acceptedCount == 0 ? lastAcceptedAtUtc is null : lastAcceptedAtUtc is { } last && now >= last &&
            now - last >= TimeSpan.FromSeconds(MinimumIntervalSeconds));
}
