using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Security;

/// <summary>受信任调用者身份；传输适配器负责提供，应用服务不读取 HttpContext。</summary>
internal interface IDocumentActorContext { Guid? UserId { get; } }

/// <summary>文档操作意图，不根据 HTTP 方法或 URL 推断权限。</summary>
internal enum DocumentOperation { Read, Write, Delete, Share }

/// <summary>文档应用边界共用的细粒度访问策略。</summary>
internal interface IDocumentAccessPolicy
{
    Guid? ActorUserId { get; }
    Task<Error?> CheckAsync(Guid documentId, DocumentOperation operation, CancellationToken cancellationToken);
}

/// <summary>始终从权威 ACL 检查；无认证上下文时失败关闭。</summary>
internal sealed class DocumentAccessPolicy(IQueryExecutor queries, IDocumentActorContext actor) : IDocumentAccessPolicy
{
    public Guid? ActorUserId => actor.UserId;

    public async Task<Error?> CheckAsync(Guid documentId, DocumentOperation operation, CancellationToken cancellationToken)
    {
        if (ActorUserId is null) return Denied();
        var allowed = await queries.QuerySingleOrDefaultAsync<long>(DocumentPermissionSql.CheckAccess,
            DocumentSqlParameters.Create(("DocumentId", documentId), ("ActorUserId", ActorUserId),
                ("Operation", operation.ToString().ToLowerInvariant())), cancellationToken).ConfigureAwait(false);
        return allowed == 0 ? Denied() : null;
    }

    private static Error Denied() => new(DocumentErrorCodes.AccessDenied, "Document access denied.", ErrorType.Forbidden);
}

/// <summary>在文件外部副作用或本地写事务开始前完成应用授权。</summary>
internal static class DocumentAccessPolicyExtensions
{
    public static async Task<Result<T>> AuthorizeAsync<T>(this IDocumentAccessPolicy policy,
        Guid documentId, DocumentOperation operation, Func<Task<Result<T>>> action, CancellationToken cancellationToken)
    {
        var error = await policy.CheckAsync(documentId, operation, cancellationToken).ConfigureAwait(false);
        return error is null ? await action().ConfigureAwait(false) : Result<T>.Failure(error);
    }
}
