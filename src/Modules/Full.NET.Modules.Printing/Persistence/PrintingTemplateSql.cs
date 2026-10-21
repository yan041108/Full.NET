using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Printing.Persistence;

/// <summary>打印模板 SQL 语句。</summary>
internal static class PrintingTemplateSql
{
    private const string TemplateColumns = """
        Id, TemplateKey, Name, FormSchemaKey, LayoutHtml, LatestPublishedVersionNumber,
        IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version
        """;

    private const string VersionColumns = """
        Id, TemplateId, VersionNumber, LayoutHtml, ChangeNote, PublishedByUserId, PublishedAtUtc
        """;

    public static readonly SqlStatement InsertTemplate = new(
        "printing.template.insert",
        $"""
        INSERT INTO fn_printing_template
            ({TemplateColumns})
        VALUES
            (@Id, @TemplateKey, @Name, @FormSchemaKey, @LayoutHtml, @LatestPublishedVersionNumber,
             @IsEnabled, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateTemplate = new(
        "printing.template.update",
        """
        UPDATE fn_printing_template
        SET Name = @Name,
            LayoutHtml = @LayoutHtml,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindTemplateById = new(
        "printing.template.find_by_id",
        $"""
        SELECT {TemplateColumns}
        FROM fn_printing_template
        WHERE Id = @TemplateId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindTemplateByKey = new(
        "printing.template.find_by_key",
        $"""
        SELECT {TemplateColumns}
        FROM fn_printing_template
        WHERE TemplateKey = @TemplateKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListTemplates = new(
        "printing.template.list",
        $"""
        SELECT {TemplateColumns}
        FROM fn_printing_template
        WHERE (@NameContains IS NULL OR Name LIKE @NameContains)
        ORDER BY Name, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteTemplate = new(
        "printing.template.delete",
        """
        DELETE FROM fn_printing_template
        WHERE Id = @TemplateId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PublishTemplate = new(
        "printing.template.publish",
        """
        UPDATE fn_printing_template
        SET LatestPublishedVersionNumber = @LatestPublishedVersionNumber,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TemplateId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertVersion = new(
        "printing.template_version.insert",
        $"""
        INSERT INTO fn_printing_template_version
            ({VersionColumns})
        VALUES
            (@Id, @TemplateId, @VersionNumber, @LayoutHtml, @ChangeNote, @PublishedByUserId, @PublishedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListVersions = new(
        "printing.template_version.list",
        $"""
        SELECT {VersionColumns}
        FROM fn_printing_template_version
        WHERE TemplateId = @TemplateId
        ORDER BY VersionNumber DESC, Id DESC
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindVersionByNumber = new(
        "printing.template_version.find_by_number",
        $"""
        SELECT {VersionColumns}
        FROM fn_printing_template_version
        WHERE TemplateId = @TemplateId
          AND VersionNumber = @VersionNumber
        """,
        SqlDataScope.HostOnly);
    public static readonly SqlStatement CountTemplates = new("printing.counttemplates", "SELECT COUNT(1) FROM fn_printing_template WHERE (@NameContains IS NULL OR Name LIKE @NameContains)", SqlDataScope.HostOnly);
    public static readonly SqlStatement TemplateExists = new("printing.templateexists", "SELECT COUNT(1) FROM fn_printing_template WHERE Id = @TemplateId", SqlDataScope.HostOnly);
    public static readonly SqlStatement CountVersions = new("printing.countversions", "SELECT COUNT(1) FROM fn_printing_template_version WHERE TemplateId = @TemplateId", SqlDataScope.HostOnly);
    public static readonly SqlStatement ListTemplateSummariesSqlServer = new(
        "printing.template_summaries.sqlserver",
        """
        SELECT Id, TemplateKey, Name, FormSchemaKey, '' AS LayoutHtml, LatestPublishedVersionNumber, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version FROM fn_printing_template WHERE (@NameContains IS NULL OR Name LIKE @NameContains) ORDER BY Name, Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement ListTemplateSummariesMySql = new(
        "printing.template_summaries.mysql",
        """
        SELECT Id, TemplateKey, Name, FormSchemaKey, '' AS LayoutHtml, LatestPublishedVersionNumber, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version FROM fn_printing_template WHERE (@NameContains IS NULL OR Name LIKE @NameContains) ORDER BY Name, Id LIMIT @PageSize OFFSET @Offset
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement ListVersionSummariesSqlServer = new(
        "printing.version_summaries.sqlserver",
        """
        SELECT Id, TemplateId, VersionNumber, '' AS LayoutHtml, ChangeNote, PublishedByUserId, PublishedAtUtc FROM fn_printing_template_version WHERE TemplateId = @TemplateId ORDER BY VersionNumber DESC, Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement ListVersionSummariesMySql = new(
        "printing.version_summaries.mysql",
        """
        SELECT Id, TemplateId, VersionNumber, '' AS LayoutHtml, ChangeNote, PublishedByUserId, PublishedAtUtc FROM fn_printing_template_version WHERE TemplateId = @TemplateId ORDER BY VersionNumber DESC, Id DESC LIMIT @PageSize OFFSET @Offset
        """, SqlDataScope.HostOnly);
}
