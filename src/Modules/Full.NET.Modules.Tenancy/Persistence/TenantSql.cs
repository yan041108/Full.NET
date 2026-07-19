using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Persistence;

internal static class TenantSql
{
    public static readonly SqlStatement FindByIdentifier = new(
        "tenancy.find-by-identifier",
        """
        SELECT COUNT(*)
        FROM fn_tenancy_tenant
        WHERE Identifier = @Identifier
        """,
        SqlDataScope.Global);

    // Seeder 只在 Migrator 的可信宿主上下文使用该 Global 查询，并以自然键判断幂等状态。
    public static readonly SqlStatement FindSummaryByIdentifier = new(
        "tenancy.tenant.find_summary_by_identifier",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version
        FROM fn_tenancy_tenant
        WHERE Identifier = @Identifier
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountByDomain = new(
        "tenancy.count-by-domain",
        """
        SELECT COUNT(*)
        FROM fn_tenancy_tenant
        WHERE Domain = @Domain
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindByDomain = new(
        "tenancy.find-by-domain",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE Domain = @Domain
        """,
        SqlDataScope.Global);

    // 按 ID 的 Global 查询只服务于宿主管理员上下文切换，调用方必须先通过权限策略。
    public static readonly SqlStatement FindById = new(
        "tenancy.find-by-explicit-id",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE Id = @TenantId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement GetAvailable = new(
        "tenancy.get-available-for-host-administrator",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE IsActive = 1
        ORDER BY Name, Identifier, Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Insert = new(
        "tenancy.insert",
        """
        INSERT INTO fn_tenancy_tenant
            (Id, Identifier, Name, Domain, IsActive, CreatedAtUtc, Version, DefaultLocale)
        VALUES
            (@Id, @Identifier, @Name, @Domain, @IsActive, @CreatedAtUtc, @Version, @DefaultLocale)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetCurrent = new(
        "tenancy.get-current",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE Id = @TenantId AND IsActive = 1
        """,
        SqlDataScope.TenantRequired);
}
