using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>公开人工转换只消费服务器原文和当前 CAS 头；不接收锚点，不运行解析、索引或发布。</summary>
internal sealed class AiKnowledgeCorrectionEditService(AiKnowledgeCorrectionAuthority authority, AiKnowledgeCorrectionStore store, IIdGenerator ids)
{
    internal static readonly string[] EditPermissions = [.. AiKnowledgeCorrectionWorkbenchService.ReadPermissions, AiKnowledgeCorrectionPermissions.Edit];
    internal static readonly string[] RestorePermissions = [.. AiKnowledgeCorrectionWorkbenchService.ReadPermissions, AiKnowledgeCorrectionPermissions.Restore];

    internal Task<Result<AiKnowledgeCorrectionRevisionResponse>> EditAsync(Guid knowledgeId, Guid documentId, Guid versionId,
        AiKnowledgeCorrectionEditRequest request, SessionBindingSnapshot actor, CancellationToken token) =>
        WriteAsync(knowledgeId, documentId, versionId, request.RequestKey, request.Expected, request.Reason, request.Operation, null, null, actor, token);

    internal Task<Result<AiKnowledgeCorrectionRevisionResponse>> RestoreAsync(Guid knowledgeId, Guid documentId, Guid versionId,
        AiKnowledgeCorrectionRestoreRequest request, SessionBindingSnapshot actor, CancellationToken token) =>
        WriteAsync(knowledgeId, documentId, versionId, request.RequestKey, request.Expected, request.Reason, null,
            request.TargetSourceId, request.TargetRevisionId, actor, token);

