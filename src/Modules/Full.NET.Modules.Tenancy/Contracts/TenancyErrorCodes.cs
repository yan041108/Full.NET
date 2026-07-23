namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>
/// 定义 Tenancy 模块对外返回的稳定错误码。
/// </summary>
public static class TenancyErrorCodes
{
    /// <summary>
    /// Tenancy 错误码前缀。
    /// </summary>
    public const string Prefix = "tenancy.";

    /// <summary>已认证租户上下文与请求主机不匹配。</summary>
    public const string ContextMismatch = "tenancy.context_mismatch";

    /// <summary>请求切换到的租户上下文不存在。</summary>
    public const string ContextNotFound = "tenancy.context_not_found";

    /// <summary>租户域名已被占用。</summary>
    public const string DomainExists = "tenancy.domain_exists";

    /// <summary>请求主机没有对应的活动租户。</summary>
    public const string HostNotFound = "tenancy.host_not_found";

    /// <summary>租户标识已被占用。</summary>
    public const string IdentifierExists = "tenancy.identifier_exists";

    /// <summary>当前租户不存在。</summary>
    public const string NotFound = "tenancy.not_found";

    /// <summary>不能禁用最后一个仍处于活动状态的租户。</summary>
    public const string LastActiveTenant = "tenancy.tenant.last_remaining";

    /// <summary>租户记录版本冲突。</summary>
    public const string VersionConflict = "tenancy.tenant.version_conflict";

    /// <summary>
    /// 获取当前目录中的全部稳定错误码。
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        ContextMismatch,
        ContextNotFound,
        DomainExists,
        HostNotFound,
        IdentifierExists,
        LastActiveTenant,
        NotFound,
        VersionConflict,
    ]);
}
