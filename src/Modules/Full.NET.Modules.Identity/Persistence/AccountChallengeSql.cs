using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

internal static class AccountChallengeSql
{
    public static readonly SqlStatement Insert = new(
        "identity.insert_account_challenge",
        """
        INSERT INTO fn_identity_account_challenge
            (ChallengeId, Purpose, NormalizedEmail, CredentialHash, ExpiresAtUtc,
             ConsumedAtUtc, AttemptCount, MaxAttempts, Version, CreatedAtUtc, DeliveryStateKey)
        VALUES
            (@ChallengeId, @Purpose, @NormalizedEmail, @CredentialHash, @ExpiresAtUtc,
             NULL, 0, @MaxAttempts, 1, @CreatedAtUtc, @DeliveryStateKey)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindById = new(
        "identity.find_account_challenge_by_id",
        """
        SELECT ChallengeId, Purpose, NormalizedEmail, CredentialHash, ExpiresAtUtc,
               ConsumedAtUtc, AttemptCount, MaxAttempts, Version, CreatedAtUtc,
               DeliveryStateKey, DeliveryCompletedAtUtc, DeliveryReconciledAtUtc
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
            DeliveryReconciledAtUtc = COALESCE(DeliveryReconciledAtUtc, @ConsumedAtUtc),
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
          AND (DeliveryStateKey = 'accepted' OR DeliveryStateKey IS NULL)
        """,
        SqlDataScope.Global);

    // 日记不保存明文载荷；同一挑战仅完成一次，已对账的未知记录不能被迟到受理重新开放。
    public static readonly SqlStatement CompleteDelivery = new(
        "identity.complete_account_challenge_delivery",
        """
        UPDATE fn_identity_account_challenge
        SET DeliveryStateKey = @DeliveryStateKey,
            DeliveryCompletedAtUtc = @CompletedAtUtc
        WHERE ChallengeId = @ChallengeId
          AND DeliveryStateKey = 'unknown'
          AND DeliveryCompletedAtUtc IS NULL
          AND DeliveryReconciledAtUtc IS NULL
        """,
        SqlDataScope.Global);

    // 仅撤销已完成或已到期的未确认记录；不重发邮件、不读取其他模块表，也不误撤销新挑战。
    public static readonly SqlStatement ReconcileDelivery = new(
        "identity.reconcile_account_challenge_delivery",
        """
        UPDATE fn_identity_account_challenge
        SET Version = CASE WHEN ConsumedAtUtc IS NULL THEN Version + 1 ELSE Version END,
            ConsumedAtUtc = COALESCE(ConsumedAtUtc, @Now),
            DeliveryReconciledAtUtc = @Now
        WHERE ChallengeId = @ChallengeId
          AND DeliveryStateKey IN ('unknown', 'rejected')
          AND DeliveryReconciledAtUtc IS NULL
          AND (DeliveryCompletedAtUtc IS NOT NULL OR ExpiresAtUtc <= @Now)
        """,
        SqlDataScope.Global);
    // 页读取与游标比较保持数据库同一排序；不依赖跨库 Guid 排序一致，也不保存游标到业务记录。
    public static readonly SqlStatement ScanDeliverySqlServer = new(
        "identity.scan_account_challenge_delivery.sql_server",
        """
        SELECT TOP (@BatchSize) ChallengeId, Purpose, NormalizedEmail, CredentialHash, ExpiresAtUtc,
               ConsumedAtUtc, AttemptCount, MaxAttempts, Version, CreatedAtUtc,
               DeliveryStateKey, DeliveryCompletedAtUtc, DeliveryReconciledAtUtc
        FROM fn_identity_account_challenge
        WHERE (@AfterId IS NULL OR ChallengeId > @AfterId)
        ORDER BY ChallengeId
        """,
        SqlDataScope.Global);

    // 页读取与游标比较保持数据库同一排序；不依赖跨库 Guid 排序一致，也不保存游标到业务记录。
    public static readonly SqlStatement ScanDeliveryMySql = new(
        "identity.scan_account_challenge_delivery.mysql",
        """
        SELECT ChallengeId, Purpose, NormalizedEmail, CredentialHash, ExpiresAtUtc,
               ConsumedAtUtc, AttemptCount, MaxAttempts, Version, CreatedAtUtc,
               DeliveryStateKey, DeliveryCompletedAtUtc, DeliveryReconciledAtUtc
        FROM fn_identity_account_challenge
        WHERE (@AfterId IS NULL OR ChallengeId > @AfterId)
        ORDER BY ChallengeId
        LIMIT @BatchSize
        """,
        SqlDataScope.Global);

}