    private async Task<Result<AiKnowledgeCorrectionRevisionResponse>> WriteAsync(Guid knowledgeId, Guid documentId, Guid versionId,
        Guid key, AiKnowledgeCorrectionHeadRequest expected, string reason, AiKnowledgeCorrectionEditOperation? operation,
        Guid? targetSource, Guid? targetRevision, SessionBindingSnapshot actor, CancellationToken token)
    {
        authority.Check(token);
        if (!Valid(expected) || key == Guid.Empty || reason is not { Length: > 0 and <= 1000 } || string.IsNullOrWhiteSpace(reason)
            || !AiKnowledgeParsedDocument.ValidText(reason, token) || targetSource == Guid.Empty || targetRevision == Guid.Empty
            || (targetSource.HasValue ? operation is not null : !Valid(operation))
            || operation?.Text is { } text && !AiKnowledgeParsedDocument.ValidText(text, token)) return Invalid();
        // 原样保存审计说明；跨客户端不借各自不同的 Unicode Trim 规则改写提交意图。
        var permission = targetSource.HasValue ? AiKnowledgeCorrectionPermissions.Restore : AiKnowledgeCorrectionPermissions.Edit;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(25000); var bounded = deadline.Token;
        var context = await authority.ReadAsync(knowledgeId, documentId, versionId, actor, permission, bounded).ConfigureAwait(false);
        if (context is null) return await UnavailableOrConflict().ConfigureAwait(false);
        var hash = Fingerprint(context, actor, key, expected, reason, operation, targetSource, targetRevision, bounded);
        // 先查原请求，再比较当前 CAS；提交后的响应丢失不能因自身增加文档版本而变成第二次保存。
        var replay = await store.ReadRequestedRevisionAsync(context.Binding, key, bounded).ConfigureAwait(false);
        if (replay is not null)
        {
            if (replay.RequestHash != hash || replay.ActorUserId != actor.UserId) return Conflict();
            var verified = await ReadSnapshot(context, replay.SourceId, replay.Id, bounded).ConfigureAwait(false);
            return verified is not null && await authority.StillCurrentAsync(context, actor, permission, bounded).ConfigureAwait(false)
                ? Result<AiKnowledgeCorrectionRevisionResponse>.Success(Response(replay)) : Unavailable();
        }
        if (context.Binding.DocumentVersion != expected.DocumentVersion) return Conflict();
        var v = context.Version;
        var current = await store.ReadCurrentAsync(context.Binding, AiKnowledgeDocumentFormats.Format(v.ContentType)!.Value, v.ContentHash,
            checked((int)v.SizeBytes), AiKnowledgeDocumentFormats.Policy(v.ContentType, v.ParsingPolicyKey)!, bounded).ConfigureAwait(false);
        if (current is null) return await UnavailableOrConflict().ConfigureAwait(false);
        if (current.Identity != new AiKnowledgeCorrectionIdentity(expected.SourceId, expected.RevisionId, expected.HeadVersion, expected.SourceHash, expected.PayloadHash)) return Conflict();
        AiKnowledgeCorrectionDocument next; string runtime; Guid sourceId;
        if (targetSource is { } source)
        {
            var selected = await ReadSnapshot(context, source, targetRevision, bounded).ConfigureAwait(false);
            if (selected is null) return Unavailable();
            next = selected.Document; runtime = selected.ParserRuntimeId; sourceId = selected.Identity.SourceId;
        }
        else
        {
            try { next = Apply(current.Document, operation!, ids.NewId, bounded); }
            catch (InvalidDataException) { return Invalid(); }
            runtime = current.ParserRuntimeId; sourceId = current.Identity.SourceId;
        }
        if (!await authority.StillCurrentAsync(context, actor, permission, bounded).ConfigureAwait(false)) return await UnavailableOrConflict().ConfigureAwait(false);
        var saved = await store.SaveAsync(context.Binding, sourceId, expected.HeadVersion, key, hash, reason, next, runtime, bounded).ConfigureAwait(false);
        if (!saved.IsSuccess) return Result<AiKnowledgeCorrectionRevisionResponse>.Failure(saved.Error!);
        // 本次提交已增加版本；以新的当前授权结束，不能用提交前的版本把自己的成功误判成冲突。
        var final = await authority.ReadAsync(knowledgeId, documentId, versionId, actor, permission, bounded).ConfigureAwait(false);
        authority.Check(bounded);
        return final is not null ? Result<AiKnowledgeCorrectionRevisionResponse>.Success(Response(saved.Value!)) : Unavailable();

        async Task<Result<AiKnowledgeCorrectionRevisionResponse>> UnavailableOrConflict()
        {
            // 并发提交可使事务外复核失效；重新完成当前授权后，版本竞争必须返回 409 以保留草稿。
            // 撤权、删除或 Files 不可用仍返回不可读，不能仅凭旧快照透露当前资源状态。
            var latest = await authority.ReadAsync(knowledgeId, documentId, versionId, actor, permission, bounded).ConfigureAwait(false);
            return latest is not null && latest.Binding.DocumentVersion != expected.DocumentVersion ? Conflict() : Unavailable();
        }
    }

    internal async Task<Result<AiKnowledgeCorrectionRevisionPageResponse>> HistoryAsync(Guid knowledgeId, Guid documentId, Guid versionId,
        int? before, SessionBindingSnapshot actor, CancellationToken token)
    {
        authority.Check(token);
        if (before is <= 0) return Result<AiKnowledgeCorrectionRevisionPageResponse>.Failure(new Error("ai.knowledge.input_invalid", "Invalid revision cursor.", ErrorType.Validation));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(25000); var bounded = deadline.Token;
        var context = await authority.ReadAsync(knowledgeId, documentId, versionId, actor, AiKnowledgeCorrectionPermissions.Read, bounded).ConfigureAwait(false);
        if (context is null) return HistoryUnavailable();
        var rows = await store.ReadHistoryAsync(context.Binding, before.HasValue ? before.Value : (long)int.MaxValue + 1, bounded).ConfigureAwait(false);
        if (rows.Count > 21 || rows.Any(r => r.Id == Guid.Empty || r.SourceId == Guid.Empty || r.VersionId != versionId || r.HeadVersion < 1
            || r.DocumentVersion < 2 || before.HasValue && r.HeadVersion >= before.Value || !Hash(r.PayloadHash))
            || !rows.Select(r => r.HeadVersion).SequenceEqual(rows.Select(r => r.HeadVersion).Distinct().OrderDescending())
            || !await authority.StillCurrentAsync(context, actor, AiKnowledgeCorrectionPermissions.Read, bounded).ConfigureAwait(false)) return HistoryUnavailable();
        var items = rows.Take(20).Select(r => new AiKnowledgeCorrectionRevisionResponse(r.Id, r.VersionId, r.SourceId, r.ParentId,
            r.HeadVersion, r.DocumentVersion, r.PayloadHash, r.ReasonText, r.CreatedAtUtc)).ToArray();
        authority.Check(bounded);
        return Result<AiKnowledgeCorrectionRevisionPageResponse>.Success(new(items, rows.Count > 20 ? items[^1].HeadVersion : null));
    }

