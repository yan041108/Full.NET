using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.GoView.Persistence;

/// <summary>GoView 大屏项目 SQL 语句。</summary>
internal static class GoViewProjectSql
{
    private const string ProjectColumns = """
        Id, ProjectKey, Name, CanvasJson, LatestPublishedVersionNumber,
        IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version
        """;

    private const string VersionColumns = """
        Id, ProjectId, VersionNumber, CanvasJson, ChangeNote, PublishedByUserId, PublishedAtUtc
        """;

    public static readonly SqlStatement InsertProject = new(
        "goview.project.insert",
        $"""
        INSERT INTO fn_goview_project
            ({ProjectColumns})
        VALUES
            (@Id, @ProjectKey, @Name, @CanvasJson, @LatestPublishedVersionNumber,
             @IsEnabled, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateProject = new(
        "goview.project.update",
        """
        UPDATE fn_goview_project
        SET Name = @Name,
            CanvasJson = @CanvasJson,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindProjectById = new(
        "goview.project.find_by_id",
        $"""
        SELECT {ProjectColumns}
        FROM fn_goview_project
        WHERE Id = @ProjectId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindProjectByKey = new(
        "goview.project.find_by_key",
        $"""
        SELECT {ProjectColumns}
        FROM fn_goview_project
        WHERE ProjectKey = @ProjectKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListProjects = new(
        "goview.project.list",
        $"""
        SELECT {ProjectColumns}
        FROM fn_goview_project
        WHERE (@NameContains IS NULL OR Name LIKE @NameContains)
        ORDER BY Name, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PublishProject = new(
        "goview.project.publish",
        """
        UPDATE fn_goview_project
        SET LatestPublishedVersionNumber = @LatestPublishedVersionNumber,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ProjectId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertVersion = new(
        "goview.project_version.insert",
        $"""
        INSERT INTO fn_goview_project_version
            ({VersionColumns})
        VALUES
            (@Id, @ProjectId, @VersionNumber, @CanvasJson, @ChangeNote, @PublishedByUserId, @PublishedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListVersions = new(
        "goview.project_version.list",
        $"""
        SELECT {VersionColumns}
        FROM fn_goview_project_version
        WHERE ProjectId = @ProjectId
        ORDER BY VersionNumber DESC, Id DESC
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindVersionByNumber = new(
        "goview.project_version.find_by_number",
        $"""
        SELECT {VersionColumns}
        FROM fn_goview_project_version
        WHERE ProjectId = @ProjectId
          AND VersionNumber = @VersionNumber
        """,
        SqlDataScope.HostOnly);
}
