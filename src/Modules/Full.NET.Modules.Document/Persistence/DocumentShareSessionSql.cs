using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>一次打开会话与配额同事务；不通过全局计数猜测某次请求是否已扣次。</summary>
internal static class DocumentShareSessionSql
{
    private const string Columns = "Id, ShareId, DocumentId, VersionId, RequestKey, AccessRevision, PolicyRevision, CreatedAtUtc, ExpiresAtUtc";
    public static readonly SqlStatement FindByRequest = new("document.share_session.find_by_request",
        $"SELECT {Columns} FROM fn_document_share_session WHERE ShareId = @ShareId AND RequestKey = @RequestKey", SqlDataScope.HostOnly);
    public static readonly SqlStatement FindById = new("document.share_session.find_by_id",
        $"SELECT {Columns} FROM fn_document_share_session WHERE Id = @Id", SqlDataScope.HostOnly);
    public static readonly SqlStatement Insert = new("document.share_session.insert", """
        INSERT INTO fn_document_share_session
            (Id, ShareId, DocumentId, VersionId, RequestKey, AccessRevision, PolicyRevision, CreatedAtUtc, ExpiresAtUtc)
        VALUES (@Id, @ShareId, @DocumentId, @VersionId, @RequestKey, @AccessRevision, @PolicyRevision, @CreatedAtUtc, @ExpiresAtUtc)
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement DeleteExpired = new("document.share_session.delete_expired",
        "DELETE FROM fn_document_share_session WHERE Id = @Id AND ExpiresAtUtc < @Before", SqlDataScope.HostOnly);
    public static readonly SqlStatement ExpiredSqlServer = new("document.share_session.expired.sql_server",
        "SELECT TOP (100) Id FROM fn_document_share_session WHERE ExpiresAtUtc < @Before ORDER BY ExpiresAtUtc, Id", SqlDataScope.HostOnly);
    public static readonly SqlStatement ExpiredMySql = new("document.share_session.expired.my_sql",
        "SELECT Id FROM fn_document_share_session WHERE ExpiresAtUtc < @Before ORDER BY ExpiresAtUtc, Id LIMIT 100", SqlDataScope.HostOnly);
}

/// <summary>固定版本与策略修订，访问计数变化不撤销其他会话。</summary>
internal sealed class DocumentShareSessionRecord
{
    public Guid Id { get; init; }
    public Guid ShareId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public Guid RequestKey { get; init; }
    public long AccessRevision { get; init; }
    public long PolicyRevision { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
}