    private async Task<AiKnowledgeCorrectionSnapshot?> ReadSnapshot(AiKnowledgeCorrectionContext context, Guid sourceId, Guid? revisionId, CancellationToken token)
    {
        var source = await store.ReadSourceAsync(context.Binding, sourceId, token).ConfigureAwait(false);
        var revision = revisionId is { } id ? await store.ReadRevisionAsync(context.Binding, id, token).ConfigureAwait(false) : null;
        if (source is null || revisionId.HasValue && revision is null) return null;
        var v = context.Version;
        return AiKnowledgeCorrectionSnapshot.Verify(source, revision, AiKnowledgeDocumentFormats.Format(v.ContentType)!.Value,
            v.ContentHash, checked((int)v.SizeBytes), AiKnowledgeDocumentFormats.Policy(v.ContentType, v.ParsingPolicyKey)!, token);
    }
    internal static AiKnowledgeCorrectionDocument Apply(AiKnowledgeCorrectionDocument document, AiKnowledgeCorrectionEditOperation operation, Func<Guid> ids, CancellationToken token)
    {
        if (!Valid(operation)) throw new InvalidDataException("Invalid correction operation.");
        return operation.Kind switch
        {
            "replace" => document.Replace(operation.BlockId, operation.PartId!.Value, operation.Text!, token),
            "split" => document.Split(operation.BlockId, operation.PartId!.Value, operation.Offset!.Value, ids, token),
            "confirm" => document.Confirm(operation.BlockId, operation.PartId!.Value, token),
            "append" => document.Append(operation.BlockId, operation.Text!, ids, token),
            "exclude" => document.Exclude(operation.BlockId, operation.Excluded!.Value, token),
            "merge" => Merge(),
            _ => throw new InvalidDataException("Invalid correction operation.")
        };
        AiKnowledgeCorrectionDocument Merge()
        {
            var index = -1;
            for (var i = 0; i < document.Blocks.Count; i++) if (document.Blocks[i].Id == operation.BlockId) { index = i; break; }
            if (index < 0 || index + 1 >= document.Blocks.Count) throw new InvalidDataException("No adjacent correction block.");
            return document.Merge(operation.BlockId, document.Blocks[index + 1].Id, token);
        }
    }
    internal static bool Valid(AiKnowledgeCorrectionEditOperation? op)
    {
        if (op is null || op.BlockId == Guid.Empty || op.PartId == Guid.Empty) return false;
        return op.Kind switch
    {
        "replace" => op.PartId.HasValue && op.Text is { Length: <= 32768 } && op.Offset is null && op.Excluded is null,
        "split" => op.PartId.HasValue && op.Text is null && op.Offset is > 0 and < 32768 && op.Excluded is null,
        "confirm" => op.PartId.HasValue && op.Text is null && op.Offset is null && op.Excluded is null,
        "append" => op.PartId is null && op.Text is { Length: > 0 and <= 32768 } && op.Offset is null && op.Excluded is null,
        "merge" => op.PartId is null && op.Text is null && op.Offset is null && op.Excluded is null,
        "exclude" => op.PartId is null && op.Text is null && op.Offset is null && op.Excluded.HasValue,
        _ => false
        };
    }
    private static bool Valid(AiKnowledgeCorrectionHeadRequest? value) => value is { DocumentVersion: > 0 and < int.MaxValue,
        SourceId: var source, HeadVersion: >= 0 and < int.MaxValue } && source != Guid.Empty && value.RevisionId != Guid.Empty
        && (value.HeadVersion == 0 ? value.RevisionId is null : value.RevisionId.HasValue) && Hash(value.SourceHash) && Hash(value.PayloadHash);
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => char.IsAsciiHexDigit(c) && !char.IsAsciiLetterUpper(c));

    internal static string Fingerprint(AiKnowledgeCorrectionContext context, SessionBindingSnapshot actor, Guid key,
        AiKnowledgeCorrectionHeadRequest expected, string reason, AiKnowledgeCorrectionEditOperation? op, Guid? source, Guid? revision, CancellationToken token)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes, new UTF8Encoding(false, true), true);
        try
        {
            var original = context with { Binding = context.Binding with { DocumentVersion = expected.DocumentVersion } };
            writer.Write(AiKnowledgeCorrectionPreparationService.RequestHash(original, actor, source.HasValue ? "knowledge-correction-restore-v1" : "knowledge-correction-edit-v1", token));
            writer.Write(key.ToString("N")); writer.Write(expected.SourceId.ToString("N")); writer.Write(expected.RevisionId?.ToString("N") ?? "");
            writer.Write(expected.HeadVersion); writer.Write(expected.SourceHash); writer.Write(expected.PayloadHash); writer.Write(reason);
            writer.Write(source?.ToString("N") ?? ""); writer.Write(revision?.ToString("N") ?? "");
            if (op is not null) { writer.Write(op.Kind); writer.Write(op.BlockId.ToString("N")); writer.Write(op.PartId?.ToString("N") ?? "");
                writer.Write(op.Text is not null); if (op.Text is not null) writer.Write(op.Text); writer.Write(op.Offset ?? -1); writer.Write(op.Excluded.HasValue); if (op.Excluded.HasValue) writer.Write(op.Excluded.Value); }
            writer.Flush(); token.ThrowIfCancellationRequested();
            return Convert.ToHexStringLower(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
        }
        finally { CryptographicOperations.ZeroMemory(bytes.GetBuffer()); }
    }
    private static AiKnowledgeCorrectionRevisionResponse Response(AiKnowledgeCorrectionRevisionRecord r) =>
        new(r.Id, r.VersionId, r.SourceId, r.ParentId, r.HeadVersion, r.DocumentVersion, r.PayloadHash, r.ReasonText, r.CreatedAtUtc);
    private static Result<AiKnowledgeCorrectionRevisionResponse> Invalid() => Result<AiKnowledgeCorrectionRevisionResponse>.Failure(new Error("ai.knowledge.input_invalid", "Invalid correction operation or audit reason.", ErrorType.Validation));
    private static Result<AiKnowledgeCorrectionRevisionResponse> Conflict() => Result<AiKnowledgeCorrectionRevisionResponse>.Failure(new Error("ai.knowledge.correction_conflict", "The correction changed. Preserve the local draft and reread the head.", ErrorType.Conflict));
    private static Result<AiKnowledgeCorrectionRevisionResponse> Unavailable() => Result<AiKnowledgeCorrectionRevisionResponse>.Failure(new Error("ai.knowledge.correction_unavailable", "The source or current authorization is unavailable.", ErrorType.NotFound));
    private static Result<AiKnowledgeCorrectionRevisionPageResponse> HistoryUnavailable() => Result<AiKnowledgeCorrectionRevisionPageResponse>.Failure(new Error("ai.knowledge.correction_unavailable", "The revision history is unavailable.", ErrorType.NotFound));
}
