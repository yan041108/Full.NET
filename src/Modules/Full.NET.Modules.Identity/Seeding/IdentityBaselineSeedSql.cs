using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Seeding;

/// <summary>Identity Baseline 种子使用的跨表只读 SQL；仅限 Migrator 可信宿主上下文。</summary>
internal static class IdentityBaselineSeedSql
{
    public static readonly SqlStatement ListActiveTenants = new(
        "identity.seed.list_active_tenants",
        """
        SELECT Id, Identifier, Name
        FROM fn_tenancy_tenant
        WHERE IsActive = 1
        ORDER BY Identifier, Id
        """,
        SqlDataScope.Global);
}
