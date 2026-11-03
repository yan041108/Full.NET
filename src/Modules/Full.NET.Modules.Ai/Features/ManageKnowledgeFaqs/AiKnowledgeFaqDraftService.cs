using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using Full.NET.AI.Abstractions.Models;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;

/// <summary>当前交付仅维护不可变草稿；不物化预留文档，不允许普通文档入口承载未核证的派生正文。</summary>
internal sealed partial class AiKnowledgeFaqDraftService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IOptions<DatabaseOptions> database, IClock clock, IIdGenerator ids,
    AiKnowledgePermissionGuard permissions, AiKnowledgeRetrievalService retrieval, AiKnowledgeFaqDraftSourceGuard sources,
    AiKnowledgeFaqIndexInput transferInput, AiDataProcessingPolicy processing, AiKnowledgeFaqFeedbackPreparation feedback,
    AiPersonalScheduleSourceReader? personalSchedules = null)
{
    private sealed record Context(Guid Actor, Guid? TenantId, bool Scoped);
    private sealed record Saved(Guid Id, Guid RevisionId, bool Replayed);
    private sealed record TransferApproval(int SourceVersion, string SourceRootClassification, int TargetVersion, string TargetClassification, string MinimumClassification);
    private sealed record CopyBinding(Guid KnowledgeBaseId, Guid Id, Guid RevisionId, int Version, TransferApproval? Transfer = null);
    private sealed class CorruptDraftException : Exception { }
    private SqlStatement Choose(Context context, SqlStatement host, SqlStatement scoped) => context.Scoped ? scoped : host;
    private Dictionary<string, object?> Parameters(Guid knowledge, Context context, Guid? faq = null)
    {
        if (knowledge == Guid.Empty || faq == Guid.Empty) throw new ArgumentException("FAQ 路径身份不能为空。");
        return new() { ["KnowledgeBaseId"] = knowledge, ["OwnerUserId"] = context.Actor, ["FaqId"] = faq };
    }
    private void Check(Context context, bool inside = false)
    {
        if ((!inside && transactions.HasTransaction) || tenant.Id != context.TenantId || AiKnowledgeScope.IsTenant(tenant) != context.Scoped) throw new UnauthorizedAccessException();
    }
    private async Task Authorize(Context context, string operation, CancellationToken token)
    {
        Check(context); if (!await permissions.IsAuthorizedAsync([AiKnowledgePermissions.Read, AiKnowledgeFaqPermissions.Read, operation], context.Actor, token).ConfigureAwait(false)) throw new UnauthorizedAccessException(); Check(context);
    }
    private async Task<Result<T>> Run<T>(Guid actor, string operation, Func<Context, CancellationToken, Task<Result<T>>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("FAQ 草稿操作不能加入外部事务。");
        var context = new Context(actor, tenant.Id, AiKnowledgeScope.IsTenant(tenant));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            if (actor == Guid.Empty || context.TenantId == Guid.Empty || context.Scoped != context.TenantId.HasValue) throw new UnauthorizedAccessException();
            await Authorize(context, operation, deadline.Token).ConfigureAwait(false);
            return await action(context, deadline.Token).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException) { return Fail<T>("authorization.permission_denied", ErrorType.Forbidden); }
        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return Unavailable<T>(); }
        catch (CorruptDraftException) { return Unavailable<T>(); }
        // 来源核证也会锁读权威行；并发死锁失败关闭，不重试或把数据库细节交付客户端。
        catch (DataCommandException error) when (error.Kind == DataCommandFailureKind.Deadlock) { return Conflict<T>(); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { return Fail<T>("ai.knowledge_faqs.input_invalid", ErrorType.Validation); }
    }
    private async Task<bool> Owned(Guid knowledge, Context context, CancellationToken token)
    {
        var p = Parameters(knowledge, context); p["Id"] = knowledge;
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(Choose(context, AiKnowledgeSql.FindHost, AiKnowledgeSql.FindTenant), p, token).ConfigureAwait(false); Check(context);
        return row is { IsEnabled: true };
    }
    private async Task<AiKnowledgeFaqDraftRecord?> Read(Guid knowledge, Guid id, Context context, CancellationToken token)
    {
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(Choose(context, AiKnowledgeFaqDraftSql.ReadHost, AiKnowledgeFaqDraftSql.ReadTenant), Parameters(knowledge, context, id), token).ConfigureAwait(false); Check(context); return row;
    }
    private async Task<bool> Proven(IReadOnlyList<AiKnowledgeFaqDraftRecord> rows, Context context, string operation, CancellationToken token, bool stored = true)
    {
        var allowed = await sources.AuthorizeAsync(rows, context.Actor, operation, token, stored).ConfigureAwait(false); Check(context);
        return rows.All(row => allowed.Contains(row.Id));
    }
    internal Task<Result<AiKnowledgeFaqDraftMutation>> IndependentAsync(Guid knowledge, Guid actor, CreateAiKnowledgeFaqIndependentDraft request, CancellationToken token) =>
        Run(actor, AiKnowledgeFaqPermissions.Write, async (context, work) =>
        {
            AiKnowledgeFaqDraftPolicy.Key(request.RequestKey); var json = AiKnowledgeFaqDraftPolicy.Encode(request.Content);
            return await Create(knowledge, context, request.RequestKey, json, new(1, "independent", AiKnowledgeFaqDraftPolicy.Basis(request.Basis), []), null, work).ConfigureAwait(false);
        }, token);
    internal Task<Result<AiKnowledgeFaqDraftMutation>> DocumentsAsync(Guid knowledge, Guid actor, CreateAiKnowledgeFaqDocumentDraft request, CancellationToken token) =>
        DerivedCoreAsync(knowledge, actor, request.RequestKey, request.Content, request.Selection, false, token);
    internal Task<Result<AiKnowledgeFaqDraftMutation>> DerivedAsync(Guid knowledge, Guid actor, CreateAiKnowledgeFaqDerivedDraft request, CancellationToken token) =>
        DerivedCoreAsync(knowledge, actor, request.RequestKey, request.Content, request.Selection, true, token);
    private Task<Result<AiKnowledgeFaqDraftMutation>> DerivedCoreAsync(Guid knowledge, Guid actor, Guid requestKey, AiKnowledgeFaqContent content,
        AiKnowledgeFaqDocumentSelection selection, bool parents, CancellationToken token) =>
        Run(actor, AiKnowledgeFaqPermissions.Write, async (context, work) =>
        {
            AiKnowledgeFaqDraftPolicy.Key(requestKey); var json = AiKnowledgeFaqDraftPolicy.Encode(content);
            if (knowledge == Guid.Empty || selection is null || selection.EmbeddingModelConfigId == Guid.Empty || selection.EmbeddingModelVersion < 1 || selection.Sources is null || selection.Sources.Count is < 1 or > 8 || selection.Sources.Any(source => source is null)) return Invalid<AiKnowledgeFaqDraftMutation>();
            if (parents != selection.Sources.Any(source => source.SourceKind == "faq-revision-v1")) return Invalid<AiKnowledgeFaqDraftMutation>();
            if (!await Owned(knowledge, context, work).ConfigureAwait(false)) return Missing<AiKnowledgeFaqDraftMutation>();
            var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Choose(context, AiModelConfigSql.FindAvailableForHostChat, AiModelConfigSql.FindAvailableForTenantChat), AiSqlParameters.Create(("ModelConfigId", selection.EmbeddingModelConfigId)), work).ConfigureAwait(false); Check(context);
            if (model is null || model.Version != selection.EmbeddingModelVersion) return Unavailable<AiKnowledgeFaqDraftMutation>();
            AiKnowledgeRetrievalSnapshot snapshot;
            try
            {
                var expected = AiPersonalScheduleAnswerSources.Freeze(selection.PersonalSchedules);
                var captured = expected is null ? null : personalSchedules is null ? null
                    : await personalSchedules.CaptureAsync(actor, expected.Select(value => value.Id).ToArray(), work).ConfigureAwait(false);
                if (expected is not null && (captured is null || captured.Count != expected.Count
                    || expected.Any(value => captured.Count(source => source.Proof.Id == value.Id && source.Proof.Version == value.Version
                        && source.Proof.ProjectionHash == value.ProjectionHash) != 1))) return Unavailable<AiKnowledgeFaqDraftMutation>();
                snapshot = await retrieval.PreflightAsync(selection.Sources.Select(source => source.KnowledgeBaseId).Distinct().ToArray(), actor, model, work,
                    searchScope: new(selection.Sources.Select(source => source.DocumentId).ToArray(), selection.EmbeddingModelVersion, selection.Sources), schedules: captured).ConfigureAwait(false); Check(context);
            }
            catch (Exception error) when (error is InvalidDataException or AiKnowledgeSearchSourceChangedException or InvalidOperationException) { return Unavailable<AiKnowledgeFaqDraftMutation>(); }
            var origin = parents ? AiKnowledgeFaqGraphs.Capture(snapshot) : new AiKnowledgeFaqProvenance(1, "document_derived", null, snapshot.Sources.Select(AiKnowledgeFaqProvenanceCodec.Dependency).ToArray());
            var business = AiKnowledgeFaqBusinessSources.For(snapshot);
            if (business.Count > 0) origin = origin with { Version = 4, PersonalSchedules = business };
            return await Create(knowledge, context, requestKey, json, origin, null, work).ConfigureAwait(false);
        }, token);
    private async Task<Result<AiKnowledgeFaqDraftMutation>> Create(Guid knowledge, Context context, Guid key, string json,
        AiKnowledgeFaqProvenance origin, CopyBinding? copy, CancellationToken token)
    {
        if (knowledge == Guid.Empty || !await Owned(knowledge, context, token).ConfigureAwait(false)) return Missing<AiKnowledgeFaqDraftMutation>();
        var sourceJson = AiKnowledgeFaqProvenanceCodec.Encode(origin); var sourceHash = AiKnowledgeFaqDraftPolicy.Hash(sourceJson);
        var id = NewId(); var revision = NewId(); var contentHash = AiKnowledgeFaqDraftPolicy.ContentHash(json, sourceHash);
        var fingerprint = AiKnowledgeFaqDraftPolicy.Hash("create\n" + knowledge.ToString("D") + "\n" + contentHash + "\n" + context.Actor.ToString("D") + "\n" + (copy is null ? "" : copy.Id.ToString("D") + ":" + copy.RevisionId.ToString("D") + ":" + copy.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var candidate = new AiKnowledgeFaqDraftRecord { Id = id, KnowledgeBaseId = knowledge, TenantId = context.TenantId, OriginKey = origin.Origin, Basis = origin.Basis, SourceJson = sourceJson, SourceHash = sourceHash };
        if (!await Proven([candidate], context, AiKnowledgeFaqPermissions.Write, token, false).ConfigureAwait(false)) return Unavailable<AiKnowledgeFaqDraftMutation>();
        await Authorize(context, AiKnowledgeFaqPermissions.Write, token).ConfigureAwait(false);
        if (copy?.Transfer is not null) await Authorize(context, AiKnowledgeFaqPermissions.Import, token).ConfigureAwait(false);
        var p = Parameters(knowledge, context, id); p["RequestKey"] = key;
        if (origin.RequiresFeedbackPermission || origin.PersonalSchedules is not null)
        {
            var bound = await feedback.WriteParametersAsync(knowledge, origin, context.Actor, token).ConfigureAwait(false); Check(context);
            if (bound is null) return Unavailable<AiKnowledgeFaqDraftMutation>();
            foreach (var parameter in bound.Value.Parameters) p[parameter.Key] = parameter.Value;
        }
        var saved = await transaction.ExecuteResultAsync(async ct =>
        {
            Check(context, true);
            // 跨库导入按固定顺序锁定两个知识库根，源修订核对与目标写入之间不能出现新的头。
            foreach (var rootId in (copy is null ? new[] { knowledge } : new[] { knowledge, copy.KnowledgeBaseId }).Distinct().OrderBy(value => value.ToString("N"), StringComparer.Ordinal))
                if (await query.QuerySingleOrDefaultAsync<Guid?>(AiKnowledgeFaqDraftSql.LockFor(database.Value.Provider, context.Scoped), Parameters(rootId, context), ct).ConfigureAwait(false) is null) return Missing<Saved>();
            Check(context, true);
            if ((origin.RequiresFeedbackPermission || origin.PersonalSchedules is not null) && await query.QuerySingleOrDefaultAsync<int>(AiKnowledgeFaqFeedbackSql.PendingGraphFor(database.Value.Provider, context.Scoped), p, ct).ConfigureAwait(false) != 1)
                return Conflict<Saved>();
            if (copy?.Transfer is { } approval)
            {
                var sourceRoot = await TransferKnowledge(copy.KnowledgeBaseId, context, ct, inside: true).ConfigureAwait(false);
                var targetRoot = await TransferKnowledge(knowledge, context, ct, inside: true).ConfigureAwait(false);
                if (sourceRoot is null || targetRoot is null || sourceRoot.Version != approval.SourceVersion || targetRoot.Version != approval.TargetVersion
                    || sourceRoot.DataClassification != approval.SourceRootClassification || targetRoot.DataClassification != approval.TargetClassification
                    || !AiKnowledgeFaqTransferPolicy.CanPlace(approval.MinimumClassification, targetRoot.DataClassification, copy.KnowledgeBaseId == knowledge)) return Conflict<Saved>();
            }
            var previous = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(Choose(context, AiKnowledgeFaqDraftSql.CreateReplayHost, AiKnowledgeFaqDraftSql.CreateReplayTenant), p, ct).ConfigureAwait(false); Check(context, true);
            if (previous is not null) return previous.CreateHash == fingerprint ? Result<Saved>.Success(new(previous.Id, previous.RevisionId, true)) : Conflict<Saved>();
            if (copy is not null)
            {
                var bound = Parameters(copy.KnowledgeBaseId, context, copy.Id);
                var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(Choose(context, AiKnowledgeFaqDraftSql.ReadHost, AiKnowledgeFaqDraftSql.ReadTenant), bound, ct).ConfigureAwait(false); Check(context, true);
                if (current is null || current.HeadRevisionId != copy.RevisionId || current.HeadVersion != copy.Version || current.SourceHash != sourceHash) return Conflict<Saved>();
            }
            p["RequestHash"] = fingerprint; p["ReservedDocumentId"] = NewId(); p["RevisionId"] = revision; p["OriginKey"] = origin.Origin; p["Basis"] = origin.Basis;
            p["SourceJson"] = sourceJson; p["SourceHash"] = sourceHash; p["ContentJson"] = json; p["ContentHash"] = contentHash; p["Now"] = UtcNow(); p["RevisionNo"] = 1; p["RestoredFromRevisionId"] = null;
            if (await command.ExecuteAsync(Choose(context, AiKnowledgeFaqDraftSql.InsertHost, AiKnowledgeFaqDraftSql.InsertTenant), p, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
            Check(context, true);
            if (await command.ExecuteAsync(Choose(context, AiKnowledgeFaqDraftSql.InsertRevisionHost, AiKnowledgeFaqDraftSql.InsertRevisionTenant), p, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
            Check(context, true); return Result<Saved>.Success(new(id, revision, false));
        }, token).ConfigureAwait(false); Check(context);
        return await Mutation(knowledge, saved, context, token).ConfigureAwait(false);
    }
    internal Task<Result<AiKnowledgeFaqDraftMutation>> SaveAsync(Guid knowledge, Guid id, Guid actor, SaveAiKnowledgeFaqDraft request, CancellationToken token) =>
        Run(actor, AiKnowledgeFaqPermissions.Write, (context, work) => Save(knowledge, id, context, request.RequestKey, request.ExpectedRevisionId, request.ExpectedHeadVersion,
            AiKnowledgeFaqDraftPolicy.Encode(request.Content), null, work), token);
    internal Task<Result<AiKnowledgeFaqDraftMutation>> RestoreAsync(Guid knowledge, Guid id, Guid actor, RestoreAiKnowledgeFaqDraft request, CancellationToken token) =>
        Run(actor, AiKnowledgeFaqPermissions.Write, async (context, work) =>
        {
            AiKnowledgeFaqDraftPolicy.Key(request.RevisionId); var p = Parameters(knowledge, context, id); p["RevisionId"] = request.RevisionId;
            var old = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(Choose(context, AiKnowledgeFaqDraftSql.RevisionHost, AiKnowledgeFaqDraftSql.RevisionTenant), p, work).ConfigureAwait(false); Check(context);
            if (old is null) return Missing<AiKnowledgeFaqDraftMutation>(); _ = MapRevision(old);
            return await Save(knowledge, id, context, request.RequestKey, request.ExpectedRevisionId, request.ExpectedHeadVersion, old.ContentJson, request.RevisionId, work).ConfigureAwait(false);
        }, token);
    internal Task<Result<AiKnowledgeFaqDraftMutation>> CopyAsync(Guid knowledge, Guid id, Guid actor, CopyAiKnowledgeFaqDraft request, CancellationToken token) =>
        Run(actor, AiKnowledgeFaqPermissions.Write, async (context, work) =>
        {
            AiKnowledgeFaqDraftPolicy.Key(request.RequestKey); AiKnowledgeFaqDraftPolicy.Key(request.ExpectedRevisionId); if (request.ExpectedHeadVersion < 1) return Invalid<AiKnowledgeFaqDraftMutation>();
            var p = Parameters(knowledge, context, id); p["RevisionId"] = request.ExpectedRevisionId;
            var original = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(Choose(context, AiKnowledgeFaqDraftSql.RevisionHost, AiKnowledgeFaqDraftSql.RevisionTenant), p, work).ConfigureAwait(false); Check(context);
            if (original is null) return Missing<AiKnowledgeFaqDraftMutation>(); if (original.RevisionNo != request.ExpectedHeadVersion) return Conflict<AiKnowledgeFaqDraftMutation>();
            _ = MapRevision(original);
            if (!await Proven([original], context, AiKnowledgeFaqPermissions.Write, work).ConfigureAwait(false)) return Unavailable<AiKnowledgeFaqDraftMutation>();
            return await Create(knowledge, context, request.RequestKey, original.ContentJson, AiKnowledgeFaqProvenanceCodec.Decode(original), new(knowledge, id, request.ExpectedRevisionId, request.ExpectedHeadVersion), work).ConfigureAwait(false);
        }, token);
    private async Task<Result<AiKnowledgeFaqDraftMutation>> Save(Guid knowledge, Guid id, Context context, Guid key, Guid expected, int version, string json, Guid? restored, CancellationToken token)
    {
        AiKnowledgeFaqDraftPolicy.Key(key); AiKnowledgeFaqDraftPolicy.Key(expected); if (version < 1) return Invalid<AiKnowledgeFaqDraftMutation>();
        var current = await Read(knowledge, id, context, token).ConfigureAwait(false); if (current is null) return Missing<AiKnowledgeFaqDraftMutation>();
        if (!await Proven([current], context, AiKnowledgeFaqPermissions.Write, token).ConfigureAwait(false)) return Unavailable<AiKnowledgeFaqDraftMutation>();
        var hash = AiKnowledgeFaqDraftPolicy.ContentHash(json, current.SourceHash);
        var fingerprint = AiKnowledgeFaqDraftPolicy.Hash((restored.HasValue ? "restore" : "save") + "\n" + context.Actor.ToString("D") + "\n" + expected.ToString("D") + "\n" + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + hash + "\n" + restored?.ToString("D"));
        await Authorize(context, AiKnowledgeFaqPermissions.Write, token).ConfigureAwait(false);
        var p = Parameters(knowledge, context, id); p["RequestKey"] = key;
        var origin = AiKnowledgeFaqProvenanceCodec.Decode(current);
        if (origin.RequiresFeedbackPermission || origin.PersonalSchedules is not null)
        {
            var binding = await feedback.WriteParametersAsync(knowledge, origin, context.Actor, token).ConfigureAwait(false); Check(context);
            if (binding is null) return Unavailable<AiKnowledgeFaqDraftMutation>();
            foreach (var pair in binding.Value.Parameters) p[pair.Key] = pair.Value;
        }
        var saved = await transaction.ExecuteResultAsync(async ct =>
        {
            Check(context, true);
            if (await query.QuerySingleOrDefaultAsync<Guid?>(AiKnowledgeFaqDraftSql.LockFor(database.Value.Provider, context.Scoped), p, ct).ConfigureAwait(false) is null) return Missing<Saved>();
            if ((origin.RequiresFeedbackPermission || origin.PersonalSchedules is not null)
                && await query.QuerySingleOrDefaultAsync<int>(AiKnowledgeFaqFeedbackSql.PendingGraphFor(database.Value.Provider, context.Scoped), p, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
            var replay = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(Choose(context, AiKnowledgeFaqDraftSql.RevisionReplayHost, AiKnowledgeFaqDraftSql.RevisionReplayTenant), p, ct).ConfigureAwait(false); Check(context, true);
            if (replay is not null) return replay.RequestHash == fingerprint ? Result<Saved>.Success(new(id, replay.RevisionId, true)) : Conflict<Saved>();
            var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(Choose(context, AiKnowledgeFaqDraftSql.ReadHost, AiKnowledgeFaqDraftSql.ReadTenant), p, ct).ConfigureAwait(false); Check(context, true);
            if (root is null || root.HeadRevisionId != expected || root.HeadVersion != version || root.SourceHash != current.SourceHash || version == int.MaxValue) return Conflict<Saved>();
            var revision = NewId(); p["RevisionId"] = revision; p["RevisionNo"] = version + 1; p["RequestHash"] = fingerprint; p["ContentJson"] = json; p["ContentHash"] = hash;
            p["Now"] = UtcNow(); p["RestoredFromRevisionId"] = restored; p["ExpectedRevisionId"] = expected; p["ExpectedHeadVersion"] = version;
            if (await command.ExecuteAsync(Choose(context, AiKnowledgeFaqDraftSql.InsertRevisionHost, AiKnowledgeFaqDraftSql.InsertRevisionTenant), p, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
            Check(context, true);
            if (await command.ExecuteAsync(Choose(context, AiKnowledgeFaqDraftSql.AdvanceHost, AiKnowledgeFaqDraftSql.AdvanceTenant), p, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
            Check(context, true); return Result<Saved>.Success(new(id, revision, false));
        }, token).ConfigureAwait(false); Check(context);
        return await Mutation(knowledge, saved, context, token).ConfigureAwait(false);
    }
    private async Task<Result<AiKnowledgeFaqDraftMutation>> Mutation(Guid knowledge, Result<Saved> saved, Context context, CancellationToken token)
    {
        if (!saved.IsSuccess) return Result<AiKnowledgeFaqDraftMutation>.Failure(saved.Error!);
        var current = await Read(knowledge, saved.Value!.Id, context, token).ConfigureAwait(false);
        if (current is null || !await Proven([current], context, AiKnowledgeFaqPermissions.Write, token).ConfigureAwait(false)) return Unavailable<AiKnowledgeFaqDraftMutation>();
        return Result<AiKnowledgeFaqDraftMutation>.Success(new(Map(current), saved.Value.RevisionId, saved.Value.Replayed));
    }
    internal Task<Result<AiKnowledgeFaqDraftWorkspace>> GetAsync(Guid knowledge, Guid id, Guid actor, CancellationToken token) =>
        Run(actor, AiKnowledgeFaqPermissions.Read, async (context, work) =>
        {
            var current = await Read(knowledge, id, context, work).ConfigureAwait(false); if (current is null) return Missing<AiKnowledgeFaqDraftWorkspace>();
            return await Proven([current], context, AiKnowledgeFaqPermissions.Read, work).ConfigureAwait(false)
                ? Result<AiKnowledgeFaqDraftWorkspace>.Success(Map(current)) : Unavailable<AiKnowledgeFaqDraftWorkspace>();
        }, token);
    internal Task<Result<AiKnowledgeFaqDraftPage>> ListAsync(Guid knowledge, Guid? after, Guid actor, CancellationToken token) =>
        Run(actor, AiKnowledgeFaqPermissions.Read, async (context, work) =>
        {
            if (after == Guid.Empty) return Invalid<AiKnowledgeFaqDraftPage>(); if (!await Owned(knowledge, context, work).ConfigureAwait(false)) return Missing<AiKnowledgeFaqDraftPage>();
            var p = Parameters(knowledge, context); p["AfterId"] = after;
            var rows = await query.QueryAsync<AiKnowledgeFaqDraftRecord>(AiKnowledgeFaqDraftSql.PageFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false); Check(context);
            if (rows.Count > 26) throw new InvalidDataException(); var window = rows.Take(25).ToArray();
            var allowed = await sources.AuthorizeAsync(window, actor, AiKnowledgeFaqPermissions.Read, work).ConfigureAwait(false); Check(context);
            return Result<AiKnowledgeFaqDraftPage>.Success(new(window.Where(row => allowed.Contains(row.Id)).Select(Map).ToArray(), rows.Count > 25 ? window[^1].Id : null));
        }, token);
    internal Task<Result<AiKnowledgeFaqDraftRevisionPage>> HistoryAsync(Guid knowledge, Guid id, Guid? after, Guid actor, CancellationToken token) =>
        Run(actor, AiKnowledgeFaqPermissions.Read, async (context, work) =>
        {
            if (after == Guid.Empty) return Invalid<AiKnowledgeFaqDraftRevisionPage>(); var current = await Read(knowledge, id, context, work).ConfigureAwait(false);
            if (current is null) return Missing<AiKnowledgeFaqDraftRevisionPage>();
            var p = Parameters(knowledge, context, id); p["AfterId"] = after;
            var rows = await query.QueryAsync<AiKnowledgeFaqDraftRecord>(AiKnowledgeFaqDraftSql.HistoryFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false); Check(context);
            if (rows.Count > 26 || rows.Any(row => row.SourceHash != current.SourceHash)) return Unavailable<AiKnowledgeFaqDraftRevisionPage>();
            if (!await Proven([current], context, AiKnowledgeFaqPermissions.Read, work).ConfigureAwait(false)) return Unavailable<AiKnowledgeFaqDraftRevisionPage>();
            var window = rows.Take(25).ToArray(); return Result<AiKnowledgeFaqDraftRevisionPage>.Success(new(window.Select(MapRevision).ToArray(), rows.Count > 25 ? window[^1].RevisionId : null));
        }, token);
    private static AiKnowledgeFaqDraftWorkspace Map(AiKnowledgeFaqDraftRecord row)
    {
        var origin = AiKnowledgeFaqProvenanceCodec.Decode(row);
        return new(row.Id, row.KnowledgeBaseId, row.ReservedDocumentId, "draft", origin.Origin, origin.Basis, row.SourceHash,
            AiKnowledgeFaqGraphs.Public(origin), row.HeadVersion, MapRevision(row)) { Graph = AiKnowledgeFaqGraphs.PublicGraph(origin.Graph), Feedback = origin.Feedback?.Public(), PersonalSchedules = AiKnowledgeFaqBusinessSources.Public(origin) };
    }
    private static AiKnowledgeFaqDraftRevision MapRevision(AiKnowledgeFaqDraftRecord row)
    {
        try
        {
            if (AiKnowledgeFaqDraftPolicy.ContentHash(row.ContentJson, row.SourceHash) != row.ContentHash || row.RevisionNo < 1) throw new InvalidDataException();
            return new(row.RevisionId, row.RevisionNo, AiKnowledgeFaqDraftPolicy.Decode(row.ContentJson), row.ContentHash, row.RevisionCreatedAtUtc, row.RestoredFromRevisionId);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { throw new CorruptDraftException(); }
    }
    private Guid NewId() { var id = ids.NewId(); AiKnowledgeFaqDraftPolicy.Key(id); return id; }
    private DateTimeOffset UtcNow() { var now = clock.UtcNow; return new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero); }
    private static Result<T> Fail<T>(string code, ErrorType type) => Result<T>.Failure(new Error(code, "FAQ draft operation could not be completed.", type));
    private static Result<T> Invalid<T>() => Fail<T>("ai.knowledge_faqs.input_invalid", ErrorType.Validation);
    private static Result<T> Missing<T>() => Fail<T>("ai.knowledge_faqs.not_found", ErrorType.NotFound);
    private static Result<T> Conflict<T>() => Fail<T>("ai.knowledge_faqs.version_conflict", ErrorType.Conflict);
    private static Result<T> Unavailable<T>() => Fail<T>("ai.knowledge_faqs.source_unavailable", ErrorType.Conflict);
}
