using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Persistence;

internal static class TenantSql
{
    public static readonly SqlStatement FindByIdentifier = new(
        "tenancy.find-by-identifier",
        """
        SELECT COUNT(*)
        FROM fn_tenant_tenant
        WHERE Identifier = @Identifier
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountByDomain = new(
        "tenancy.count-by-domain",
        """
        SELECT COUNT(*)
        FROM fn_tenant_tenant
        WHERE Domain = @Domain
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindByDomain = new(
        "tenancy.find-by-domain",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version
        FROM fn_tenant_tenant
        WHERE Domain = @Domain
        """,
        SqlDataScope.Global);

    // 按 ID 的 Global 查询只服务于宿主管理员上下文切换，调用方必须先通过权限策略。
    public static readonly SqlStatement FindById = new(
        "tenancy.find-by-explicit-id",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version
        FROM fn_tenant_tenant
        WHERE Id = @TenantId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement GetAvailable = new(
        "tenancy.get-available-for-host-administrator",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version
        FROM fn_tenant_tenant
        WHERE IsActive = 1
        ORDER BY Name, Identifier, Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Insert = new(
        "tenancy.insert",
        """
        INSERT INTO fn_tenant_tenant
            (Id, Identifier, Name, Domain, IsActive, CreatedAt, Version)
        VALUES
            (@Id, @Identifier, @Name, @Domain, @IsActive, @CreatedAt, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetCurrent = new(
        "tenancy.get-current",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version
        FROM fn_tenant_tenant
        WHERE Id = @TenantId AND IsActive = 1
        """,
        SqlDataScope.TenantRequired);
}
