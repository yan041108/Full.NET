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
    private const string AuthorizedJoin = """
        FROM fn_reporting_definition_tenant_grant AS accessGrant
        INNER JOIN fn_reporting_definition AS definition ON definition.Id = accessGrant.DefinitionId
        INNER JOIN fn_reporting_definition_version AS version ON version.DefinitionId = definition.Id
            AND version.VersionNumber = definition.LatestPublishedVersionNumber
        INNER JOIN fn_reporting_data_source AS source ON source.Id=version.DataSourceId
            AND source.IsEnabled=1 AND (source.TenantId IS NULL OR source.TenantId=@TenantId)
        WHERE accessGrant.TenantId = @TenantId AND accessGrant.IsEnabled = 1 AND definition.IsEnabled = 1
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
    public static readonly SqlStatement PublishedVersion = new("reporting.published.version", """
        SELECT version.Id,version.DefinitionId,version.VersionNumber,version.DataSourceId,version.QueryPortKey,
            version.ParameterSchemaJson,version.LayoutConfigJson,version.ChangeNote,version.PublishedByUserId,version.PublishedAtUtc
        FROM fn_reporting_definition_tenant_grant AS accessGrant
        INNER JOIN fn_reporting_definition AS definition ON definition.Id=accessGrant.DefinitionId
        INNER JOIN fn_reporting_definition_version AS version ON version.DefinitionId=definition.Id
        WHERE accessGrant.TenantId=@TenantId AND accessGrant.IsEnabled=1 AND definition.IsEnabled=1
            AND definition.Id=@DefinitionId AND version.VersionNumber=@VersionNumber
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement PublishedDataSource = new("reporting.published.data_source", """
        SELECT source.Id,source.TenantId,source.Name,source.ProviderKey,source.ServerHost,source.Port,source.DatabaseName,
            source.Username,source.PasswordProtected,source.TrustServerCertificate,source.IsEnabled,
            source.LastTestedAtUtc,source.LastTestStatusKey,source.LastTestMessage,source.CreatedAtUtc,source.UpdatedAtUtc,source.Version
        FROM fn_reporting_definition_tenant_grant AS accessGrant
        INNER JOIN fn_reporting_definition AS definition ON definition.Id=accessGrant.DefinitionId
        INNER JOIN fn_reporting_definition_version AS version ON version.DefinitionId=definition.Id
        INNER JOIN fn_reporting_data_source AS source ON source.Id=version.DataSourceId
        WHERE accessGrant.TenantId=@TenantId AND accessGrant.IsEnabled=1 AND definition.IsEnabled=1
            AND definition.Id=@DefinitionId AND version.VersionNumber=@VersionNumber AND source.Id=@DataSourceId
            AND (source.TenantId IS NULL OR source.TenantId=@TenantId)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
