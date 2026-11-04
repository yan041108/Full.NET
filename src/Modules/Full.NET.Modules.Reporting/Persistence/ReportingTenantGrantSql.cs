using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>仅通过本模块版本授权读取 Host 发布快照；租户参数必须来自可信上下文。</summary>
internal static class ReportingTenantGrantSql
{
    /// <summary>Host 分页查看精确版本授权，不连接其他模块的租户表。</summary>
    public static readonly SqlStatement CountGrants = new("reporting.count_tenant_version_grants",
        "SELECT COUNT(*) FROM fn_reporting_definition_version_tenant_grant WHERE DefinitionId = @DefinitionId AND VersionNumber = @VersionNumber",
        SqlDataScope.HostOnly);

    private static readonly SqlStatement ListGrantsSqlServer = new("reporting.list_tenant_version_grants",
        """
        SELECT TenantId FROM fn_reporting_definition_version_tenant_grant
        WHERE DefinitionId = @DefinitionId AND VersionNumber = @VersionNumber
        ORDER BY CreatedAtUtc, TenantId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """, SqlDataScope.HostOnly);
    private static readonly SqlStatement ListGrantsMySql = new("reporting.list_tenant_version_grants",
        """
        SELECT TenantId FROM fn_reporting_definition_version_tenant_grant
        WHERE DefinitionId = @DefinitionId AND VersionNumber = @VersionNumber
        ORDER BY CreatedAtUtc, TenantId LIMIT @PageSize OFFSET @Offset
        """, SqlDataScope.HostOnly);

    public static SqlStatement ListGrants(DatabaseProvider provider) =>
        provider == DatabaseProvider.SqlServer ? ListGrantsSqlServer : ListGrantsMySql;

    // 逐版本目录和执行都复用同一权威范围，不以另一套 JOIN 绕过停用或数据源归属。
    public static readonly SqlStatement ListPublished = new("reporting.list_granted_definitions", $"""
        SELECT version.DefinitionId, definition.DefinitionKey, definition.Name,
               version.VersionNumber, version.QueryPortKey, version.ParameterSchemaJson, version.LayoutConfigJson
        {ReportingDefinitionGrantSql.ReadableVersionJoin} AND {ReportingDefinitionGrantSql.AuthorizedGrantPredicate}
        ORDER BY definition.Name, definition.Id, version.VersionNumber DESC
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListPublishedHost = new(
        "reporting.list_published_host_definitions",
        """
        SELECT version.DefinitionId, definition.DefinitionKey, definition.Name,
               version.VersionNumber, version.QueryPortKey, version.ParameterSchemaJson, version.LayoutConfigJson
        FROM fn_reporting_definition AS definition
        INNER JOIN fn_reporting_definition_version AS version
            ON version.DefinitionId = definition.Id AND version.VersionNumber = definition.LatestPublishedVersionNumber
        WHERE definition.IsEnabled = 1
        ORDER BY definition.Name, definition.Id
        """, SqlDataScope.HostOnly);

    private static readonly SqlStatement GrantSqlServer = new(
        "reporting.grant_tenant_version",
        """
        INSERT INTO fn_reporting_definition_version_tenant_grant (Id, TenantId, DefinitionId, VersionNumber, GrantedByUserId, CreatedAtUtc)
        SELECT @Id, @TargetTenantId, @DefinitionId, @VersionNumber, @GrantedByUserId, @CreatedAtUtc
        WHERE NOT EXISTS (SELECT 1 FROM fn_reporting_definition_version_tenant_grant WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId = @TargetTenantId AND DefinitionId = @DefinitionId AND VersionNumber = @VersionNumber)
        """, SqlDataScope.HostOnly);
    private static readonly SqlStatement GrantMySql = new(
        "reporting.grant_tenant_version",
        """
        INSERT INTO fn_reporting_definition_version_tenant_grant (Id, TenantId, DefinitionId, VersionNumber, GrantedByUserId, CreatedAtUtc)
        VALUES (@Id, @TargetTenantId, @DefinitionId, @VersionNumber, @GrantedByUserId, @CreatedAtUtc)
        ON DUPLICATE KEY UPDATE Id = Id
        """, SqlDataScope.HostOnly);

    public static SqlStatement Grant(DatabaseProvider provider) =>
        provider == DatabaseProvider.SqlServer ? GrantSqlServer : GrantMySql;

    public static readonly SqlStatement Revoke = new("reporting.revoke_tenant_version",
        "DELETE FROM fn_reporting_definition_version_tenant_grant WHERE TenantId = @TargetTenantId AND DefinitionId = @DefinitionId AND VersionNumber = @VersionNumber",
        SqlDataScope.HostOnly);
}
