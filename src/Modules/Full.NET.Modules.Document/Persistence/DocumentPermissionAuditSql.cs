using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>全局 ACL 治理的前后值审计，与权限替换同事务提交。</summary>
internal static class DocumentPermissionAuditSql
{
    public static readonly SqlStatement Insert = new("document.permission_audit.insert", """
        INSERT INTO fn_document_permission_audit
            (Id, DocumentId, ActorUserId, PreviousMode, NewMode, BeforeActionsJson, AfterActionsJson, CreatedAtUtc)
        VALUES (@Id, @DocumentId, @ActorUserId, @PreviousMode, @NewMode, @BeforeActionsJson, @AfterActionsJson, @CreatedAtUtc)
        """, SqlDataScope.HostOnly);
}
