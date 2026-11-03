namespace Full.NET.Modules.Calendar.Persistence;

/// <summary>接收方原操作事实；永久保留参数摘要，不依赖资源是否仍存在。</summary>
internal sealed record PersonalScheduleCompletionRecord(Guid OperationId, string RequestDigest, string Outcome,
    string? Reason, int? ActualVersion, DateTimeOffset? CompletedAtUtc);
