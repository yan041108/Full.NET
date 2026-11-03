using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>永久删除的关联行按固定清单有界清理，避免访问日志或分享历史形成无界末尾事务。</summary>
internal static class DocumentPurgeSql
{
    private static readonly (string Entity, string DocumentColumn)[] Dependents =
    [
        ("preview_task", "DocumentItemId"), ("access_log", "DocumentItemId"),
        ("tag_assignment", "DocumentItemId"), ("version_deletion_audit", "DocumentItemId"),
        ("share_session", "DocumentId"), ("version_operation", "DocumentId"),
        ("permission", "DocumentId"), ("share", "DocumentId"),
    ];

    // 标识符只能来自编译期清单；保留操作者的唯一 ACL 到根删除，下一批仍能重新授权。
    internal static readonly IReadOnlyList<SqlStatement> SqlServer = new[] { new SqlStatement(
        "document.host_recycle_bin.purge_attempt.sqlserver", "DELETE TOP (100) FROM fn_document_preview_attempt WHERE TaskId IN (SELECT Id FROM fn_document_preview_task WHERE DocumentItemId = @DocumentItemId)", SqlDataScope.HostOnly) }.Concat(Dependents.Select(value => new SqlStatement(
        $"document.host_recycle_bin.purge_{value.Entity}.sql_server",
        $"DELETE TOP (100) FROM fn_document_{value.Entity} WHERE {value.DocumentColumn} = @DocumentItemId{(value.Entity == "permission" ? " AND UserId <> @ActorUserId" : "")}", SqlDataScope.HostOnly))).ToArray();
    internal static readonly IReadOnlyList<SqlStatement> MySql = new[] { new SqlStatement(
        "document.host_recycle_bin.purge_attempt.mysql", "DELETE FROM fn_document_preview_attempt WHERE TaskId IN (SELECT Id FROM fn_document_preview_task WHERE DocumentItemId = @DocumentItemId) LIMIT 100", SqlDataScope.HostOnly) }.Concat(Dependents.Select(value => new SqlStatement(
        $"document.host_recycle_bin.purge_{value.Entity}.my_sql",
        $"DELETE FROM fn_document_{value.Entity} WHERE {value.DocumentColumn} = @DocumentItemId{(value.Entity == "permission" ? " AND UserId <> @ActorUserId" : "")} LIMIT 100", SqlDataScope.HostOnly))).ToArray();
}
