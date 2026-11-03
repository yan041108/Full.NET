using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>当前会话及权威文件元数据，不是后续读取、解析或发布的长期授权缓存。</summary>
internal sealed record AiKnowledgeCorrectionContext(AiKnowledgeCorrectionStore.Binding Binding, AiKnowledgeVersionRecord Version);

/// <summary>校对只允许本人维护；当前会话、双层来源所有权与 Files 均在 AI 本地事务外复核。</summary>
internal sealed class AiKnowledgeCorrectionAuthority(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IBackgroundSessionAuthorization authorization, IHostFileReferenceReader references, ITenantResourceFileDescriptorReader descriptors)
{
    private static readonly string[] ReadPermissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead, AiKnowledgeCorrectionPermissions.Read];
    private bool Scoped => AiKnowledgeScope.IsTenant(tenant);

    internal async Task<AiKnowledgeCorrectionContext?> ReadAsync(Guid knowledgeId, Guid documentId, Guid versionId,
        SessionBindingSnapshot actor, string permission, CancellationToken token, int? expectedDocumentVersion = null)
    {
        Check(token);
        if (knowledgeId == Guid.Empty || documentId == Guid.Empty || versionId == Guid.Empty
            || !await Authorized(actor, permission, token).ConfigureAwait(false)) return null;
        var p = new Dictionary<string, object?> { ["KnowledgeBaseId"] = knowledgeId, ["DocumentId"] = documentId, ["VersionId"] = versionId, ["OwnerUserId"] = actor.UserId };
        var document = await query.QuerySingleOrDefaultAsync<AiKnowledgeDocumentRecord>(Scoped ? AiKnowledgeDocumentSql.FindTenant : AiKnowledgeDocumentSql.FindHost, p, token).ConfigureAwait(false);
        if (document is null || document.Version < 1 || expectedDocumentVersion.HasValue && document.Version != expectedDocumentVersion.Value) return null;
        p["DocumentVersion"] = document.Version;
        var version = await query.QuerySingleOrDefaultAsync<AiKnowledgeVersionRecord>(Scoped ? AiKnowledgeCorrectionSql.VersionTenant : AiKnowledgeCorrectionSql.VersionHost, p, token).ConfigureAwait(false);
        Check(token);
        if (version is null || version.Id != versionId || version.DocumentId != documentId || version.FileId == Guid.Empty || version.VersionNumber < 1
            || version.ContentHash is not { Length: 64 } || !version.ContentHash.All(char.IsAsciiHexDigit)
            || Domain.AiKnowledgeFilePolicy.Validate(version.OriginalFileName, version.ContentType, document.Version, version.Id, version.SizeBytes) is not null
            || AiKnowledgeDocumentFormats.Policy(version.ContentType, version.ParsingPolicyKey) is null) return null;
        var context = new AiKnowledgeCorrectionContext(new(knowledgeId, documentId, versionId, actor.UserId, document.Version), version);
        if (!await FileReadyAsync(version, actor, token).ConfigureAwait(false) || !await Authorized(actor, permission, token).ConfigureAwait(false)) return null;
        // Files 与 Identity 等待后，以同一 AI 权威版本结束复核；不声称存在跨模块原子事务。
        var final = await query.QuerySingleOrDefaultAsync<AiKnowledgeVersionRecord>(Scoped ? AiKnowledgeCorrectionSql.VersionTenant : AiKnowledgeCorrectionSql.VersionHost, p, token).ConfigureAwait(false);
        Check(token);
        return SameActor(actor) && final is not null && SameVersion(version, final) ? context : null;
    }

    internal async Task<bool> StillCurrentAsync(AiKnowledgeCorrectionContext expected, SessionBindingSnapshot actor, string permission, CancellationToken token)
    {
        var b = expected.Binding;
        var current = await ReadAsync(b.KnowledgeBaseId, b.DocumentId, b.VersionId, actor, permission, token, b.DocumentVersion).ConfigureAwait(false);
        return current is not null && current.Binding == b && SameVersion(expected.Version, current.Version);
    }

    private async Task<bool> Authorized(SessionBindingSnapshot actor, string permission, CancellationToken token)
    {
        Check(token);
        if (permission is not (AiKnowledgeCorrectionPermissions.Read or AiKnowledgeCorrectionPermissions.Prepare or AiKnowledgeCorrectionPermissions.Edit or AiKnowledgeCorrectionPermissions.Restore) || !SameActor(actor)
            || actor.SessionId == Guid.Empty || actor.SessionKind is not (SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication)
            || string.IsNullOrWhiteSpace(actor.SecurityStamp) || actor.SecurityStamp.Length > 128
            || string.IsNullOrWhiteSpace(actor.ActorScope) || actor.ActorScope.Length > 64
            || string.IsNullOrWhiteSpace(actor.EffectiveScope) || actor.EffectiveScope.Length > 64) return false;
        var result = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => authorization.AuthorizeAllAsync(actor, [.. ReadPermissions, permission], ct), token).ConfigureAwait(false);
        Check(token);
        return SameActor(actor) && result?.UserId == actor.UserId && result.TenantId == actor.TenantId && result.SessionId == actor.SessionId;
    }

    /// <summary>已完成代次按其原文件版本复核 Files，不能因新草稿存在而改读新文件；AI 可见性由调用者另验。</summary>
    internal async Task<bool> FileReadyAsync(AiKnowledgeVersionRecord v, SessionBindingSnapshot actor, CancellationToken token)
    {
        Check(token);
        if (!Scoped)
        {
            var file = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => references.GetReadyReferenceAsync(v.FileId, ct), token).ConfigureAwait(false);
            Check(token);
            return SameActor(actor) && file is not null && file.FileId == v.FileId && file.SizeBytes == v.SizeBytes
                && string.Equals(file.ContentHash, v.ContentHash, StringComparison.OrdinalIgnoreCase);
        }
        var descriptor = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => descriptors.GetReadyDescriptorAsync("ai", v.Id, v.FileId, ct), token).ConfigureAwait(false);
        Check(token);
        return SameActor(actor) && descriptor is not null && descriptor.FileId == v.FileId && descriptor.SizeBytes == v.SizeBytes
            && descriptor.ContentType == v.ContentType && descriptor.OriginalFileName == v.OriginalFileName
            && descriptor.CreatedByUserId == v.UploadedByUserId
            && string.Equals(descriptor.ContentHash, v.ContentHash, StringComparison.OrdinalIgnoreCase);
    }
    private bool SameActor(SessionBindingSnapshot actor) => actor.UserId != Guid.Empty && actor.TenantId == tenant.Id
        && (actor.TenantId.HasValue ? tenant.IsAvailable && !tenant.IsHost : tenant.IsHost);
    internal void Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("Correction authority requires a transaction-free boundary.");
    }
    private static bool SameVersion(AiKnowledgeVersionRecord left, AiKnowledgeVersionRecord right) => left.Id == right.Id
        && left.DocumentId == right.DocumentId && left.FileId == right.FileId && left.VersionNumber == right.VersionNumber
        && left.ContentType == right.ContentType && left.ParsingPolicyKey == right.ParsingPolicyKey && left.ContentHash == right.ContentHash
        && left.SizeBytes == right.SizeBytes && left.OriginalFileName == right.OriginalFileName && left.UploadedByUserId == right.UploadedByUserId;
}
