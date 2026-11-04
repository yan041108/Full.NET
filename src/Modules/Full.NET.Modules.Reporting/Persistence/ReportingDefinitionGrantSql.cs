using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>Host 写入授权，租户只读取自己已获授权的固定版本；所有值参数化。</summary>
internal static class ReportingDefinitionGrantSql
{
    private const string GrantColumns = "Id, DefinitionId, TenantId, IsEnabled, Version, CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId";
    // 与既有 Definition 物化器顺序一致；执行目录使用发布版本中的参数和布局，不读取编辑中的草稿。
    private const string DefinitionColumns = """
        definition.Id, definition.GroupId, version.DataSourceId, definition.DefinitionKey, definition.Name,
        definition.Description, version.QueryPortKey, version.ParameterSchemaJson, version.LayoutConfigJson,
        version.VersionNumber AS LatestPublishedVersionNumber, definition.IsEnabled,
        definition.CreatedAtUtc, definition.UpdatedAtUtc, definition.Version
        """;
    // 定义级授权覆盖其发布版本；版本级授权只覆盖明确授予的一版。两者共用执行权威。
    internal const string AuthorizedGrantPredicate = """
        (EXISTS (SELECT 1 FROM fn_reporting_definition_tenant_grant AS accessGrant
            WHERE accessGrant.DefinitionId=definition.Id AND accessGrant.TenantId=@TenantId AND accessGrant.IsEnabled=1)
         OR EXISTS (SELECT 1 FROM fn_reporting_definition_version_tenant_grant AS versionGrant
            WHERE versionGrant.DefinitionId=definition.Id AND versionGrant.TenantId=@TenantId AND versionGrant.VersionNumber=version.VersionNumber))
        """;
    internal const string ReadableVersionJoin = """
        FROM fn_reporting_definition AS definition
        INNER JOIN fn_reporting_definition_version AS version ON version.DefinitionId=definition.Id
        INNER JOIN fn_reporting_data_source AS source ON source.Id=version.DataSourceId
        WHERE definition.IsEnabled=1 AND source.IsEnabled=1 AND (source.TenantId IS NULL OR source.TenantId=@TenantId)
        """;
    private const string AuthorizedJoin = $"""
        {ReadableVersionJoin}
          AND {AuthorizedGrantPredicate}
          AND version.VersionNumber = (SELECT MAX(candidate.VersionNumber) FROM fn_reporting_definition_version AS candidate
            INNER JOIN fn_reporting_data_source AS candidateSource ON candidateSource.Id=candidate.DataSourceId
            WHERE candidate.DefinitionId=definition.Id AND candidateSource.IsEnabled=1
              AND (candidateSource.TenantId IS NULL OR candidateSource.TenantId=@TenantId)
              AND (EXISTS (SELECT 1 FROM fn_reporting_definition_tenant_grant AS broad
                    WHERE broad.DefinitionId=definition.Id AND broad.TenantId=@TenantId AND broad.IsEnabled=1)
                OR EXISTS (SELECT 1 FROM fn_reporting_definition_version_tenant_grant AS narrow
                    WHERE narrow.DefinitionId=definition.Id AND narrow.TenantId=@TenantId AND narrow.VersionNumber=candidate.VersionNumber)))
          AND (@NameContains IS NULL OR definition.Name LIKE @NameContains ESCAPE '~')
        """;
    public static readonly SqlStatement LockDefinitionSqlServer = new("reporting.grant.lock_definition.sqlserver",
        "SELECT Id FROM fn_reporting_definition WITH (UPDLOCK,HOLDLOCK) WHERE Id=@DefinitionId", SqlDataScope.HostOnly);
    public static readonly SqlStatement LockDefinitionMySql = new("reporting.grant.lock_definition.mysql",
        "SELECT Id FROM fn_reporting_definition WHERE Id=@DefinitionId FOR UPDATE", SqlDataScope.HostOnly);
    public static readonly SqlStatement Find = new("reporting.grant.find",
        $"SELECT {GrantColumns} FROM fn_reporting_definition_tenant_grant WHERE DefinitionId=@DefinitionId AND TenantId=@TargetTenantId", SqlDataScope.HostOnly);
    public static readonly SqlStatement Count = new("reporting.grant.count",
        "SELECT COUNT(1) FROM fn_reporting_definition_tenant_grant WHERE DefinitionId=@DefinitionId", SqlDataScope.HostOnly);
    public static readonly SqlStatement ListSqlServer = new("reporting.grant.list.sqlserver",
        $"SELECT {GrantColumns} FROM fn_reporting_definition_tenant_grant WHERE DefinitionId=@DefinitionId ORDER BY TenantId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.HostOnly);
    public static readonly SqlStatement ListMySql = new("reporting.grant.list.mysql",
        $"SELECT {GrantColumns} FROM fn_reporting_definition_tenant_grant WHERE DefinitionId=@DefinitionId ORDER BY TenantId LIMIT @PageSize OFFSET @Offset", SqlDataScope.HostOnly);
    public static readonly SqlStatement Insert = new("reporting.grant.insert", """
        INSERT INTO fn_reporting_definition_tenant_grant
            (Id,DefinitionId,TenantId,IsEnabled,Version,CreatedAtUtc,CreatedByUserId,UpdatedAtUtc,UpdatedByUserId)
        VALUES (@Id,@DefinitionId,@TargetTenantId,@IsEnabled,1,@Now,@ActorUserId,NULL,NULL)
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Update = new("reporting.grant.update", """
        UPDATE fn_reporting_definition_tenant_grant SET IsEnabled=@IsEnabled, Version=Version+1, UpdatedAtUtc=@Now, UpdatedByUserId=@ActorUserId
        WHERE DefinitionId=@DefinitionId AND TenantId=@TargetTenantId AND Version=@ExpectedVersion
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement PublishedCount = new("reporting.published.count",
        $"SELECT COUNT(1) {AuthorizedJoin}", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement PublishedListSqlServer = new("reporting.published.list.sqlserver",
        $"SELECT {DefinitionColumns} {AuthorizedJoin} ORDER BY definition.Name,definition.Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement PublishedListMySql = new("reporting.published.list.mysql",
        $"SELECT {DefinitionColumns} {AuthorizedJoin} ORDER BY definition.Name,definition.Id LIMIT @PageSize OFFSET @Offset", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement PublishedDefinition = new("reporting.published.definition",
        $"SELECT {DefinitionColumns} {AuthorizedJoin} AND definition.Id=@DefinitionId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement PublishedVersion = new("reporting.published.version", $"""
        SELECT {ReportingDefinitionSql.VersionColumns}
        {ReadableVersionJoin} AND {AuthorizedGrantPredicate}
          AND definition.Id=@DefinitionId AND version.VersionNumber=@VersionNumber
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement PublishedDataSource = new("reporting.published.data_source", $"""
        SELECT {ReportingDataSourceSql.SelectColumns}
        {ReadableVersionJoin} AND {AuthorizedGrantPredicate}
          AND definition.Id=@DefinitionId AND version.VersionNumber=@VersionNumber AND source.Id=@DataSourceId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
