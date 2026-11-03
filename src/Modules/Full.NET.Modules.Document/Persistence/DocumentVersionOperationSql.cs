using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>追加版本的持久化幂等结果；历史版本删除后仍保留已提交操作身份。</summary>
internal static class DocumentVersionOperationSql
{
    public static readonly SqlStatement Find = new("document.version_operation.find", """
        SELECT Id, RequestKey, DocumentId, ActorUserId, VersionId, FileId, ChangeDescription, CreatedAtUtc
        FROM fn_document_version_operation WHERE DocumentId = @DocumentId AND ActorUserId = @ActorUserId AND RequestKey = @RequestKey
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Insert = new("document.version_operation.insert", """
        INSERT INTO fn_document_version_operation (Id, RequestKey, DocumentId, ActorUserId, VersionId, FileId, ChangeDescription, CreatedAtUtc)
        VALUES (@Id, @RequestKey, @DocumentId, @ActorUserId, @VersionId, @FileId, @ChangeDescription, @CreatedAtUtc)
        """, SqlDataScope.HostOnly);
}

/// <summary>幂等结果身份不得随当前版本切换而被重写。</summary>
internal sealed class DocumentVersionOperationRecord
{
    public Guid Id { get; init; }
    public Guid RequestKey { get; init; }
    public Guid DocumentId { get; init; }
    public Guid ActorUserId { get; init; }
    public Guid VersionId { get; init; }
    public Guid FileId { get; init; }
    public string? ChangeDescription { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
