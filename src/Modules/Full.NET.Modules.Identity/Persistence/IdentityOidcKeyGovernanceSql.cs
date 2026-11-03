using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>公开材料登记、保留窗口与节点就绪租约；数据库从不保存签名私钥。</summary>
internal static class IdentityOidcKeyGovernanceSql
{
    // 同时覆盖期限配置收紧前的合法状态；保守跨密钥取最大值，不根据当前配置推断历史到期。
    public static readonly SqlStatement LatestExpiry = new("identity.latest_oidc_signing_artifact_expiry", """
        SELECT MAX(item.ExpiryUtc) AS ExpiryUtc FROM (
            SELECT ExpirationDateUtc AS ExpiryUtc FROM fn_identity_oidc_token WHERE Status = 'valid'
            UNION ALL
            SELECT ExpiresAtUtc AS ExpiryUtc FROM fn_identity_oidc_center_session WHERE RevokedAtUtc IS NULL
        ) AS item
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Keys = new("identity.oidc_signing_key_lifecycle", """
        SELECT Id, KeyId, PublicKeyHash, PublishedAtUtc, LastActivatedAtUtc, RetireAfterUtc, IsRetired, Version
        FROM fn_identity_oidc_signing_key
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement ValidateKey = new("identity.validate_oidc_signing_key", """
        SELECT COUNT(1) FROM fn_identity_oidc_signing_key
        WHERE KeyId = @KeyId AND PublicKeyHash = @PublicKeyHash AND IsRetired = 0
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertSqlServer = new("identity.publish_oidc_signing_key", """
        INSERT INTO fn_identity_oidc_signing_key (Id, KeyId, PublicKeyHash, PublishedAtUtc, LastActivatedAtUtc, RetireAfterUtc, IsRetired, Version)
        SELECT @Id, @KeyId, @PublicKeyHash, @Now, @LastActivated, @RetireAfter, 0, 1
        WHERE NOT EXISTS (SELECT 1 FROM fn_identity_oidc_signing_key WITH (UPDLOCK, HOLDLOCK) WHERE KeyId = @KeyId)
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertMySql = new("identity.publish_oidc_signing_key_mysql", """
        INSERT INTO fn_identity_oidc_signing_key (Id, KeyId, PublicKeyHash, PublishedAtUtc, LastActivatedAtUtc, RetireAfterUtc, IsRetired, Version)
        VALUES (@Id, @KeyId, @PublicKeyHash, @Now, @LastActivated, @RetireAfter, 0, 1)
        ON DUPLICATE KEY UPDATE Id = Id
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Retain = new("identity.retain_oidc_signing_key", """
        UPDATE fn_identity_oidc_signing_key SET RetireAfterUtc = @RetireAfter, Version = Version + 1
        WHERE KeyId = @KeyId AND IsRetired = 0
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Activated = new("identity.mark_oidc_signing_key_active", """
        UPDATE fn_identity_oidc_signing_key SET LastActivatedAtUtc = @Now, RetireAfterUtc = NULL, Version = Version + 1
        WHERE KeyId = @KeyId AND IsRetired = 0
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement LockRetirementPolicy = new("identity.lock_oidc_signing_key_retirement", """
        UPDATE fn_identity_oidc_signing_policy SET Version = Version + 1, UpdatedAtUtc = @Now
        WHERE PolicyKey = 'default' AND ActiveKeyId <> @KeyId
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Retire = new("identity.retire_oidc_signing_key", """
        UPDATE fn_identity_oidc_signing_key SET IsRetired = 1, Version = Version + 1
        WHERE KeyId = @KeyId AND Version = @Version AND IsRetired = 0
          AND (LastActivatedAtUtc IS NULL OR RetireAfterUtc <= @Now)
        """, SqlDataScope.HostOnly);
    private const string NodeColumns = "Id, RoleKey, SigningManifest, EncryptionFingerprint, PrivateKeysJson, PolicyVersion, ExpiresAtUtc, UpdatedAtUtc";
    public static readonly SqlStatement NodesSqlServer = new("identity.live_oidc_signing_nodes", $"SELECT TOP (256) {NodeColumns} FROM fn_identity_oidc_signing_node WHERE ExpiresAtUtc > @Now ORDER BY UpdatedAtUtc DESC", SqlDataScope.HostOnly);
    public static readonly SqlStatement NodesMySql = new("identity.live_oidc_signing_nodes_mysql", $"SELECT {NodeColumns} FROM fn_identity_oidc_signing_node WHERE ExpiresAtUtc > @Now ORDER BY UpdatedAtUtc DESC LIMIT 256", SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertNodeSqlServer = new("identity.register_oidc_signing_node", $"""
        INSERT INTO fn_identity_oidc_signing_node ({NodeColumns})
        SELECT @Id, @RoleKey, @Manifest, @Encryption, @PrivateKeys, @PolicyVersion, @Expires, @Now
        WHERE NOT EXISTS (SELECT 1 FROM fn_identity_oidc_signing_node WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id)
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertNodeMySql = new("identity.register_oidc_signing_node_mysql", $"""
        INSERT INTO fn_identity_oidc_signing_node ({NodeColumns})
        VALUES (@Id, @RoleKey, @Manifest, @Encryption, @PrivateKeys, @PolicyVersion, @Expires, @Now)
        ON DUPLICATE KEY UPDATE Id = Id
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement UpdateNode = new("identity.renew_oidc_signing_node", """
        UPDATE fn_identity_oidc_signing_node SET RoleKey = @RoleKey, SigningManifest = @Manifest,
            EncryptionFingerprint = @Encryption, PrivateKeysJson = @PrivateKeys, PolicyVersion = @PolicyVersion,
            ExpiresAtUtc = @Expires, UpdatedAtUtc = @Now WHERE Id = @Id
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement PruneNodesSqlServer = new("identity.prune_oidc_signing_nodes", "DELETE TOP (500) FROM fn_identity_oidc_signing_node WHERE ExpiresAtUtc < @Before", SqlDataScope.HostOnly);
    public static readonly SqlStatement PruneNodesMySql = new("identity.prune_oidc_signing_nodes_mysql", "DELETE FROM fn_identity_oidc_signing_node WHERE ExpiresAtUtc < @Before LIMIT 500", SqlDataScope.HostOnly);
}

internal sealed record IdentityOidcSigningKeyRow(Guid Id, string KeyId, string PublicKeyHash, DateTimeOffset PublishedAtUtc,
    DateTimeOffset? LastActivatedAtUtc, DateTimeOffset? RetireAfterUtc, bool IsRetired, int Version);
internal sealed record IdentityOidcSigningNodeRow(Guid Id, string RoleKey, string SigningManifest, string EncryptionFingerprint,
    string PrivateKeysJson, int PolicyVersion, DateTimeOffset ExpiresAtUtc, DateTimeOffset UpdatedAtUtc);
internal sealed record IdentityOidcSigningExpiryRow(DateTimeOffset? ExpiryUtc);
