namespace Full.NET.Modules.Identity.Persistence;

internal sealed record AccountChallengeRecord(
    Guid ChallengeId,
    byte Purpose,
    string NormalizedEmail,
    string CredentialHash,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    int AttemptCount,
    int MaxAttempts,
    int Version,
    DateTimeOffset CreatedAtUtc)
{
    // MySQL 驱动的 tinyint 与 datetime 返回类型不同于位置构造签名，供 Dapper 逐列映射。
    public AccountChallengeRecord()
        : this(
            Guid.Empty,
            0,
            string.Empty,
            string.Empty,
            DateTimeOffset.MinValue,
            null,
            0,
            0,
            0,
            DateTimeOffset.MinValue)
    {
    }
}
