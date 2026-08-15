namespace Full.NET.Caching.Fusion;

/// <summary>
/// 统一缓存键构造器，严格输出 7 段格式：<c>fullnet:{env}:{scope}:{module}:{res}:{id}:{ver}</c>。
/// scope 段区分 <c>host</c>（全局共享）与具体 <c>tenantId</c>（租户隔离），禁止业务模块自行拼接字符串。
/// 所有段均以规范小写写入，避免同键多写造成缓存击穿。
/// </summary>
public static class CacheKeyBuilder
{
    /// <summary>
    /// 按域名解析租户的全局共享缓存键。
    /// </summary>
    /// <param name="environment">部署环境名（Development/Staging/Production 等），将自动转小写。</param>
    /// <param name="domain">租户绑定的完整域名，末尾点与大小写自动规范化。</param>
    /// <returns>符合 7 段格式的租户解析缓存键，版本固定为 <c>v1</c>。</returns>
    public static string TenantResolutionByDomain(
        string environment,
        string domain) =>
        ForGlobal(
            environment,
            "tenancy",
            "domain",
            NormalizeDomain(domain),
            "v1");

    /// <summary>
    /// 按租户 ID 直接解析租户信息的全局共享缓存键。
    /// </summary>
    /// <param name="environment">部署环境名，将自动转小写。</param>
    /// <param name="tenantId">租户唯一标识，不能为空。</param>
    /// <returns>符合 7 段格式的租户解析缓存键，版本固定为 <c>v1</c>。</returns>
    public static string TenantResolutionById(
        string environment,
        Guid tenantId) =>
        ForGlobal(
            environment,
            "tenancy",
            "id",
            tenantId.ToString("N"),
            "v1");

    /// <summary>
    /// 构造租户隔离的 7 段缓存键；scope 段写入租户 ID，用于 Backplane 按租户批量失效。
    /// </summary>
    /// <param name="environment">部署环境名，将自动转小写。</param>
    /// <param name="tenantId">业务归属租户 ID，不能为空。</param>
    /// <param name="module">模块键（如 <c>settings</c>/<c>files</c>），低基数可观测指标使用。</param>
    /// <param name="resource">资源类型名（如 <c>grid-preference</c>），应稳定且不可变。</param>
    /// <param name="id">资源主键或复合标识，禁止包含冒号。</param>
    /// <param name="version">契约版本段（如 <c>v1</c>），字段结构变更必须提升版本。</param>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> 为空时抛出。</exception>
    public static string ForTenant(
        string environment,
        Guid tenantId,
        string module,
        string resource,
        object id,
        string version)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant cache key requires a non-empty tenant identifier.", nameof(tenantId));
        }

        return $"fullnet:{environment.ToLowerInvariant()}:{tenantId:D}:{module}:{resource}:{id}:{version}";
    }

    /// <summary>
    /// 构造宿主级全局共享缓存键；scope 段固定为 <c>host</c>。
    /// </summary>
    /// <param name="environment">部署环境名，将自动转小写。</param>
    /// <param name="module">模块键（如 <c>tenancy</c>/<c>auth</c>）。</param>
    /// <param name="resource">资源类型名（如 <c>tenant-resolution</c>）。</param>
    /// <param name="id">资源唯一标识。</param>
    /// <param name="version">契约版本段。</param>
    public static string ForGlobal(
        string environment,
        string module,
        string resource,
        object id,
        string version) =>
        $"fullnet:{environment.ToLowerInvariant()}:host:{module}:{resource}:{id}:{version}";

    /// <summary>
    /// 生成 FusionCache 按租户批量失效标签；与 <see cref="ForTenant"/> 生成的缓存键协同使用。
    /// </summary>
    /// <param name="tenantId">目标租户 ID，不能为空。</param>
    /// <returns>格式化的租户标签字符串：<c>tenant:{Guid:D}</c>。</returns>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> 为空时抛出。</exception>
    public static string TenantTag(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant tag requires a non-empty tenant identifier.", nameof(tenantId));
        }

        return $"tenant:{tenantId:D}";
    }

    /// <summary>
    /// 生成域名解析类缓存的失效标签，用于租户域名变更时精确摘除相关 L1/L2 条目。
    /// </summary>
    /// <param name="domain">规范化前的原始域名字符串。</param>
    /// <returns>格式化的域名标签字符串：<c>tenancy:domain:{normalized}</c>。</returns>
    public static string DomainTag(string domain) =>
        $"tenancy:domain:{NormalizeDomain(domain)}";

    private static string NormalizeDomain(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }
}
