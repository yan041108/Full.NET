namespace Full.NET.Modules.Jobs.Execution;

/// <summary>
/// 表示某一采样时刻的 Host Jobs 待处理与到期重试快照。
/// </summary>
/// <param name="PendingCount">全部待处理执行数量，包含尚未到期的重试。</param>
/// <param name="OldestClaimableCreatedAtUtc">当前可领取执行中最早的 UTC 创建时间。</param>
/// <param name="DueRetryCount">已到重试时间的待处理执行数量。</param>
/// <param name="OldestDueRetryAtUtc">已到期重试中最早的 UTC 到期时间。</param>
internal sealed record JobsBacklogSnapshot(
    long PendingCount,
    DateTimeOffset? OldestClaimableCreatedAtUtc,
    long DueRetryCount,
    DateTimeOffset? OldestDueRetryAtUtc);
