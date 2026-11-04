using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Printing.Persistence;

/// <summary>仅通过本模块版本授权读取 Host 发布快照；租户参数必须来自可信上下文。</summary>
internal static class PrintingTenantGrantSql
{
    /// <summary>Host 分页查看精确版本授权，不连接其他模块的租户表。</summary>
    public static readonly SqlStatement CountGrants = new("printing.count_tenant_version_grants",
        "SELECT COUNT(*) FROM fn_printing_template_tenant_grant WHERE TemplateId = @TemplateId AND VersionNumber = @VersionNumber",
        SqlDataScope.HostOnly);

    private static readonly SqlStatement ListGrantsSqlServer = new("printing.list_tenant_version_grants",
        """
        SELECT TenantId FROM fn_printing_template_tenant_grant
        WHERE TemplateId = @TemplateId AND VersionNumber = @VersionNumber
        ORDER BY CreatedAtUtc, TenantId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """, SqlDataScope.HostOnly);
    private static readonly SqlStatement ListGrantsMySql = new("printing.list_tenant_version_grants",
        """
        SELECT TenantId FROM fn_printing_template_tenant_grant
        WHERE TemplateId = @TemplateId AND VersionNumber = @VersionNumber
        ORDER BY CreatedAtUtc, TenantId LIMIT @PageSize OFFSET @Offset
        """, SqlDataScope.HostOnly);

    public static SqlStatement ListGrants(DatabaseProvider provider) =>
        provider == DatabaseProvider.SqlServer ? ListGrantsSqlServer : ListGrantsMySql;

    private const string CatalogColumns = "version.TemplateId, template.TemplateKey, template.Name AS TemplateName, template.FormSchemaKey, version.VersionNumber";
    private const string GrantedVersionJoin = """
        FROM fn_printing_template_tenant_grant AS tenant_grant
        INNER JOIN fn_printing_template AS template ON template.Id = tenant_grant.TemplateId
        INNER JOIN fn_printing_template_version AS version
            ON version.TemplateId = tenant_grant.TemplateId AND version.VersionNumber = tenant_grant.VersionNumber
        WHERE tenant_grant.TenantId = @TenantId AND template.IsEnabled = 1
        """;

    /// <summary>目录只读取元数据；不加载或返回布局与绑定值。</summary>
    public static readonly SqlStatement ListPublished = new("printing.list_granted_templates",
        $"SELECT {CatalogColumns} {GrantedVersionJoin} ORDER BY template.Name, template.Id, version.VersionNumber DESC",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    private static readonly SqlStatement ResolveVersionSqlServer = new("printing.resolve_granted_version",
        $"SELECT TOP (1) {CatalogColumns}, version.LayoutHtml {GrantedVersionJoin} AND version.TemplateId = @TemplateId AND (@VersionNumber IS NULL OR version.VersionNumber = @VersionNumber) ORDER BY version.VersionNumber DESC",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement ResolveVersionMySql = new("printing.resolve_granted_version",
        $"SELECT {CatalogColumns}, version.LayoutHtml {GrantedVersionJoin} AND version.TemplateId = @TemplateId AND (@VersionNumber IS NULL OR version.VersionNumber = @VersionNumber) ORDER BY version.VersionNumber DESC LIMIT 1",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>默认版本是当前租户获授的最新版本，不能自动跟随全局最新发布。</summary>
    public static SqlStatement ResolveVersion(DatabaseProvider provider) =>
        provider == DatabaseProvider.SqlServer ? ResolveVersionSqlServer : ResolveVersionMySql;

    // SQL Server 用键范围锁保护并发首次授权，唯一索引作为最终兜底。
    private static readonly SqlStatement GrantSqlServer = new(
        "printing.grant_tenant_version",
        """
        INSERT INTO fn_printing_template_tenant_grant (Id, TenantId, TemplateId, VersionNumber, GrantedByUserId, CreatedAtUtc)
        SELECT @Id, @TargetTenantId, @TemplateId, @VersionNumber, @GrantedByUserId, @CreatedAtUtc
        WHERE NOT EXISTS (SELECT 1 FROM fn_printing_template_tenant_grant WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId = @TargetTenantId AND TemplateId = @TemplateId AND VersionNumber = @VersionNumber)
        """, SqlDataScope.HostOnly);
    // MySQL 依赖唯一键实现原子幂等；重复授权不改写最初操作人与时间。
    private static readonly SqlStatement GrantMySql = new(
        "printing.grant_tenant_version",
        """
        INSERT INTO fn_printing_template_tenant_grant (Id, TenantId, TemplateId, VersionNumber, GrantedByUserId, CreatedAtUtc)
        VALUES (@Id, @TargetTenantId, @TemplateId, @VersionNumber, @GrantedByUserId, @CreatedAtUtc)
        ON DUPLICATE KEY UPDATE Id = Id
        """, SqlDataScope.HostOnly);

    public static SqlStatement Grant(DatabaseProvider provider) =>
        provider == DatabaseProvider.SqlServer ? GrantSqlServer : GrantMySql;

    public static readonly SqlStatement Revoke = new("printing.revoke_tenant_version",
        "DELETE FROM fn_printing_template_tenant_grant WHERE TenantId = @TargetTenantId AND TemplateId = @TemplateId AND VersionNumber = @VersionNumber",
        SqlDataScope.HostOnly);
}
