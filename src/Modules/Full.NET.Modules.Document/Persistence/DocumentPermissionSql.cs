using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

internal static class DocumentPermissionSql
{
    private const string Projection = """
        Id, DocumentId, UserId, PermissionLevel, CreatedAtUtc
        """;

    public static readonly SqlStatement ListByDocument = new(
        "document.host_permission.list_by_document",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_permission
        WHERE TenantId IS NULL AND DocumentId = @DocumentId
        ORDER BY CreatedAtUtc DESC, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteByDocument = new(
        "document.host_permission.delete_by_document",
        """
        DELETE FROM fn_document_permission
        WHERE TenantId IS NULL AND DocumentId = @DocumentId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "document.host_permission.insert",
        """
        INSERT INTO fn_document_permission
            (Id, TenantId, DocumentId, UserId, PermissionLevel, CreatedAtUtc)
        VALUES
            (@Id, NULL, @DocumentId, @UserId, @PermissionLevel, @CreatedAtUtc)
        """,
        SqlDataScope.HostOnly);
}
