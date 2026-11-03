using Full.NET.Data.Abstractions;
namespace Full.NET.Modules.Document.Persistence;
/// <summary>永久删除最小事实不保留正文、口令或地址，且不随父文档级联删除。</summary>
internal static class DocumentDeletionFactSql
{
    internal static readonly SqlStatement Insert = new("document.deletion_fact.insert", """
        INSERT INTO fn_document_deletion_fact (Id, DocumentId, ActorUserId, OccurredAtUtc, RetainUntilUtc, ReasonKey, VersionCount, PreviewTaskCount)
        SELECT @Id, @DocumentId, @ActorUserId, @Now, @RetainUntilUtc, 'permanent_delete',
        (SELECT COUNT(1) FROM fn_document_version WHERE DocumentItemId = @DocumentId),
        (SELECT COUNT(1) FROM fn_document_preview_task WHERE DocumentItemId = @DocumentId)
        WHERE NOT EXISTS (SELECT 1 FROM fn_document_deletion_fact WHERE DocumentId = @DocumentId)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement CleanSqlServer = new("document.deletion_fact.clean.sqlserver", """
        DELETE TOP (100) FROM fn_document_deletion_fact WHERE RetainUntilUtc <= @Now
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement CleanMySql = new("document.deletion_fact.clean.mysql", """
        DELETE FROM fn_document_deletion_fact WHERE RetainUntilUtc <= @Now LIMIT 100
        """, SqlDataScope.HostOnly);
}
