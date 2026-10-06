using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

internal static class AccountChallengeSql
{
    public static readonly SqlStatement Insert = new(
        "identity.insert_account_challenge",
        """
        INSERT INTO fn_identity_account_challenge
            (ChallengeId, Purpose, NormalizedEmail, CredentialHash, ExpiresAtUtc,
             ConsumedAtUtc, AttemptCount, MaxAttempts, Version, CreatedAtUtc)
        VALUES
            (@ChallengeId, @Purpose, @NormalizedEmail, @CredentialHash, @ExpiresAtUtc,
             NULL, 0, @MaxAttempts, 1, @CreatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindById = new(
        "identity.find_account_challenge_by_id",
        """
        SELECT ChallengeId, Purpose, NormalizedEmail, CredentialHash, ExpiresAtUtc,
               ConsumedAtUtc, AttemptCount, MaxAttempts, Version, CreatedAtUtc
        FROM fn_identity_account_challenge
        WHERE ChallengeId = @ChallengeId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InvalidateActive = new(
        "identity.invalidate_active_account_challenges",
        """
        UPDATE fn_identity_account_challenge
        SET ConsumedAtUtc = @ConsumedAtUtc,
            Version = Version + 1
        WHERE Purpose = @Purpose
          AND NormalizedEmail = @NormalizedEmail
          AND ConsumedAtUtc IS NULL
          AND ExpiresAtUtc > @ConsumedAtUtc
        """,
        SqlDataScope.Global);

    // 失败补偿按请求所属挑战定位，已消费或过期记录保持幂等无操作。
    public static readonly SqlStatement InvalidateById = new(
        "identity.invalidate_account_challenge",
        """
        UPDATE fn_identity_account_challenge
        SET ConsumedAtUtc = @ConsumedAtUtc,
            Version = Version + 1
        WHERE ChallengeId = @ChallengeId
          AND ConsumedAtUtc IS NULL
          AND ExpiresAtUtc > @ConsumedAtUtc
        """,
        SqlDataScope.Global);

    // 错误尝试是可累加的原子更新，不能按读取版本丢弃并发请求；消费仍使用版本校验。
    public static readonly SqlStatement IncrementAttempt = new(
        "identity.increment_account_challenge_attempt",
        """
        UPDATE fn_identity_account_challenge
        SET AttemptCount = AttemptCount + 1,
            Version = Version + 1
        WHERE ChallengeId = @ChallengeId
          AND ConsumedAtUtc IS NULL
          AND AttemptCount < MaxAttempts
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Consume = new(
        "identity.consume_account_challenge",
        """
        UPDATE fn_identity_account_challenge
        SET ConsumedAtUtc = @ConsumedAtUtc,
            Version = Version + 1
        WHERE ChallengeId = @ChallengeId
          AND ConsumedAtUtc IS NULL
          AND ExpiresAtUtc > @ConsumedAtUtc
          AND CredentialHash = @CredentialHash
          AND AttemptCount < MaxAttempts
          AND Version = @Version
        """,
        SqlDataScope.Global);
}
