using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>退出意图与投递状态；CAS 租约保护多 Worker，网络调用不进入数据库事务。</summary>
internal static class IdentityOidcLogoutSql
{
    private const string Targets = """
        SELECT DISTINCT app.CenterSessionId, app.UserId, app.ClientId, client.PropertiesJson
        FROM fn_identity_oidc_application_session AS app
        INNER JOIN fn_identity_oidc_application AS client ON client.Id = app.OidcApplicationId
        INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
        """;
    public static readonly SqlStatement TargetsByCenter = new("identity.oidc_logout_targets_by_center",
        Targets + " WHERE center.Id = @CenterSessionId", SqlDataScope.HostOnly);
    public static readonly SqlStatement TargetsByRevokedUser = new("identity.oidc_logout_targets_by_revoked_user",
        Targets + " WHERE center.UserId = @UserId AND center.RevokedAtUtc = @Now", SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertSqlServer = new("identity.insert_oidc_logout_delivery", """
        INSERT INTO fn_identity_oidc_logout_delivery
            (Id, CenterSessionId, UserId, ClientId, TargetUri, StatusKey, AttemptCount, NextAttemptAtUtc, CreatedAtUtc)
        SELECT @Id, @CenterSessionId, @UserId, @ClientId, @TargetUri, 'pending', 0, @Now, @Now
        WHERE NOT EXISTS (SELECT 1 FROM fn_identity_oidc_logout_delivery WITH (UPDLOCK, HOLDLOCK)
            WHERE CenterSessionId = @CenterSessionId AND ClientId = @ClientId)
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertMySql = new("identity.insert_oidc_logout_delivery_mysql", """
        INSERT INTO fn_identity_oidc_logout_delivery
            (Id, CenterSessionId, UserId, ClientId, TargetUri, StatusKey, AttemptCount, NextAttemptAtUtc, CreatedAtUtc)
        VALUES (@Id, @CenterSessionId, @UserId, @ClientId, @TargetUri, 'pending', 0, @Now, @Now)
        ON DUPLICATE KEY UPDATE Id = Id
        """, SqlDataScope.HostOnly);
    private const string Columns = "Id, CenterSessionId, UserId, ClientId, TargetUri, StatusKey, AttemptCount, NextAttemptAtUtc, LeaseId, LeaseUntilUtc, LastErrorCode, CreatedAtUtc, DeliveredAtUtc";
    private const string Due = "FROM fn_identity_oidc_logout_delivery WHERE StatusKey = 'pending' AND AttemptCount < 8 AND NextAttemptAtUtc <= @Now AND (LeaseId IS NULL OR LeaseUntilUtc <= @Now) ORDER BY NextAttemptAtUtc, Id";
    public static readonly SqlStatement DueSqlServer = new("identity.due_oidc_logout_delivery", $"SELECT TOP (20) {Columns} {Due}", SqlDataScope.HostOnly);
    public static readonly SqlStatement DueMySql = new("identity.due_oidc_logout_delivery_mysql", $"SELECT {Columns} {Due} LIMIT 20", SqlDataScope.HostOnly);
    public static readonly SqlStatement Acquire = new("identity.acquire_oidc_logout_delivery", """
        UPDATE fn_identity_oidc_logout_delivery SET LeaseId = @LeaseId, LeaseUntilUtc = @LeaseUntil,
            AttemptCount = AttemptCount + 1
        WHERE Id = @Id AND StatusKey = 'pending' AND AttemptCount < 8 AND AttemptCount = @AttemptCount AND NextAttemptAtUtc <= @Now
          AND (LeaseId IS NULL OR LeaseUntilUtc <= @Now)
        """, SqlDataScope.HostOnly);
    // 最后一次领取后若 Worker 中断，租约到期必须进入终态，不能无限增加实际发送次数。
    private const string Exhausted = "SET StatusKey = 'failed', LastErrorCode = 'attempts_exhausted', NextAttemptAtUtc = @Now, LeaseId = NULL, LeaseUntilUtc = NULL WHERE StatusKey = 'pending' AND AttemptCount >= 8 AND (LeaseId IS NULL OR LeaseUntilUtc <= @Now)";
    public static readonly SqlStatement ExhaustedSqlServer = new("identity.exhaust_oidc_logout_delivery", $"UPDATE TOP (500) fn_identity_oidc_logout_delivery {Exhausted}", SqlDataScope.HostOnly);
    public static readonly SqlStatement ExhaustedMySql = new("identity.exhaust_oidc_logout_delivery_mysql", $"UPDATE fn_identity_oidc_logout_delivery {Exhausted} LIMIT 500", SqlDataScope.HostOnly);
    public static readonly SqlStatement Complete = new("identity.complete_oidc_logout_delivery", """
        UPDATE fn_identity_oidc_logout_delivery SET StatusKey = 'delivered', DeliveredAtUtc = @Now,
            LeaseId = NULL, LeaseUntilUtc = NULL, LastErrorCode = NULL
        WHERE Id = @Id AND LeaseId = @LeaseId
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Fail = new("identity.fail_oidc_logout_delivery", """
        UPDATE fn_identity_oidc_logout_delivery SET StatusKey = @StatusKey, NextAttemptAtUtc = @NextAttempt,
            LastErrorCode = @ErrorCode, LeaseId = NULL, LeaseUntilUtc = NULL
        WHERE Id = @Id AND LeaseId = @LeaseId
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement ListSqlServer = new("identity.list_oidc_logout_delivery", $"SELECT TOP (100) {Columns} FROM fn_identity_oidc_logout_delivery ORDER BY CreatedAtUtc DESC, Id DESC", SqlDataScope.HostOnly);
    public static readonly SqlStatement ListMySql = new("identity.list_oidc_logout_delivery_mysql", $"SELECT {Columns} FROM fn_identity_oidc_logout_delivery ORDER BY CreatedAtUtc DESC, Id DESC LIMIT 100", SqlDataScope.HostOnly);
    public static readonly SqlStatement Retry = new("identity.retry_oidc_logout_delivery", """
        UPDATE fn_identity_oidc_logout_delivery SET StatusKey = 'pending', AttemptCount = 0,
            NextAttemptAtUtc = @Now, LastErrorCode = NULL
        WHERE Id = @Id AND StatusKey = 'failed' AND LeaseId IS NULL
        """, SqlDataScope.HostOnly);
    // 活跃重试和持有租约的工作不会被保留清理抢先删除。
    public static readonly SqlStatement PruneSqlServer = new("identity.prune_oidc_logout_delivery", """
        DELETE TOP (500) FROM fn_identity_oidc_logout_delivery
        WHERE StatusKey IN ('delivered', 'failed') AND LeaseId IS NULL AND NextAttemptAtUtc < @Threshold
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement PruneMySql = new("identity.prune_oidc_logout_delivery_mysql", """
        DELETE FROM fn_identity_oidc_logout_delivery
        WHERE StatusKey IN ('delivered', 'failed') AND LeaseId IS NULL AND NextAttemptAtUtc < @Threshold LIMIT 500
        """, SqlDataScope.HostOnly);
}

internal sealed record IdentityOidcLogoutTargetRow(Guid CenterSessionId, Guid UserId, string ClientId, string? PropertiesJson);
internal sealed record IdentityOidcLogoutDeliveryRow(Guid Id, Guid CenterSessionId, Guid UserId, string ClientId,
    string TargetUri, string StatusKey, int AttemptCount, DateTimeOffset NextAttemptAtUtc, Guid? LeaseId,
    DateTimeOffset? LeaseUntilUtc, string? LastErrorCode, DateTimeOffset CreatedAtUtc, DateTimeOffset? DeliveredAtUtc);
