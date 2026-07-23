namespace Full.NET.Caching.Fusion;

public static class CacheKeyBuilder
{
    public static string TenantResolutionByDomain(
        string environment,
        string domain) =>
        ForGlobal(
            environment,
            "tenancy",
            "domain",
            NormalizeDomain(domain),
            "v1");

    public static string TenantResolutionById(
        string environment,
        Guid tenantId) =>
        ForGlobal(
            environment,
            "tenancy",
            "id",
            tenantId.ToString("N"),
            "v1");

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

    public static string ForGlobal(
        string environment,
        string module,
        string resource,
        object id,
        string version) =>
        $"fullnet:{environment.ToLowerInvariant()}:host:{module}:{resource}:{id}:{version}";

    public static string TenantTag(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant tag requires a non-empty tenant identifier.", nameof(tenantId));
        }

        return $"tenant:{tenantId:D}";
    }

    public static string DomainTag(string domain) =>
        $"tenancy:domain:{NormalizeDomain(domain)}";

    private static string NormalizeDomain(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }
}
