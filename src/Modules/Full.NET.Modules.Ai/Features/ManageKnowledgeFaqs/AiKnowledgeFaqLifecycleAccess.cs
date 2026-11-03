using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;

/// <summary>FAQ 生命周期统一使用当前会话、库内读者及完整来源交集；事务内不调用外部端口。</summary>
internal sealed class AiKnowledgeFaqLifecycleAccess(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IClock clock, IOptions<DatabaseOptions> database, AiKnowledgeReadAccess access, IBackgroundSessionAuthorization authorization,
    AiKnowledgeFaqDraftSourceGuard sources, AiKnowledgeFaqIndexInput input, AiKnowledgeIndexActivationStore proof)
{
    internal bool Scoped => AiKnowledgeScope.IsTenant(tenant);
    internal DatabaseProvider Provider => database.Value.Provider;
    internal DateTimeOffset Now { get { var now = clock.UtcNow; return new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero); } }
    internal static readonly string[] ReadPermissions = [AiKnowledgePermissions.Read, AiKnowledgeFaqPermissions.Read,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];

    internal async Task<bool> AuthorizedAsync(SessionBindingSnapshot actor, string operation, CancellationToken token, params string[] additional)
    {
        Check(actor, token);
        if (actor.UserId == Guid.Empty || actor.SessionId == Guid.Empty || actor.SessionKind is not (SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication)) return false;
        var result = await authorization.AuthorizeAllAsync(actor, [.. ReadPermissions, operation, .. additional], token).ConfigureAwait(false);
        Check(actor, token);
        return result?.UserId == actor.UserId && result.SessionId == actor.SessionId && result.TenantId == actor.TenantId;
    }

    internal async Task<Dictionary<string, object?>> ParametersAsync(Guid knowledge, Guid faq, SessionBindingSnapshot actor, CancellationToken token)
    {
        Check(actor, token); var p = await access.ParametersAsync(actor.UserId, token).ConfigureAwait(false); Check(actor, token);
        p["KnowledgeBaseId"] = knowledge; p["Id"] = knowledge; p["FaqId"] = faq; p["Now"] = Now;
        p["ActorUserId"] = actor.UserId; p["CurrentActorUserId"] = actor.UserId; p["CurrentSessionId"] = actor.SessionId;
        return p;
    }

    internal async Task<AiKnowledgeFaqDraftRecord?> ReadAsync(Dictionary<string, object?> p, Guid? revision, CancellationToken token, bool owned)
    {
        p["RevisionId"] = revision;
        var statement = owned ? revision.HasValue ? Choose(AiKnowledgeFaqDraftSql.RevisionHost, AiKnowledgeFaqDraftSql.RevisionTenant)
            : Choose(AiKnowledgeFaqDraftSql.ReadHost, AiKnowledgeFaqDraftSql.ReadTenant) : AiKnowledgeFaqLifecycleSql.RevisionFor(Provider, Scoped);
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(statement, p, token).ConfigureAwait(false);
        if (row is null || row.KnowledgeBaseId != (Guid)p["KnowledgeBaseId"]! || row.Id != (Guid)p["FaqId"]! || row.TenantId != tenant.Id) return null;
        _ = Content(row); p["DocumentId"] = row.ReservedDocumentId; return row;
    }

    internal async Task<bool> ProveAsync(AiKnowledgeFaqDraftRecord row, SessionBindingSnapshot actor, string operation, bool owned, CancellationToken token)
    {
        Check(actor, token);
        var result = await sources.AuthorizeAsync([row], actor.UserId, operation, token, requireOwner: owned).ConfigureAwait(false);
        Check(actor, token); return result.Contains(row.Id);
    }

    internal async Task<AiKnowledgeIndexStatusRecord?> CandidateAsync(AiKnowledgeFaqDraftRecord root, Guid version, Dictionary<string, object?> p,
        SessionBindingSnapshot actor, string operation, bool owned, CancellationToken token)
    {
        if (root.HeadRevisionId != root.RevisionId || !await ProveAsync(root, actor, operation, owned, token).ConfigureAwait(false)) return null;
        p["VersionId"] = version;
        var source = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.ForFaq(Provider, Scoped), p, token).ConfigureAwait(false);
        if (source is null || source.FaqRevisionId != root.RevisionId || source.FileId is not null || source.VersionId != version
            || source.DocumentId != root.ReservedDocumentId || source.DocumentVersion != root.HeadVersion || source.IsCurrent != 1
            || source.IsDeleted != 0 || source.CleanupPending != 0 || source.StateKey != "completed" || source.TaskId is null
            || source.ActiveTaskId != source.TaskId || source.ActiveLeaseToken is null) return null;
        if (await input.ClassificationAsync(root, actor.UserId, token).ConfigureAwait(false) != source.DataClassification
            || !await proof.IsReadyForReadAsync(source, p, token).ConfigureAwait(false)) return null;
        Check(actor, token);
        var final = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.ForFaq(Provider, Scoped), p, token).ConfigureAwait(false);
        return source == final && await ProveAsync(root, actor, operation, owned, token).ConfigureAwait(false) ? source : null;
    }

    internal static AiKnowledgeFaqContent Content(AiKnowledgeFaqDraftRecord row)
    {
        if (row.RevisionId == Guid.Empty || row.SourceHash.Length != 64 || AiKnowledgeFaqDraftPolicy.ContentHash(row.ContentJson, row.SourceHash) != row.ContentHash) throw new InvalidDataException();
        return AiKnowledgeFaqDraftPolicy.Decode(row.ContentJson);
    }

    internal static IReadOnlyList<AiKnowledgeSearchSource> PublicSources(AiKnowledgeFaqDraftRecord row) => AiKnowledgeFaqGraphs.Public(AiKnowledgeFaqProvenanceCodec.Decode(row));
    internal SqlStatement Choose(SqlStatement host, SqlStatement scoped) => Scoped ? scoped : host;
    internal void Check(SessionBindingSnapshot actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction || actor.TenantId != tenant.Id || tenant.Id == Guid.Empty || Scoped != tenant.Id.HasValue) throw new UnauthorizedAccessException();
    }
    internal static Result<T> Failure<T>(string code, ErrorType type) => Result<T>.Failure(new Error(code, "The FAQ lifecycle operation is unavailable.", type));
    internal static Result<T> Missing<T>() => Failure<T>("ai.knowledge_faqs.not_found", ErrorType.NotFound);
    internal static Result<T> Conflict<T>() => Failure<T>("ai.knowledge_faqs.conflict", ErrorType.Conflict);
}
