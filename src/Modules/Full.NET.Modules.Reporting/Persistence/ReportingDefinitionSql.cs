using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>报表定义与版本管理 SQL。</summary>
internal static class ReportingDefinitionSql
{
    private const string DefinitionColumns = """
        definition.Id,
               definition.GroupId,
               definition.DataSourceId,
               definition.DefinitionKey,
               definition.Name,
               definition.Description,
               definition.QueryPortKey,
               definition.ParameterSchemaJson,
               definition.LayoutConfigJson,
               definition.LatestPublishedVersionNumber,
               definition.IsEnabled,
               definition.CreatedAtUtc,
               definition.UpdatedAtUtc,
               definition.Version
        """;

    private const string VersionColumns = """
        version.Id,
               version.DefinitionId,
               version.VersionNumber,
               version.DataSourceId,
               version.QueryPortKey,
               version.ParameterSchemaJson,
               version.LayoutConfigJson,
               version.ChangeNote,
               version.PublishedByUserId,
               version.PublishedAtUtc
        """;

    public static readonly SqlStatement InsertDefinition = new(
        "reporting.insert_definition",
        """
        INSERT INTO fn_reporting_definition
            (Id, GroupId, DataSourceId, DefinitionKey, Name, Description, QueryPortKey,
             ParameterSchemaJson, LayoutConfigJson, LatestPublishedVersionNumber, IsEnabled,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @GroupId, @DataSourceId, @DefinitionKey, @Name, @Description, @QueryPortKey,
             @ParameterSchemaJson, @LayoutConfigJson, @LatestPublishedVersionNumber, @IsEnabled,
             @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindDefinitionById = new(
        "reporting.find_definition_by_id",
        $"""
        SELECT {DefinitionColumns}
        FROM fn_reporting_definition AS definition
        WHERE definition.Id = @DefinitionId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindDefinitionByKey = new(
        "reporting.find_definition_by_key",
        $"""
        SELECT {DefinitionColumns}
        FROM fn_reporting_definition AS definition
        WHERE definition.DefinitionKey = @DefinitionKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListDefinitions = new(
        "reporting.list_definitions",
        $"""
        SELECT {DefinitionColumns}
        FROM fn_reporting_definition AS definition
        WHERE (@GroupId IS NULL OR definition.GroupId = @GroupId)
          AND (@NameContains IS NULL OR definition.Name LIKE '%' + @NameContains + '%')
        ORDER BY definition.Name, definition.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly string ListDefinitionsMySql = $"""
        SELECT {DefinitionColumns}
        FROM fn_reporting_definition AS definition
        WHERE (@GroupId IS NULL OR definition.GroupId = @GroupId)
          AND (@NameContains IS NULL OR definition.Name LIKE CONCAT('%', @NameContains, '%'))
        ORDER BY definition.Name, definition.Id
        """;

    public static readonly SqlStatement UpdateDefinition = new(
        "reporting.update_definition",
        """
        UPDATE fn_reporting_definition
        SET GroupId = @GroupId,
            DataSourceId = @DataSourceId,
            Name = @Name,
            Description = @Description,
            QueryPortKey = @QueryPortKey,
            ParameterSchemaJson = @ParameterSchemaJson,
            LayoutConfigJson = @LayoutConfigJson,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DefinitionId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PublishDefinition = new(
        "reporting.publish_definition",
        """
        UPDATE fn_reporting_definition
        SET LatestPublishedVersionNumber = @LatestPublishedVersionNumber,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DefinitionId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteDefinition = new(
        "reporting.delete_definition",
        """
        DELETE FROM fn_reporting_definition
        WHERE Id = @DefinitionId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteDefinitionVersions = new(
        "reporting.delete_definition_versions",
        """
        DELETE FROM fn_reporting_definition_version
        WHERE DefinitionId = @DefinitionId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertVersion = new(
        "reporting.insert_definition_version",
        """
        INSERT INTO fn_reporting_definition_version
            (Id, DefinitionId, VersionNumber, DataSourceId, QueryPortKey, ParameterSchemaJson,
             LayoutConfigJson, ChangeNote, PublishedByUserId, PublishedAtUtc)
        VALUES
            (@Id, @DefinitionId, @VersionNumber, @DataSourceId, @QueryPortKey, @ParameterSchemaJson,
             @LayoutConfigJson, @ChangeNote, @PublishedByUserId, @PublishedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListVersions = new(
        "reporting.list_definition_versions",
        $"""
        SELECT {VersionColumns}
        FROM fn_reporting_definition_version AS version
        WHERE version.DefinitionId = @DefinitionId
        ORDER BY version.VersionNumber DESC
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindVersionByNumber = new(
        "reporting.find_definition_version_by_number",
        $"""
        SELECT {VersionColumns}
        FROM fn_reporting_definition_version AS version
        WHERE version.DefinitionId = @DefinitionId
          AND version.VersionNumber = @VersionNumber
        """,
        SqlDataScope.HostOnly);
}
