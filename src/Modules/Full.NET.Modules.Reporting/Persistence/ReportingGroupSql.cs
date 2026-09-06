using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>报表分组管理 SQL。</summary>
internal static class ReportingGroupSql
{
    private const string SelectColumns = """
        grp.Id,
               grp.ParentId,
               grp.Name,
               grp.SortOrder,
               grp.IsEnabled,
               grp.CreatedAtUtc,
               grp.UpdatedAtUtc,
               grp.Version
        """;

    public static readonly SqlStatement Insert = new(
        "reporting.insert_group",
        """
        INSERT INTO fn_reporting_group
            (Id, ParentId, Name, SortOrder, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @ParentId, @Name, @SortOrder, @IsEnabled, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "reporting.find_group_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_reporting_group AS grp
        WHERE grp.Id = @GroupId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement List = new(
        "reporting.list_groups",
        $"""
        SELECT {SelectColumns}
        FROM fn_reporting_group AS grp
        ORDER BY grp.SortOrder, grp.Name, grp.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "reporting.update_group",
        """
        UPDATE fn_reporting_group
        SET ParentId = @ParentId,
            Name = @Name,
            SortOrder = @SortOrder,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @GroupId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountChildren = new(
        "reporting.count_group_children",
        """
        SELECT COUNT(1)
        FROM fn_reporting_group AS grp
        WHERE grp.ParentId = @GroupId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountDefinitions = new(
        "reporting.count_group_definitions",
        """
        SELECT COUNT(1)
        FROM fn_reporting_definition AS definition
        WHERE definition.GroupId = @GroupId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Delete = new(
        "reporting.delete_group",
        """
        DELETE FROM fn_reporting_group
        WHERE Id = @GroupId
        """,
        SqlDataScope.HostOnly);
}
