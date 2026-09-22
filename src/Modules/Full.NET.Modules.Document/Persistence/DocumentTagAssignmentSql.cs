using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>文档项与标签关联表的 Dapper SQL。</summary>
internal static class DocumentTagAssignmentSql
{
    public static readonly SqlStatement ListByDocumentItemIds = new(
        "document.host_tag_assignment.list_by_document_item_ids",
        """
        SELECT a.DocumentItemId, a.TagId, t.Name AS TagName
        FROM fn_document_tag_assignment AS a
        INNER JOIN fn_document_tag AS t ON t.Id = a.TagId
        WHERE a.DocumentItemId IN @DocumentItemIds
          AND t.TenantId IS NULL
          AND t.IsDeleted = 0
        ORDER BY t.Name, a.TagId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteByDocumentItemId = new(
        "document.host_tag_assignment.delete_by_document_item_id",
        """
        DELETE FROM fn_document_tag_assignment
        WHERE DocumentItemId = @DocumentItemId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "document.host_tag_assignment.insert",
        """
        INSERT INTO fn_document_tag_assignment (DocumentItemId, TagId, CreatedAtUtc)
        VALUES (@DocumentItemId, @TagId, @CreatedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveTagsByIds = new(
        "document.host_tag.count_active_by_ids",
        """
        SELECT COUNT(1)
        FROM fn_document_tag
        WHERE TenantId IS NULL
          AND IsDeleted = 0
          AND Id IN @TagIds
        """,
        SqlDataScope.HostOnly);
}

internal sealed class DocumentTagAssignmentRow
{
    public Guid DocumentItemId { get; init; }

    public Guid TagId { get; init; }

    public string TagName { get; init; } = string.Empty;
}
