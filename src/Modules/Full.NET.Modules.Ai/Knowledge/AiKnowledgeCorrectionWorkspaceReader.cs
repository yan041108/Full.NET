using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>经当前交互授权取得的维护正文；不得缓存为普通检索来源或发布证明。</summary>
internal sealed record AiKnowledgeCorrectionWorkspace(AiKnowledgeCorrectionContext Context, AiKnowledgeCorrectionSnapshot Snapshot);

/// <summary>正文读取前后都复核原会话及 Files；修订记录本身不能授予来源权限。</summary>
internal sealed class AiKnowledgeCorrectionWorkspaceReader(AiKnowledgeCorrectionAuthority authority, AiKnowledgeCorrectionStore store)
{
    internal async Task<AiKnowledgeCorrectionWorkspace?> ReadCurrentAsync(Guid knowledgeId, Guid documentId, Guid versionId,
        SessionBindingSnapshot actor, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(25000); var bounded = deadline.Token;
        var context = await authority.ReadAsync(knowledgeId, documentId, versionId, actor, AiKnowledgeCorrectionPermissions.Read, bounded).ConfigureAwait(false);
        if (context is null) return null;
        var v = context.Version;
        var snapshot = await store.ReadCurrentAsync(context.Binding, AiKnowledgeDocumentFormats.Format(v.ContentType)!.Value, v.ContentHash,
            checked((int)v.SizeBytes), AiKnowledgeDocumentFormats.Policy(v.ContentType, v.ParsingPolicyKey)!, bounded).ConfigureAwait(false);
        if (snapshot is null || !await authority.StillCurrentAsync(context, actor, AiKnowledgeCorrectionPermissions.Read, bounded).ConfigureAwait(false)) return null;
        authority.Check(bounded); return new(context, snapshot);
    }

    /// <summary>重解析结果可与人工头分别读取；选择新来源不会自动改写人工修订。</summary>
    internal async Task<AiKnowledgeCorrectionWorkspace?> ReadSourceAsync(Guid knowledgeId, Guid documentId, Guid versionId, Guid sourceId,
        SessionBindingSnapshot actor, CancellationToken token)
    {
        if (sourceId == Guid.Empty) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(25000); var bounded = deadline.Token;
        var context = await authority.ReadAsync(knowledgeId, documentId, versionId, actor, AiKnowledgeCorrectionPermissions.Read, bounded).ConfigureAwait(false);
        if (context is null) return null;
        var source = await store.ReadSourceAsync(context.Binding, sourceId, bounded).ConfigureAwait(false);
        if (source is null) return null;
        var v = context.Version;
        var snapshot = AiKnowledgeCorrectionSnapshot.Verify(source, null, AiKnowledgeDocumentFormats.Format(v.ContentType)!.Value, v.ContentHash,
            checked((int)v.SizeBytes), AiKnowledgeDocumentFormats.Policy(v.ContentType, v.ParsingPolicyKey)!, bounded);
        if (snapshot is null || !await authority.StillCurrentAsync(context, actor, AiKnowledgeCorrectionPermissions.Read, bounded).ConfigureAwait(false)) return null;
        authority.Check(bounded); return new(context, snapshot);
    }

    /// <summary>历史预览按所选不可变修订定位来源，不追随人工头；仍受当前资源及交互权限保护。</summary>
    internal async Task<AiKnowledgeCorrectionWorkspace?> ReadRevisionAsync(Guid knowledgeId, Guid documentId, Guid versionId, Guid revisionId,
        SessionBindingSnapshot actor, CancellationToken token)
    {
        if (revisionId == Guid.Empty) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(25000); var bounded = deadline.Token;
        var context = await authority.ReadAsync(knowledgeId, documentId, versionId, actor, AiKnowledgeCorrectionPermissions.Read, bounded).ConfigureAwait(false);
        if (context is null) return null;
        var revision = await store.ReadRevisionAsync(context.Binding, revisionId, bounded).ConfigureAwait(false);
        if (revision is null) return null;
        var source = await store.ReadSourceAsync(context.Binding, revision.SourceId, bounded).ConfigureAwait(false);
        if (source is null) return null;
        var v = context.Version;
        var snapshot = AiKnowledgeCorrectionSnapshot.Verify(source, revision, AiKnowledgeDocumentFormats.Format(v.ContentType)!.Value, v.ContentHash,
            checked((int)v.SizeBytes), AiKnowledgeDocumentFormats.Policy(v.ContentType, v.ParsingPolicyKey)!, bounded);
        if (snapshot is null || !await authority.StillCurrentAsync(context, actor, AiKnowledgeCorrectionPermissions.Read, bounded).ConfigureAwait(false)) return null;
        authority.Check(bounded); return new(context, snapshot);
    }
}
