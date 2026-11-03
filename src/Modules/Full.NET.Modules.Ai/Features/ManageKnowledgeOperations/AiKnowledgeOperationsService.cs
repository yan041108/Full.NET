using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeOperations;

/// <summary>运营元数据独立授权；当前库所有者范围不能用超级管理员或普通读者身份代替。</summary>
internal sealed class AiKnowledgeOperationsService(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    ICurrentSessionAuthorization authorization, IClock clock, IOptions<DatabaseOptions> database)
{
    internal static readonly string[] Permissions = [AiKnowledgeOperationsPermissions.Read, AiKnowledgePermissions.Read,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgePublicationPermissions.Read, AiKnowledgeFaqPermissions.Read];

    internal async Task<Result<AiKnowledgeOperationsPage>> ReadAsync(Guid actor, Guid? knowledgeBaseId, Guid? afterId,
        string? view, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken token)
    {
        Outside();
        var scope = tenant.Id; var host = tenant.IsHost; var now = clock.UtcNow;
        var end = toUtc?.ToUniversalTime() ?? now;
        if (fromUtc is null && end.UtcTicks < TimeSpan.FromDays(30).Ticks) return Fail(ErrorType.Validation, AiKnowledgeErrorCodes.InputInvalid);
        var start = fromUtc?.ToUniversalTime() ?? end.AddDays(-30); view ??= "all";
        if (actor == Guid.Empty || knowledgeBaseId == Guid.Empty || afterId == Guid.Empty || view is not ("all" or "failed" or "expired")
            || end > now || start >= end || end - start > TimeSpan.FromDays(31))
            return Fail(ErrorType.Validation, AiKnowledgeErrorCodes.InputInvalid);
        var session = await authorization.AuthorizeAllAsync(Permissions, token).ConfigureAwait(false);
        Outside();
        if (session is null || session.UserId != actor || session.TenantId != scope || session.SessionId == Guid.Empty || !Same()) return Denied();
        var parameters = new Dictionary<string, object?> { ["ActorUserId"] = actor, ["KnowledgeBaseId"] = knowledgeBaseId,
            ["AfterId"] = afterId, ["View"] = view, ["FromUtc"] = start, ["ToUtc"] = end, ["Now"] = now };
        var rows = (await query.QueryAsync<AiKnowledgeOperationRecord>(AiKnowledgeOperationsSql.For(database.Value.Provider, !host), parameters, token).ConfigureAwait(false)).ToArray();
        Outside(); token.ThrowIfCancellationRequested(); if (!Same()) return Denied();
        var current = await authorization.AuthorizeAllAsync(Permissions, token).ConfigureAwait(false);
        Outside(); token.ThrowIfCancellationRequested();
        if (current is null || current.UserId != actor || current.TenantId != scope || current.SessionId != session.SessionId || !Same()) return Denied();
        // 再读相同有界页，让等待授权期间发生的转移/删除/停用及发布变化也在最后 SQL 收敛。
        now = clock.UtcNow; parameters["Now"] = now;
        rows = (await query.QueryAsync<AiKnowledgeOperationRecord>(AiKnowledgeOperationsSql.For(database.Value.Provider, !host), parameters, token).ConfigureAwait(false)).ToArray();
        Outside(); token.ThrowIfCancellationRequested(); if (!Same()) return Denied();
        if (rows.Length > 26 || rows.Any(row => Project(row, now) is null)) return Fail(ErrorType.Conflict, "ai.knowledge.index_unavailable");
        return Result<AiKnowledgeOperationsPage>.Success(new(rows.Take(25).Select(row => Project(row, now)!).ToArray(),
            rows.Length > 25 ? rows[24].VersionId : null, start, end, view));

        bool Same() => tenant.Id == scope && tenant.IsHost == host;
    }

    internal static AiKnowledgeOperationItem? Project(AiKnowledgeOperationRecord row, DateTimeOffset now)
    {
        if (row.KnowledgeBaseId == Guid.Empty || row.DocumentId == Guid.Empty || row.VersionId == Guid.Empty || row.VersionNumber < 1
            || row.AttemptCount < 0 || row.HasActiveGeneration is < 0 or > 1
            || (row.SourceKind == "document-v1" ? row.FaqId is not null : row.SourceKind != "faq-revision-v1" || row.FaqId is null || row.FaqId == Guid.Empty)
            || (row.PublicationId is null) != (row.PublicationVersionId is null)
            || row.TaskId == Guid.Empty || row.PublicationId == Guid.Empty || row.PublicationVersionId == Guid.Empty
            || (row.PublicationId is not null && (row.EffectiveFromUtc is null || row.EffectiveToUtc is null || row.EffectiveFromUtc >= row.EffectiveToUtc))) return null;
        var task = row.TaskId is null ? "not_enqueued" : row.TaskState == "running"
            ? row.TaskLeaseExpiresAtUtc is null || row.TaskLeaseExpiresAtUtc <= now ? "lease_expired" : "processing"
            : row.TaskState is "queued" or "completed" or "failed" or "cancelled" ? row.TaskState : "unknown";
        var preparation = row.PreparationState is null ? "not_recorded" : row.PreparationState is "static_revision" or "awaiting_upload" or "file_uploaded" or "pending_confirmation"
            or "pending" or "processing" or "completed" or "failed" or "cancelled" or "cleanup_pending" ? row.PreparationState : "unknown";
        var review = row.ReviewState is null ? "none" : row.ReviewState is "pending" or "proposed" or "approved" or "rejected" or "published" or "withdrawn" ? row.ReviewState : "unknown";
        var publication = row.PublicationId is null ? "none" : row.PublicationState != "published" ? "unknown"
            : row.EffectiveToUtc <= now ? "expired_record" : row.EffectiveFromUtc > now ? "scheduled_record" : "published_record";
        return new(row.KnowledgeBaseId, row.DocumentId, row.VersionId, row.VersionNumber, row.SourceKind, row.FaqId,
            preparation, row.TaskId, task, row.AttemptCount, task == "failed" || preparation == "failed" ? "ai.knowledge.index_unavailable" : null,
            row.CreatedAtUtc, row.TaskUpdatedAtUtc, row.HasActiveGeneration == 1, review, row.PublicationId, row.PublicationVersionId,
            publication, row.EffectiveFromUtc, row.EffectiveToUtc);
    }
    private void Outside() { if (transactions.HasTransaction) throw new InvalidOperationException("Knowledge operations cannot join an ambient transaction."); }
    private static Result<AiKnowledgeOperationsPage> Denied() => Fail(ErrorType.Forbidden, "authorization.permission_denied");
    private static Result<AiKnowledgeOperationsPage> Fail(ErrorType type, string code) => Result<AiKnowledgeOperationsPage>.Failure(new(code, "The knowledge operations query is unavailable or invalid.", type));
}
