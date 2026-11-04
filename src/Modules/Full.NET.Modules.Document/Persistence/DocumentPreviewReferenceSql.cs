using Full.NET.Data.Abstractions;
namespace Full.NET.Modules.Document.Persistence;
/// <summary>意图在 Files 调用前提交；准备中与有效租约内的释放意图均参与文件保留。</summary>
internal static class DocumentPreviewReferenceSql
{
    internal static readonly SqlStatement Insert = new("document.preview_reference.insert", """
        INSERT INTO fn_document_preview_reference (Id, DocumentId, TaskId, FileId, Purpose, StateKey, CreatedAtUtc)
        VALUES (@Id, @DocumentId, @TaskId, @FileId, @Purpose, 'preparing', @Now)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ByTask = new("document.preview_reference.by_task", """
        SELECT Id, DocumentId, TaskId, FileId, Purpose, StateKey, ReleaseAfterUtc, CreatedAtUtc
        FROM fn_document_preview_reference WHERE TaskId = @TaskId
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Claim = new("document.preview_reference.claim", """
        UPDATE fn_document_preview_reference SET StateKey = 'claimed' WHERE Id = @Id AND StateKey = 'preparing'
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Release = new("document.preview_reference.release", """
        UPDATE fn_document_preview_reference SET StateKey = @StateKey, ReleaseAfterUtc = @ReleaseAfterUtc
        WHERE Id = @Id AND StateKey NOT IN ('release_pending','released')
        """, SqlDataScope.HostOnly);
    // 与版本引用共同用于批量对账，释放等待期内仍必须保留对象。
    internal static readonly SqlStatement FindForClaimProbe = new("document.preview_reference.for_claim_probe", """
        SELECT Id, DocumentId, TaskId, FileId, Purpose, StateKey, ReleaseAfterUtc, CreatedAtUtc
        FROM fn_document_preview_reference WHERE Id IN @ReferenceIds
          AND (StateKey <> 'released' OR ReleaseAfterUtc > @Now)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Exists = new("document.preview_reference.exists", """
        SELECT CASE WHEN EXISTS (SELECT 1 FROM fn_document_preview_reference WHERE Id = @ReferenceId AND FileId = @FileId
        AND (StateKey <> 'released' OR ReleaseAfterUtc > @Now)) THEN 1 ELSE 0 END
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FileReferenced = new("document.preview_reference.file_referenced", """
        SELECT CASE WHEN EXISTS (SELECT 1 FROM fn_document_preview_reference WHERE FileId = @FileId
        AND (StateKey <> 'released' OR ReleaseAfterUtc > @Now)) OR EXISTS
        (SELECT 1 FROM fn_document_preview_task WHERE SourceReferenceId IS NULL AND (SourceFileId = @FileId OR OutputFileId = @FileId))
        THEN 1 ELSE 0 END
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ByDocumentSqlServer = new("document.preview_reference.by_document.sqlserver", """
        SELECT TOP (100) Id, DocumentId, TaskId, FileId, Purpose, StateKey, ReleaseAfterUtc, CreatedAtUtc
        FROM fn_document_preview_reference WHERE DocumentId = @DocumentId AND StateKey NOT IN ('release_pending','released') ORDER BY Id
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ByDocumentMySql = new("document.preview_reference.by_document.mysql", """
        SELECT Id, DocumentId, TaskId, FileId, Purpose, StateKey, ReleaseAfterUtc, CreatedAtUtc
        FROM fn_document_preview_reference WHERE DocumentId = @DocumentId AND StateKey NOT IN ('release_pending','released') ORDER BY Id LIMIT 100
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement DueSqlServer = new("document.preview_reference.due.sqlserver", """
        SELECT TOP (100) Id, DocumentId, TaskId, FileId, Purpose, StateKey, ReleaseAfterUtc, CreatedAtUtc
        FROM fn_document_preview_reference WHERE StateKey = 'release_pending' AND ReleaseAfterUtc <= @Now ORDER BY ReleaseAfterUtc, Id
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement DueMySql = new("document.preview_reference.due.mysql", """
        SELECT Id, DocumentId, TaskId, FileId, Purpose, StateKey, ReleaseAfterUtc, CreatedAtUtc
        FROM fn_document_preview_reference WHERE StateKey = 'release_pending' AND ReleaseAfterUtc <= @Now ORDER BY ReleaseAfterUtc, Id LIMIT 100
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FinalRelease = new("document.preview_reference.final_release", """
        UPDATE fn_document_preview_reference SET StateKey = 'released' WHERE Id = @Id AND StateKey = 'release_pending' AND ReleaseAfterUtc <= @Now
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement DocumentLeaseEnd = new("document.preview_reference.document_lease_end", """
        SELECT MAX(LeaseExpiresAtUtc) FROM fn_document_preview_task WHERE DocumentItemId = @DocumentId
        """, SqlDataScope.HostOnly);

}
