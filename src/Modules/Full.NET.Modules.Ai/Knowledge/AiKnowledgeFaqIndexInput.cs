using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>每次收费准入和外部派发前重新核证 FAQ 修订、完整来源与当前最严格分类。</summary>
internal sealed class AiKnowledgeFaqIndexInput(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IOptions<DatabaseOptions> database, IClock clock, AiKnowledgeFaqDraftSourceGuard sources, AiKnowledgeReadAccess access)
{
    internal Task<AiKnowledgeFaqIndexContent?> ReadAsync(AiKnowledgeIndexTaskBinding binding, Guid revisionId,
        string expectedHash, long expectedSize, string classification, Guid taskId, Guid leaseToken, CancellationToken token) =>
        ReadCoreAsync(binding, revisionId, expectedHash, expectedSize, classification, taskId, leaseToken, token, verifyPayloads: true);

    /// <summary>任务阶段只返回绑定是否仍有效；正文及收费边界另走完整载荷核证，不缓存权限或延长租约。</summary>
    internal async Task<bool> CheckDispatchBindingAsync(AiKnowledgeIndexTaskBinding binding, Guid revisionId,
        string expectedHash, long expectedSize, string classification, Guid taskId, Guid leaseToken, CancellationToken token) =>
        await ReadCoreAsync(binding, revisionId, expectedHash, expectedSize, classification, taskId, leaseToken, token, verifyPayloads: false).ConfigureAwait(false) is not null;

    private async Task<AiKnowledgeFaqIndexContent?> ReadCoreAsync(AiKnowledgeIndexTaskBinding binding, Guid revisionId,
        string expectedHash, long expectedSize, string classification, Guid taskId, Guid leaseToken, CancellationToken token, bool verifyPayloads)
    {
        Check(); var scope = tenant.Id;
        var p = new Dictionary<string, object?> { ["KnowledgeBaseId"] = binding.KnowledgeBaseId, ["DocumentId"] = binding.DocumentId,
            ["VersionId"] = binding.VersionId, ["OwnerUserId"] = binding.OwnerUserId };
        var held = new Dictionary<string, object?>(p) { ["TaskId"] = taskId, ["LeaseToken"] = leaseToken, ["DocumentVersion"] = binding.DocumentVersion,
            ["KnowledgeBaseVersion"] = binding.KnowledgeBaseVersion, ["Now"] = clock.UtcNow };
        var session = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Scoped ? AiKnowledgeIndexSessionSql.HeldTenant : AiKnowledgeIndexSessionSql.HeldHost, held, token).ConfigureAwait(false);
        if (session is null || session.ActorUserId != binding.OwnerUserId || session.TenantId != tenant.Id || session.IndexTaskId != taskId) return null;
        var original = new SessionBindingSnapshot(session.ActorUserId, session.TenantId, session.SessionId, session.SecurityStamp, session.ActorScope, session.EffectiveScope, session.SessionKind);
        var input = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqIndexSourceRecord>(Scoped ? AiKnowledgeFaqIndexSql.FindTenant : AiKnowledgeFaqIndexSql.FindHost, p, token).ConfigureAwait(false);
        if (input is null || input.Id != binding.VersionId || input.RevisionId != revisionId || input.BodyHash != expectedHash || input.SizeBytes != expectedSize || input.DataClassification != classification || input.KnowledgeBaseVersion != binding.KnowledgeBaseVersion) return null;
        p["ModelConfigId"] = input.ModelConfigId;
        var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Scoped ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat, p, token).ConfigureAwait(false);
        if (model is null || !model.IsEnabled || model.Version != input.ModelConfigVersion || AiKnowledgeFaqModelFingerprint.Create(model) != input.ModelFingerprint) return null;
        p["FaqId"] = input.FaqId; p["RevisionId"] = revisionId;
        var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(Scoped ? AiKnowledgeFaqDraftSql.RevisionTenant : AiKnowledgeFaqDraftSql.RevisionHost, p, token).ConfigureAwait(false);
        if (root is null || root.HeadRevisionId != revisionId || root.HeadVersion != binding.DocumentVersion || root.RevisionId != revisionId
            || root.SourceHash != input.SourceHash || root.ContentHash != input.RevisionHash || root.ReservedDocumentId != binding.DocumentId) return null;
        try
        {
            var content = AiKnowledgeFaqDraftPolicy.Decode(root.ContentJson);
            if (content.ExpiresAtUtc is { } end && end <= clock.UtcNow) return null;
            if (AiKnowledgeFaqDraftPolicy.ContentHash(root.ContentJson, root.SourceHash) != root.ContentHash) return null;
            var frozen = AiKnowledgeFaqIndexContent.Create(content);
            if (frozen.Text != input.BodyText || frozen.Hash != expectedHash || frozen.SizeBytes != expectedSize) return null;
            var allowed = verifyPayloads
                ? await sources.AuthorizeAsync([root], binding.OwnerUserId, AiKnowledgeFaqPermissions.Index, token, original: original).ConfigureAwait(false)
                : await sources.AuthorizeIndexBindingsAsync(root, original, token).ConfigureAwait(false);
            if (!allowed.Contains(root.Id)) return null;
            if (await ClassificationAsync(root, binding.OwnerUserId, token).ConfigureAwait(false) != classification) return null;
            Check(); return tenant.Id == scope ? frozen : null;
        }
        catch (InvalidDataException) { return null; }
    }

    internal async Task<string?> ClassificationAsync(AiKnowledgeFaqDraftRecord row, Guid actor, CancellationToken token) =>
        (await new AiKnowledgeFaqClassification(query, transactions, tenant, database, access).ReadAsync(row, actor, token).ConfigureAwait(false))?.Value;
    private bool Scoped => AiKnowledgeScope.IsTenant(tenant);
    private static int Severity(string value) => value switch { "public" => 0, "internal" => 1, "restricted" => 2, _ => -1 };
    private void Check() { if (transactions.HasTransaction) throw new InvalidOperationException("FAQ 索引来源核证不能加入事务。"); }
}
