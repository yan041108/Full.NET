namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 定义 Full.NET 已签名身份令牌对外公开的 Claim 名称。
/// </summary>
/// <remarks>
/// 这些名称属于跨模块认证契约。调用方只能信任认证中间件已经验证签名、签发者和受众的主体，
/// 不得从普通 Header、查询参数或请求体构造同名值。
/// </remarks>
public static class FullNetIdentityClaimTypes
{
    /// <summary>
    /// 获取刷新会话标识 Claim 名称。
    /// </summary>
    public const string SessionId = "sid";

    /// <summary>
    /// 获取演员账号原始作用域 Claim 名称。
    /// </summary>
    public const string ActorScope = "fullnet_actor_scope";

    /// <summary>
    /// 获取当前有效作用域 Claim 名称。
    /// </summary>
    public const string Scope = "fullnet_scope";

    /// <summary>
    /// 获取账号安全戳 Claim 名称。
    /// </summary>
    public const string SecurityStamp = "fullnet_security_stamp";

    /// <summary>
    /// 获取当前有效租户标识 Claim 名称。
    /// </summary>
    public const string TenantId = "fullnet_tenant_id";

    /// <summary>
    /// 获取可重复权限编码 Claim 名称。
    /// </summary>
    public const string Permission = "fullnet_permission";

    /// <summary>
    /// 获取受保护超级管理员标记 Claim 名称。
    /// </summary>
    public const string SuperAdministrator = "fullnet_super_administrator";
}
