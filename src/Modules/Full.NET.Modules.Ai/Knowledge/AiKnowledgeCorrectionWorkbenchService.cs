using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>工作台入口不运行解析器；准备只入队，维护正文每页都复核当前交互来源权限。</summary>
internal sealed class AiKnowledgeCorrectionWorkbenchService(AiKnowledgeCorrectionAuthority authority,
    AiKnowledgeCorrectionPreparationQueue queue, AiKnowledgeCorrectionWorkspaceReader reader)
{
    internal static readonly string[] ReadPermissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead, AiKnowledgeCorrectionPermissions.Read];
    internal static readonly string[] PreparePermissions = [.. ReadPermissions, AiKnowledgeCorrectionPermissions.Prepare];

    internal async Task<Result<AiKnowledgeCorrectionPreparationResponse>> PrepareAsync(Guid knowledgeId, Guid documentId, Guid versionId,
        AiKnowledgeCorrectionPrepareRequest request, SessionBindingSnapshot actor, CancellationToken token)
    {
        authority.Check(token);
        if (request.RequestKey == Guid.Empty || request.DocumentVersion is < 1 or int.MaxValue)
            return Failure<AiKnowledgeCorrectionPreparationResponse>("ai.knowledge.input_invalid", "A request identity and current document version are required.", ErrorType.Validation);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(25000); var bounded = deadline.Token;
        var context = await authority.ReadAsync(knowledgeId, documentId, versionId, actor, AiKnowledgeCorrectionPermissions.Prepare, bounded, request.DocumentVersion).ConfigureAwait(false);
        if (context is null) return Unavailable<AiKnowledgeCorrectionPreparationResponse>();
        var saved = await queue.EnqueueAsync(context, actor, request.RequestKey, bounded).ConfigureAwait(false);
        if (!saved.IsSuccess) return Result<AiKnowledgeCorrectionPreparationResponse>.Failure(saved.Error!);
        if (!await authority.StillCurrentAsync(context, actor, AiKnowledgeCorrectionPermissions.Prepare, bounded).ConfigureAwait(false)) return Unavailable<AiKnowledgeCorrectionPreparationResponse>();
        return Result<AiKnowledgeCorrectionPreparationResponse>.Success(Response(saved.Value!));
    }

    internal async Task<Result<AiKnowledgeCorrectionPreparationResponse>> ReadPreparationAsync(Guid knowledgeId, Guid documentId, Guid versionId,
        Guid preparationId, SessionBindingSnapshot actor, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(25000); var bounded = deadline.Token;
        var context = await authority.ReadAsync(knowledgeId, documentId, versionId, actor, AiKnowledgeCorrectionPermissions.Read, bounded).ConfigureAwait(false);
        if (context is null) return Unavailable<AiKnowledgeCorrectionPreparationResponse>();
        var row = preparationId == Guid.Empty ? await queue.ReadLatestAsync(context.Binding, bounded).ConfigureAwait(false)
            : await queue.ReadAsync(context.Binding, preparationId, bounded).ConfigureAwait(false);
        if (row is null || row.ActorUserId != actor.UserId
            || !await authority.StillCurrentAsync(context, actor, AiKnowledgeCorrectionPermissions.Read, bounded).ConfigureAwait(false)) return Unavailable<AiKnowledgeCorrectionPreparationResponse>();
        authority.Check(bounded); return Result<AiKnowledgeCorrectionPreparationResponse>.Success(Response(row));
    }

    internal async Task<Result<AiKnowledgeCorrectionWorkspaceResponse>> ReadWorkspaceAsync(Guid knowledgeId, Guid documentId, Guid versionId,
        Guid? sourceId, int blockIndex, SessionBindingSnapshot actor, CancellationToken token, Guid? revisionId = null)
    {
        authority.Check(token);
        if (sourceId == Guid.Empty || revisionId == Guid.Empty || sourceId.HasValue && revisionId.HasValue || blockIndex is < 0 or >= AiKnowledgeCorrectionDocument.MaxItems)
            return Failure<AiKnowledgeCorrectionWorkspaceResponse>("ai.knowledge.input_invalid", "The workspace page is invalid.", ErrorType.Validation);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(25000); var bounded = deadline.Token;
        var workspace = revisionId is { } revision
            ? await reader.ReadRevisionAsync(knowledgeId, documentId, versionId, revision, actor, bounded).ConfigureAwait(false)
            : sourceId is { } id
            ? await reader.ReadSourceAsync(knowledgeId, documentId, versionId, id, actor, bounded).ConfigureAwait(false)
            : await reader.ReadCurrentAsync(knowledgeId, documentId, versionId, actor, bounded).ConfigureAwait(false);
        if (workspace is null || blockIndex >= workspace.Snapshot.Document.Blocks.Count) return Unavailable<AiKnowledgeCorrectionWorkspaceResponse>();
        var initial = workspace;
        if (workspace.Snapshot.Identity.RevisionId.HasValue && workspace.Snapshot.Document.Blocks[blockIndex].Parts.Any(p => p.Origin == AiKnowledgeCorrectionOrigin.Original && p.AnchorId is null))
        {
            initial = await reader.ReadSourceAsync(knowledgeId, documentId, versionId, workspace.Snapshot.Identity.SourceId, actor, bounded).ConfigureAwait(false);
            if (initial is null || initial.Context.Binding != workspace.Context.Binding || initial.Snapshot.Identity.SourceHash != workspace.Snapshot.Identity.SourceHash
                || initial.Snapshot.ParserRuntimeId != workspace.Snapshot.ParserRuntimeId) return Unavailable<AiKnowledgeCorrectionWorkspaceResponse>();
        }
        var response = Project(workspace, initial.Snapshot.Document, blockIndex, sourceId.HasValue, bounded);
        if (response is null || !await authority.StillCurrentAsync(workspace.Context, actor, AiKnowledgeCorrectionPermissions.Read, bounded).ConfigureAwait(false))
            return Unavailable<AiKnowledgeCorrectionWorkspaceResponse>();
        authority.Check(bounded); return Result<AiKnowledgeCorrectionWorkspaceResponse>.Success(response);
    }

    /// <summary>只返回实际锚点上下文；空单元格用初始不可变片段身份定位，补写永远没有原文坐标。</summary>
    internal static AiKnowledgeCorrectionWorkspaceResponse? Project(AiKnowledgeCorrectionWorkspace workspace,
        AiKnowledgeCorrectionDocument initial, int blockIndex, bool sourceView, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var snapshot = workspace.Snapshot; var document = snapshot.Document;
        if (blockIndex < 0 || blockIndex >= document.Blocks.Count) return null;
        var block = document.Blocks[blockIndex]; var parts = new List<AiKnowledgeCorrectionPartResponse>(block.Parts.Count);
        var anchors = document.Anchors.ToDictionary(a => a.Id);
        Dictionary<Guid, AiKnowledgeParsedBlock>? empty = null;
        foreach (var part in block.Parts)
        {
            token.ThrowIfCancellationRequested();
            string? original = null; int? offset = null, length = null; AiKnowledgeSourceLocationResponse? location = null;
            if (part.AnchorId is { } id)
            {
                if (!anchors.TryGetValue(id, out var anchor)) return null;
                var source = document.Source.Blocks[anchor.BlockSequence]; var position = source.Locate(anchor.Offset, anchor.Length);
                if (position is null) return null;
                original = source.Text.Substring(anchor.Offset, anchor.Length); offset = anchor.Offset; length = anchor.Length;
                location = AiKnowledgeSourceLocations.Create(source.Sequence, position, document.Source);
            }
            else if (part.Origin == AiKnowledgeCorrectionOrigin.Original)
            {
                if (part.Text.Length != 0) return null;
                if (empty is null)
                {
                    var cells = document.Source.Blocks.Where(b => b.Text.Length == 0).ToArray();
                    var originalParts = initial.Blocks.SelectMany(b => b.Parts).Where(p => p.Origin == AiKnowledgeCorrectionOrigin.Original && p.AnchorId is null && p.Text.Length == 0).ToArray();
                    if (cells.Length != originalParts.Length) return null;
                    empty = originalParts.Select((p, i) => (p.Id, Cell: cells[i])).ToDictionary(p => p.Id, p => p.Cell);
                }
                if (!empty.TryGetValue(part.Id, out var cell)) return null;
                original = ""; offset = 0; length = 0; location = AiKnowledgeSourceLocations.Create(cell.Sequence, cell.Position, document.Source);
            }
            else if (part.Origin != AiKnowledgeCorrectionOrigin.Supplement) return null;
            var kind = part.Origin switch { AiKnowledgeCorrectionOrigin.Original => "original", AiKnowledgeCorrectionOrigin.Corrected => "corrected", _ => "supplement" };
            if (!AiKnowledgeSourceLocations.Valid(location, reviewed: true)) return null;
            parts.Add(new(part.Id, part.Text, part.Prefix, kind, part.AnchorId, original, offset, length, location));
        }
        var b = workspace.Context.Binding; var v = workspace.Context.Version; var identity = snapshot.Identity;
        return new(b.VersionId, b.DocumentVersion, identity.SourceId, identity.RevisionId, identity.HeadVersion, identity.SourceHash,
            identity.PayloadHash, snapshot.ParserRuntimeId, v.ContentType, v.OriginalFileName, blockIndex, document.Blocks.Count, sourceView,
            new(block.Id, block.Excluded, block.Separator, parts.AsReadOnly()));
    }

    private static AiKnowledgeCorrectionPreparationResponse Response(Persistence.AiKnowledgeCorrectionPreparationRecord row) =>
        new(row.Id, row.VersionId, row.DocumentVersion, row.StateKey, row.AttemptCount, row.SourceId, row.FailureCode, row.CreatedAtUtc, row.UpdatedAtUtc);
    private static Result<T> Unavailable<T>() => Failure<T>("ai.knowledge.correction_unavailable", "The correction source or current authorization is unavailable.", ErrorType.NotFound);
    private static Result<T> Failure<T>(string code, string message, ErrorType type) => Result<T>.Failure(new Error(code, message, type));
}
