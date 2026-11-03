using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>签名策略只有一个持久化活动指针；启动配置只初始化，不覆盖管理操作。</summary>
internal static class IdentityOidcSigningPolicySql
{
    public static readonly SqlStatement FindActiveKey = new(
        "identity.find_oidc_active_signing_key",
        "SELECT ActiveKeyId FROM fn_identity_oidc_signing_policy WHERE PolicyKey = 'default'",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindVersion = new(
        "identity.find_oidc_signing_policy_version",
        "SELECT Version FROM fn_identity_oidc_signing_policy WHERE PolicyKey = 'default'",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "identity.insert_oidc_signing_policy",
        """
        INSERT INTO fn_identity_oidc_signing_policy (Id, PolicyKey, ActiveKeyId, Version, UpdatedAtUtc)
        VALUES (@Id, 'default', @ActiveKeyId, 1, @Now)
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement Activate = new(
        "identity.activate_oidc_signing_key",
        """
        UPDATE fn_identity_oidc_signing_policy
        SET ActiveKeyId = @ActiveKeyId, Version = Version + 1, UpdatedAtUtc = @Now
        WHERE PolicyKey = 'default' AND ActiveKeyId = @PreviousKeyId AND Version = @Version
        """, SqlDataScope.HostOnly);
}
