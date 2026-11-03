using System.Text.Json;
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
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeEvaluationSets;

/// <summary>评估集只追加修订和审核；来源授权在事务外，最终资格与写入在本地事务中复核，无模型调用。</summary>
internal sealed class AiKnowledgeEvaluationService(IQueryExecutor query, ICommandExecutor commands, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, ICurrentSessionAuthorization authorization,
    AiKnowledgeReadAccess access, AiKnowledgeEvaluationSourceAuthority sources, IOptions<DatabaseOptions> database,
    IClock clock, IIdGenerator ids, AiKnowledgeEvaluationCandidateToken candidates)
{
    private sealed record Context(Guid Actor, Guid? TenantId, bool Scoped, Guid SessionId, string Permission)
    {
        internal IReadOnlyList<string> ExtraPermissions { get; init; } = [];
    }
    private sealed record Saved(Guid SetId, Guid RevisionId, bool Replayed);
    private sealed class CorruptEvaluationException : Exception { }

    /// <summary>仅供实际运行的最后联合门禁消费；参数是本次重新核证的证明，不能缓存为长期授权。</summary>
    internal sealed record RunProof(AiKnowledgeEvaluationWorkspace Workspace, AiKnowledgeEvaluationRow Row,
        Dictionary<string, object?> Parameters, IReadOnlyList<string> RequiredPermissions);

    internal async Task<RunProof?> ProveRunAsync(Guid knowledge, Guid set, Guid actor, Guid session, string permission, CancellationToken token)
    {
        var context = new Context(actor, tenant.Id, AiKnowledgeScope.IsTenant(tenant), session, permission);
        Check(context);
        var row = await ReadAsync(knowledge, set, context, token).ConfigureAwait(false);
        if (row is null) return null;
        var proof = await ProveAsync(row, context, token).ConfigureAwait(false);
        return proof is null ? null : new(Map(row, actor), row, proof, AiKnowledgeEvaluationSourceAuthority.RequiredFor(Footprint(row)));
    }

    internal Task<Result<AiKnowledgeEvaluationPreparedFeedback>> PrepareFeedbackAsync(Guid knowledge, Guid actor,
        PrepareAiKnowledgeEvaluationFeedback request, CancellationToken token) =>
        Run(actor, AiKnowledgeEvaluationPermissions.Feedback, async (context, work) =>
        {
            var p = await ParametersAsync(knowledge, null, context, work).ConfigureAwait(false); p["Id"] = knowledge;
            var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false);
            Check(context);
            // 先证明目标所有权和私人分类，不能为无效目标提前读取生产答案来计算摘要。
            if (root is not { IsEnabled: true, DataClassification: "restricted" } || root.OwnerUserId != actor) return Missing<AiKnowledgeEvaluationPreparedFeedback>();
            var captured = await sources.CaptureOriginalAsync(request.CandidateToken, actor, context.SessionId, work).ConfigureAwait(false); Check(context);
            if (captured is null) return Unavailable<AiKnowledgeEvaluationPreparedFeedback>();
            var footprint = AiKnowledgeEvaluationPolicy.Extend(null, [captured.Value.Source], actor);
            var parentJson = await sources.ProveAsync(footprint, actor, work).ConfigureAwait(false); Check(context);
            if (parentJson is null) return Unavailable<AiKnowledgeEvaluationPreparedFeedback>();
            await AuthorizeAsync(context, context.Permission, work, footprint).ConfigureAwait(false);
            // 目标库在来源核证等待之后必须仍是同一版本与分类；实际保存另有完整联合 SQL 门禁。
            var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false);
            Check(context);
            if (current is null || current.Version != root.Version || current.OwnerUserId != actor || current.DataClassification != "restricted" || !current.IsEnabled) return Unavailable<AiKnowledgeEvaluationPreparedFeedback>();
            if (captured.Value.ExpiresAtUtc <= clock.UtcNow) return Unavailable<AiKnowledgeEvaluationPreparedFeedback>();
            return Result<AiKnowledgeEvaluationPreparedFeedback>.Success(candidates.Protect(new(1, actor, context.TenantId, context.SessionId,
                knowledge, root.Version, captured.Value.Source, captured.Value.Sample, captured.Value.ExpiresAtUtc)));
        }, token);

    internal Task<Result<AiKnowledgeEvaluationPage>> ListAsync(Guid knowledge, Guid? after, Guid actor, CancellationToken token) =>
        Run(actor, AiKnowledgeEvaluationPermissions.Read, async (context, work) =>
        {
            if (knowledge == Guid.Empty || after == Guid.Empty) return Invalid<AiKnowledgeEvaluationPage>();
            var p = await ParametersAsync(knowledge, null, context, work).ConfigureAwait(false); p["AfterId"] = after;
            var rows = await query.QueryAsync<AiKnowledgeEvaluationRow>(AiKnowledgeEvaluationSql.PageFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false);
            Check(context); if (rows.Count > 9) return Unavailable<AiKnowledgeEvaluationPage>();
            var items = new List<AiKnowledgeEvaluationRow>();
            var parents = new List<AiKnowledgeFaqParentAuthorization>();
            foreach (var row in rows.Take(8))
            {
                var proven = await ProveAsync(row, context, work).ConfigureAwait(false);
                if (proven is not null)
                {
                    items.Add(row);
                    parents.AddRange(JsonSerializer.Deserialize((string)proven["FaqParentBindingsJson"]!, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqParentAuthorizationArray)!);
                }
            }
            p = await ParametersAsync(knowledge, null, context, work).ConfigureAwait(false); p["Id"] = knowledge;
            var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false);
            if (root is not { IsEnabled: true }) return Missing<AiKnowledgeEvaluationPage>();
            p["KnowledgeVersion"] = root.Version; p["Classification"] = root.DataClassification; p["FaqFeedbackReadAllowed"] = 1;
            p["FaqParentBindingsJson"] = JsonSerializer.Serialize(parents.ToArray(), AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqParentAuthorizationArray);
            p["SetBindingsJson"] = JsonSerializer.Serialize(items.Select(row => new AiKnowledgeEvaluationBinding(row.Id, row.Version,
                row.HeadRevisionId, row.FootprintHash, row.RevisionId, row.ContentHash, row.SourceHash, row.FootprintJson)).ToArray(),
                AiKnowledgeEvaluationJsonContext.Default.AiKnowledgeEvaluationBindingArray);
            var extra = items.SelectMany(row => AiKnowledgeEvaluationSourceAuthority.RequiredFor(Footprint(row))).Distinct(StringComparer.Ordinal).ToArray();
            context = context with { ExtraPermissions = extra };
            await AuthorizeAsync(context, context.Permission, work).ConfigureAwait(false);
            // 所有选中根、来源及父模型在同一条最终查询中复核，避免逐项等待释放前一项旧资格。
            var visible = await query.QueryAsync<Guid>(AiKnowledgeEvaluationSql.CurrentPageFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false);
            Check(context);
            return Result<AiKnowledgeEvaluationPage>.Success(new(items.Where(row => visible.Contains(row.Id)).Select(row => Map(row, actor)).ToArray(),
                rows.Count > 8 ? rows.ElementAt(7).Id : null));
        }, token);

    internal Task<Result<AiKnowledgeEvaluationWorkspace>> GetAsync(Guid knowledge, Guid set, Guid? revision, Guid actor, CancellationToken token) =>
        Run(actor, AiKnowledgeEvaluationPermissions.Read, async (context, work) =>
        {
            if (revision == Guid.Empty) return Invalid<AiKnowledgeEvaluationWorkspace>();
            var p = await ParametersAsync(knowledge, set, context, work).ConfigureAwait(false); p["RevisionId"] = revision;
            var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationRow>(revision.HasValue
                ? AiKnowledgeEvaluationSql.RevisionFor(database.Value.Provider, context.Scoped)
                : AiKnowledgeEvaluationSql.ReadFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false);
            if (row is null) return Missing<AiKnowledgeEvaluationWorkspace>();
            return await ProveAsync(row, context, work).ConfigureAwait(false) is not null
                ? Result<AiKnowledgeEvaluationWorkspace>.Success(Map(row, actor)) : Unavailable<AiKnowledgeEvaluationWorkspace>();
        }, token);

    internal Task<Result<AiKnowledgeEvaluationMutation>> SaveAsync(Guid knowledge, Guid? set, Guid actor,
        SaveAiKnowledgeEvaluationSet request, CancellationToken token) =>
        Run(actor, set.HasValue ? AiKnowledgeEvaluationPermissions.Append : AiKnowledgeEvaluationPermissions.Create, async (context, work) =>
        {
            if (request.RequestKey == Guid.Empty || !request.ConfirmManualRedaction || request.Cases is null
                || request.Cases.Count is < 1 or > AiKnowledgeEvaluationPolicy.MaximumCases
                || request.Cases.Any(value => value is null) || set == Guid.Empty
                || (set.HasValue ? request.ExpectedRevisionId is null || request.ExpectedRevisionId == Guid.Empty || request.ExpectedVersion < 1
                    : request.ExpectedRevisionId is not null || request.ExpectedVersion != 0)) return Invalid<AiKnowledgeEvaluationMutation>();
            if (request.Freeze)
            {
                context = context with { ExtraPermissions = [AiKnowledgeEvaluationPermissions.Freeze] };
                await AuthorizeAsync(context, context.Permission, work).ConfigureAwait(false);
            }
            var fingerprint = Fingerprint(context, knowledge, set, "save", JsonSerializer.Serialize(request, AiJsonSerializerContext.Default.SaveAiKnowledgeEvaluationSet));
            var p = await ParametersAsync(knowledge, set, context, work).ConfigureAwait(false); p["RequestKey"] = request.RequestKey;
            var replay = await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationRow>(set.HasValue
                ? AiKnowledgeEvaluationSql.ReplayFor(database.Value.Provider, context.Scoped)
                : AiKnowledgeEvaluationSql.CreateReplayFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false);
            if (replay is not null) return (set.HasValue ? replay.RequestHash : replay.CreateHash) == fingerprint
                ? await RespondAsync(new(replay.Id, replay.RevisionId, true), knowledge, context, work).ConfigureAwait(false) : Conflict<AiKnowledgeEvaluationMutation>();
            var current = set.HasValue ? await ReadAsync(knowledge, set.Value, context, work).ConfigureAwait(false) : null;
            if (set.HasValue && current is null) return Missing<AiKnowledgeEvaluationMutation>();
            if (current is not null && (current.HeadRevisionId != request.ExpectedRevisionId || current.Version != request.ExpectedVersion
                || await ProveAsync(current, context, work).ConfigureAwait(false) is null)) return Conflict<AiKnowledgeEvaluationMutation>();
            var old = current is null ? null : Body(current);
            var footprint = current is null ? null : Footprint(current);
            var cases = new List<AiKnowledgeEvaluationCase>(); var added = new List<string>();
            DateTimeOffset? candidateExpires = null; int? candidateKnowledgeVersion = null;
            foreach (var input in request.Cases)
            {
                if (input.Id is { } id)
                {
                    var original = old?.Cases.SingleOrDefault(value => value.Id == id);
                    if (original is null || input.FeedbackCandidateToken is not null || input.IndependentBasis != original.IndependentBasis) return Invalid<AiKnowledgeEvaluationMutation>();
                    cases.Add(original with { Sample = input.Sample });
                }
                else if (input.FeedbackCandidateToken is { } candidate)
                {
                    if (input.IndependentBasis is not null) return Invalid<AiKnowledgeEvaluationMutation>();
                    var captured = candidates.Read(candidate, actor, context.TenantId, context.SessionId, knowledge);
                    if (captured is null || captured.Sample != input.Sample
                        || candidateKnowledgeVersion is { } version && version != captured.KnowledgeVersion) return Unavailable<AiKnowledgeEvaluationMutation>();
                    context = context with { ExtraPermissions = [.. context.ExtraPermissions, AiKnowledgeEvaluationPermissions.Feedback] };
                    candidateKnowledgeVersion = captured.KnowledgeVersion; added.Add(captured.SourceJson);
                    candidateExpires = candidateExpires is null || captured.ExpiresAtUtc < candidateExpires ? captured.ExpiresAtUtc : candidateExpires;
                    cases.Add(new(NewId(), input.Sample, null, AiKnowledgeEvaluationPolicy.Hash(captured.SourceJson)));
                }
                else cases.Add(new(NewId(), input.Sample, input.IndependentBasis, null));
            }
            var next = AiKnowledgeEvaluationPolicy.Extend(footprint, added, actor);
            var body = new AiKnowledgeEvaluationBody(1, request.Name, request.Freeze ? "frozen" : "draft", cases);
            return await PersistAsync(knowledge, current, body, next, request.RequestKey, fingerprint, null, context, work, candidateExpires,
                candidateKnowledgeVersion: candidateKnowledgeVersion).ConfigureAwait(false);
        }, token);

    internal Task<Result<AiKnowledgeEvaluationMutation>> RestoreAsync(Guid knowledge, Guid set, Guid actor,
        RestoreAiKnowledgeEvaluationSet request, CancellationToken token) =>
        Run(actor, AiKnowledgeEvaluationPermissions.Restore, async (context, work) =>
        {
            if (request.RequestKey == Guid.Empty || request.RevisionId == Guid.Empty) return Invalid<AiKnowledgeEvaluationMutation>();
            var current = await ReadAsync(knowledge, set, context, work).ConfigureAwait(false);
            if (current is null) return Missing<AiKnowledgeEvaluationMutation>();
            var fingerprint = Fingerprint(context, knowledge, set, "restore", JsonSerializer.Serialize(request, AiJsonSerializerContext.Default.RestoreAiKnowledgeEvaluationSet));
            var replay = await ReplayAsync(knowledge, set, request.RequestKey, fingerprint, context, work).ConfigureAwait(false);
            if (replay is not null) return replay;
            if (current.HeadRevisionId != request.ExpectedRevisionId || current.Version != request.ExpectedVersion) return Conflict<AiKnowledgeEvaluationMutation>();
            var p = await ParametersAsync(knowledge, set, context, work).ConfigureAwait(false); p["RevisionId"] = request.RevisionId;
            var old = await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationRow>(AiKnowledgeEvaluationSql.RevisionFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false);
            if (old is null || await ProveAsync(old, context, work).ConfigureAwait(false) is null) return Unavailable<AiKnowledgeEvaluationMutation>();
            return await PersistAsync(knowledge, current, Body(old) with { State = "draft" },
                AiKnowledgeEvaluationPolicy.Extend(Footprint(current), [], actor), request.RequestKey, fingerprint, request.RevisionId, context, work).ConfigureAwait(false);
        }, token);

    internal Task<Result<AiKnowledgeEvaluationMutation>> CopyAsync(Guid knowledge, Guid set, Guid actor,
        CopyAiKnowledgeEvaluationSet request, CancellationToken token) =>
        Run(actor, AiKnowledgeEvaluationPermissions.Copy, async (context, work) =>
        {
            if (request.RequestKey == Guid.Empty) return Invalid<AiKnowledgeEvaluationMutation>();
            var current = await ReadAsync(knowledge, set, context, work).ConfigureAwait(false);
            if (current is null) return Missing<AiKnowledgeEvaluationMutation>();
            var fingerprint = Fingerprint(context, knowledge, set, "copy", JsonSerializer.Serialize(request, AiJsonSerializerContext.Default.CopyAiKnowledgeEvaluationSet));
            var p = await ParametersAsync(knowledge, null, context, work).ConfigureAwait(false); p["RequestKey"] = request.RequestKey;
            var replay = await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationRow>(AiKnowledgeEvaluationSql.CreateReplayFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false);
            if (replay is not null) return replay.CreateHash == fingerprint ? await RespondAsync(new(replay.Id, replay.RevisionId, true), knowledge, context, work).ConfigureAwait(false) : Conflict<AiKnowledgeEvaluationMutation>();
            if (current.HeadRevisionId != request.ExpectedRevisionId || current.Version != request.ExpectedVersion
                || await ProveAsync(current, context, work).ConfigureAwait(false) is null) return Conflict<AiKnowledgeEvaluationMutation>();
            var body = Body(current) with { State = "draft", Cases = Body(current).Cases.Select(item => item with { Id = NewId() }).ToArray() };
            return await PersistAsync(knowledge, null, body, AiKnowledgeEvaluationPolicy.Extend(Footprint(current), [], actor),
                request.RequestKey, fingerprint, null, context, work, copied: current).ConfigureAwait(false);
        }, token);

    internal Task<Result<AiKnowledgeEvaluationMutation>> ReviewAsync(Guid knowledge, Guid set, Guid actor,
        ReviewAiKnowledgeEvaluationSet request, CancellationToken token) =>
        Run(actor, request.Decision == "withdrawn" ? AiKnowledgeEvaluationPermissions.Withdraw : AiKnowledgeEvaluationPermissions.Approve, async (context, work) =>
        {
            if (request.RequestKey == Guid.Empty || request.Decision is not ("approved" or "withdrawn")) return Invalid<AiKnowledgeEvaluationMutation>();
            var current = await ReadAsync(knowledge, set, context, work).ConfigureAwait(false);
            if (current is null) return Missing<AiKnowledgeEvaluationMutation>();
            var fingerprint = Fingerprint(context, knowledge, set, "review", JsonSerializer.Serialize(request, AiJsonSerializerContext.Default.ReviewAiKnowledgeEvaluationSet));
            var p = await ParametersAsync(knowledge, set, context, work).ConfigureAwait(false); p["RequestKey"] = request.RequestKey;
            var replay = await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationApprovalRow>(AiKnowledgeEvaluationSql.ReviewReplayFor(database.Value.Provider, context.Scoped), p, work).ConfigureAwait(false);
            if (replay is not null) return replay.RequestHash == fingerprint ? await RespondAsync(new(set, replay.RevisionId, true), knowledge, context, work).ConfigureAwait(false) : Conflict<AiKnowledgeEvaluationMutation>();
            if (current.HeadRevisionId != request.ExpectedRevisionId || current.Version != request.ExpectedVersion || current.Version == int.MaxValue
                || (request.Decision == "approved" ? !request.ConfirmIndependentRedactionReview || !AiKnowledgeEvaluationPolicy.CanApprove(Body(current), Footprint(current), actor)
                    : current.ApprovalState != "approved")) return Conflict<AiKnowledgeEvaluationMutation>();
            var proven = await ProveAsync(current, context, work).ConfigureAwait(false);
            if (proven is null) return Unavailable<AiKnowledgeEvaluationMutation>();
            proven["RequestKey"] = request.RequestKey; proven["RequestHash"] = fingerprint; proven["Decision"] = request.Decision;
            proven["ApprovalId"] = NewId(); proven["Now"] = Now();
            using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(work); writeDeadline.CancelAfter(TimeSpan.FromSeconds(25));
            var saved = await transaction.ExecuteResultAsync(async ct =>
            {
                Check(context, true);
                if (await query.QuerySingleOrDefaultAsync<Guid?>(AiKnowledgeEvaluationSql.LockFor(database.Value.Provider, context.Scoped), proven, ct).ConfigureAwait(false) is null) return Missing<Saved>();
                var repeated = await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationApprovalRow>(AiKnowledgeEvaluationSql.ReviewReplayFor(database.Value.Provider, context.Scoped), proven, ct).ConfigureAwait(false);
                if (repeated is not null) return repeated.RequestHash == fingerprint ? Result<Saved>.Success(new(set, repeated.RevisionId, true)) : Conflict<Saved>();
                if (await query.QuerySingleOrDefaultAsync<int>(AiKnowledgeEvaluationSql.CurrentFor(database.Value.Provider, context.Scoped), proven, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
                if (await commands.ExecuteAsync(AiKnowledgeEvaluationSql.ReviewFor(database.Value.Provider, context.Scoped), proven, ct).ConfigureAwait(false) != 1
                    || await commands.ExecuteAsync(AiKnowledgeEvaluationSql.ApproveFor(database.Value.Provider, context.Scoped), proven, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
                proven["ExpectedVersion"] = current.Version + 1;
                if (await query.QuerySingleOrDefaultAsync<int>(AiKnowledgeEvaluationSql.CurrentFor(database.Value.Provider, context.Scoped), proven, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
                Check(context, true); return Result<Saved>.Success(new(set, current.RevisionId, false));
            }, writeDeadline.Token).ConfigureAwait(false);
            return saved.IsSuccess ? await RespondAsync(saved.Value!, knowledge, context, work).ConfigureAwait(false) : Result<AiKnowledgeEvaluationMutation>.Failure(saved.Error!);
        }, token);

    private async Task<Result<AiKnowledgeEvaluationMutation>> PersistAsync(Guid knowledge, AiKnowledgeEvaluationRow? current,
        AiKnowledgeEvaluationBody body, AiKnowledgeEvaluationFootprint footprint, Guid key, string fingerprint, Guid? restored,
        Context context, CancellationToken token, DateTimeOffset? expires = null, AiKnowledgeEvaluationRow? copied = null, int? candidateKnowledgeVersion = null)
    {
        AiKnowledgeEvaluationPolicy.Match(body, footprint);
        var json = AiKnowledgeEvaluationPolicy.Encode(body); var source = AiKnowledgeEvaluationPolicy.Encode(footprint);
        var sourceHash = AiKnowledgeEvaluationPolicy.Hash(source); var contentHash = AiKnowledgeEvaluationPolicy.ContentHash(json, sourceHash);
        if (current is not null && (current.Version == int.MaxValue || current.HeadRevisionNo == int.MaxValue)) return Conflict<AiKnowledgeEvaluationMutation>();
        var parents = await sources.ProveAsync(footprint, context.Actor, token).ConfigureAwait(false); Check(context);
        if (parents is null) return Unavailable<AiKnowledgeEvaluationMutation>();
        await AuthorizeAsync(context, context.Permission, token, footprint).ConfigureAwait(false);
        var p = await ParametersAsync(knowledge, current?.Id, context, token).ConfigureAwait(false);
        p["Id"] = knowledge;
        var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, context.Scoped), p, token).ConfigureAwait(false);
        Check(context);
        if (root is not { IsEnabled: true } || root.OwnerUserId != context.Actor || footprint.Sources.Count > 0 && root.DataClassification != "restricted") return Missing<AiKnowledgeEvaluationMutation>();
        if (candidateKnowledgeVersion is { } preparedVersion && root.Version != preparedVersion) return Conflict<AiKnowledgeEvaluationMutation>();
        p["KnowledgeVersion"] = root.Version; p["Classification"] = root.DataClassification;
        p["FootprintJson"] = source; p["FootprintHash"] = sourceHash; p["FaqParentBindingsJson"] = parents; p["FaqFeedbackReadAllowed"] = 1;
        p["Actor"] = context.Actor; p["RequestKey"] = key; p["RequestHash"] = fingerprint;
        p["SetId"] = current?.Id ?? NewId(); p["RevisionId"] = NewId(); p["RevisionNo"] = current is null ? 1 : current.HeadRevisionNo + 1;
        p["ContentJson"] = json; p["ContentHash"] = contentHash; p["Now"] = Now(); p["RestoredFromRevisionId"] = restored;
        var qualification = current is null ? null : await ProveAsync(current, context, token).ConfigureAwait(false);
        var copyProof = copied is null ? null : await ProveAsync(copied, context, token).ConfigureAwait(false);
        if (current is not null && qualification is null || copied is not null && copyProof is null) return Unavailable<AiKnowledgeEvaluationMutation>();
        await AuthorizeAsync(context, context.Permission, token, footprint).ConfigureAwait(false);
        if (expires <= clock.UtcNow) return Unavailable<AiKnowledgeEvaluationMutation>();
        using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(token); writeDeadline.CancelAfter(TimeSpan.FromSeconds(25));
        var saved = await transaction.ExecuteResultAsync(async ct =>
        {
            Check(context, true);
            if (await query.QuerySingleOrDefaultAsync<Guid?>(AiKnowledgeEvaluationSql.LockFor(database.Value.Provider, context.Scoped), p, ct).ConfigureAwait(false) is null) return Missing<Saved>();
            var replay = await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationRow>(current is null
                ? AiKnowledgeEvaluationSql.CreateReplayFor(database.Value.Provider, context.Scoped)
                : AiKnowledgeEvaluationSql.ReplayFor(database.Value.Provider, context.Scoped), p, ct).ConfigureAwait(false);
            if (replay is not null) return (current is null ? replay.CreateHash : replay.RequestHash) == fingerprint ? Result<Saved>.Success(new(replay.Id, replay.RevisionId, true)) : Conflict<Saved>();
            if (qualification is not null && await query.QuerySingleOrDefaultAsync<int>(AiKnowledgeEvaluationSql.CurrentFor(database.Value.Provider, context.Scoped), qualification, ct).ConfigureAwait(false) != 1
                || copyProof is not null && await query.QuerySingleOrDefaultAsync<int>(AiKnowledgeEvaluationSql.CurrentFor(database.Value.Provider, context.Scoped), copyProof, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
            if (current is null)
            {
                if (await commands.ExecuteAsync(AiKnowledgeEvaluationSql.InsertFor(database.Value.Provider, context.Scoped), p, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
            }
            else
            {
                p["ExpectedVersion"] = current.Version; p["ExpectedRevisionId"] = current.HeadRevisionId; p["ExpectedFootprintHash"] = current.FootprintHash;
                if (await commands.ExecuteAsync(AiKnowledgeEvaluationSql.AdvanceFor(database.Value.Provider, context.Scoped), p, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
            }
            if (await commands.ExecuteAsync(AiKnowledgeEvaluationSql.AppendFor(database.Value.Provider, context.Scoped), p, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
            // 写入后失败必须令整个本地事务回滚，最后核对新头及新增反馈，不能沿用旧头的来源参数。
            p["ExpectedVersion"] = current is null ? 1 : current.Version + 1;
            p["ExpectedRevisionId"] = p["RevisionId"]; p["ExpectedFootprintHash"] = sourceHash; p["SourceHash"] = sourceHash;
            if (await query.QuerySingleOrDefaultAsync<int>(AiKnowledgeEvaluationSql.CurrentFor(database.Value.Provider, context.Scoped), p, ct).ConfigureAwait(false) != 1) return Conflict<Saved>();
            Check(context, true); return Result<Saved>.Success(new((Guid)p["SetId"]!, (Guid)p["RevisionId"]!, false));
        }, writeDeadline.Token).ConfigureAwait(false);
        return saved.IsSuccess ? await RespondAsync(saved.Value!, knowledge, context, token).ConfigureAwait(false) : Result<AiKnowledgeEvaluationMutation>.Failure(saved.Error!);
    }

    private async Task<Dictionary<string, object?>?> ProveAsync(AiKnowledgeEvaluationRow row, Context context, CancellationToken token)
    {
        Check(context); _ = Body(row); var footprint = Footprint(row);
        var parentJson = await sources.ProveAsync(footprint, context.Actor, token).ConfigureAwait(false); Check(context);
        if (parentJson is null) return null;
        await AuthorizeAsync(context, context.Permission, token, footprint).ConfigureAwait(false);
        var p = await ParametersAsync(row.KnowledgeBaseId, row.Id, context, token).ConfigureAwait(false); p["Id"] = row.KnowledgeBaseId;
        var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, context.Scoped), p, token).ConfigureAwait(false);
        Check(context);
        if (root is not { IsEnabled: true } || footprint.Sources.Count > 0 && root.DataClassification != "restricted") return null;
        p["Actor"] = context.Actor; p["ExpectedVersion"] = row.Version; p["ExpectedRevisionId"] = row.HeadRevisionId; p["ExpectedFootprintHash"] = row.FootprintHash;
        p["RevisionId"] = row.RevisionId; p["ContentHash"] = row.ContentHash; p["SourceHash"] = row.SourceHash;
        p["KnowledgeVersion"] = root.Version; p["Classification"] = root.DataClassification;
        p["FootprintJson"] = row.FootprintJson; p["FaqParentBindingsJson"] = parentJson; p["FaqFeedbackReadAllowed"] = 1;
        await AuthorizeAsync(context, context.Permission, token, footprint).ConfigureAwait(false);
        var valid = await query.QuerySingleOrDefaultAsync<int>(AiKnowledgeEvaluationSql.CurrentFor(database.Value.Provider, context.Scoped), p, token).ConfigureAwait(false);
        Check(context); return valid == 1 ? p : null;
    }

    private async Task<Result<AiKnowledgeEvaluationMutation>?> ReplayAsync(Guid knowledge, Guid set, Guid key, string hash, Context context, CancellationToken token)
    {
        var p = await ParametersAsync(knowledge, set, context, token).ConfigureAwait(false); p["RequestKey"] = key;
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationRow>(AiKnowledgeEvaluationSql.ReplayFor(database.Value.Provider, context.Scoped), p, token).ConfigureAwait(false);
        return row is null ? null : row.RequestHash == hash ? await RespondAsync(new(set, row.RevisionId, true), knowledge, context, token).ConfigureAwait(false) : Conflict<AiKnowledgeEvaluationMutation>();
    }
    private async Task<Result<AiKnowledgeEvaluationMutation>> RespondAsync(Saved saved, Guid knowledge, Context context, CancellationToken token)
    {
        var row = await ReadAsync(knowledge, saved.SetId, context, token).ConfigureAwait(false);
        // 保存已提交，目录停用等变化可能使后续读取没有记录；仍交付原身份收据，不能诱导创建新请求。
        if (row is null) return Result<AiKnowledgeEvaluationMutation>.Success(new(null, saved.RevisionId, saved.Replayed, "source_unavailable"));
        var proven = await ProveAsync(row, context, token).ConfigureAwait(false);
        return Result<AiKnowledgeEvaluationMutation>.Success(new(proven is null ? null : Map(row, context.Actor), saved.RevisionId,
            saved.Replayed, proven is null ? "source_unavailable" : "available"));
    }
    private async Task<AiKnowledgeEvaluationRow?> ReadAsync(Guid knowledge, Guid set, Context context, CancellationToken token)
    {
        var p = await ParametersAsync(knowledge, set, context, token).ConfigureAwait(false);
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationRow>(AiKnowledgeEvaluationSql.ReadFor(database.Value.Provider, context.Scoped), p, token).ConfigureAwait(false);
        Check(context); return row;
    }
    private async Task<Dictionary<string, object?>> ParametersAsync(Guid knowledge, Guid? set, Context context, CancellationToken token)
    {
        if (knowledge == Guid.Empty || set == Guid.Empty) throw new ArgumentException("评估集路径身份不能为空。");
        Check(context); var p = await access.ParametersAsync(context.Actor, token).ConfigureAwait(false); Check(context);
        p["KnowledgeBaseId"] = knowledge; p["SetId"] = set; return p;
    }
    private static AiKnowledgeEvaluationFootprint Footprint(AiKnowledgeEvaluationRow row)
    {
        try
        {
            if (AiKnowledgeEvaluationPolicy.Hash(row.FootprintJson) != row.FootprintHash) throw new InvalidDataException();
            return AiKnowledgeEvaluationPolicy.DecodeFootprint(row.FootprintJson);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException) { throw new CorruptEvaluationException(); }
    }
    private static AiKnowledgeEvaluationBody Body(AiKnowledgeEvaluationRow row)
    {
        try
        {
            if (AiKnowledgeEvaluationPolicy.Hash(row.SourceJson) != row.SourceHash
                || AiKnowledgeEvaluationPolicy.ContentHash(row.ContentJson, row.SourceHash) != row.ContentHash) throw new InvalidDataException();
            var body = AiKnowledgeEvaluationPolicy.Decode(row.ContentJson);
            AiKnowledgeEvaluationPolicy.Match(body, AiKnowledgeEvaluationPolicy.DecodeFootprint(row.SourceJson));
            AiKnowledgeEvaluationPolicy.Match(body, Footprint(row)); return body;
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException) { throw new CorruptEvaluationException(); }
    }
    private static AiKnowledgeEvaluationWorkspace Map(AiKnowledgeEvaluationRow row, Guid actor)
    {
        var body = Body(row); var source = Footprint(row);
        return new(row.Id, row.KnowledgeBaseId, row.Version, new(row.RevisionId, row.RevisionNo, body.Name, body.State, body.Cases,
            row.ContentHash, row.SourceHash, source.Sources.Count, row.CreatedAtUtc, row.RestoredFromRevisionId),
            row.ApprovalState ?? "none", row.ApprovalId, row.RevisionId == row.HeadRevisionId && AiKnowledgeEvaluationPolicy.CanApprove(body, source, actor));
    }
    private async Task<Result<T>> Run<T>(Guid actor, string permission, Func<Context, CancellationToken, Task<Result<T>>> action, CancellationToken token)
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("评估集用例不能加入外部事务。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var session = await authorization.AuthorizeAllAsync([AiKnowledgePermissions.Read, AiKnowledgeEvaluationPermissions.Read, permission], deadline.Token).ConfigureAwait(false);
            var context = new Context(actor, tenant.Id, AiKnowledgeScope.IsTenant(tenant), session?.SessionId ?? Guid.Empty, permission);
            if (actor == Guid.Empty || session is null || session.UserId != actor || session.TenantId != context.TenantId || context.SessionId == Guid.Empty) throw new UnauthorizedAccessException();
            Check(context); return await action(context, deadline.Token).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException) { return Fail<T>("authorization.permission_denied", ErrorType.Forbidden); }
        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return Unavailable<T>(); }
        catch (CorruptEvaluationException) { return Unavailable<T>(); }
        catch (DataCommandException error) when (error.Kind == DataCommandFailureKind.Deadlock) { return Conflict<T>(); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or JsonException) { return Invalid<T>(); }
    }
    private async Task AuthorizeAsync(Context context, string permission, CancellationToken token, AiKnowledgeEvaluationFootprint? footprint = null)
    {
        Check(context);
        var required = new[] { AiKnowledgePermissions.Read, AiKnowledgeEvaluationPermissions.Read, permission }
            .Concat(context.ExtraPermissions)
            .Concat(footprint is null ? [] : AiKnowledgeEvaluationSourceAuthority.RequiredFor(footprint)).Distinct(StringComparer.Ordinal).ToArray();
        var session = await authorization.AuthorizeAllAsync(required, token).ConfigureAwait(false);
        if (session?.UserId != context.Actor || session?.TenantId != context.TenantId || session?.SessionId != context.SessionId) throw new UnauthorizedAccessException();
        Check(context);
    }
    private void Check(Context context, bool inside = false)
    {
        if ((!inside && transactions.HasTransaction) || context.TenantId != tenant.Id || context.Scoped != AiKnowledgeScope.IsTenant(tenant)) throw new UnauthorizedAccessException();
    }
    private static string Fingerprint(Context context, Guid knowledge, Guid? set, string purpose, string json) => AiKnowledgeEvaluationPolicy.Hash(
        purpose + "\n" + context.Actor.ToString("D") + "\n" + knowledge.ToString("D") + "\n" + set?.ToString("D") + "\n" + json);
    private Guid NewId() { var id = ids.NewId(); AiKnowledgeFaqDraftPolicy.Key(id); return id; }
    private DateTimeOffset Now() { var value = clock.UtcNow; return new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero); }
    private static Result<T> Fail<T>(string code, ErrorType type) => Result<T>.Failure(new(code, "The evaluation set operation is unavailable or invalid.", type));
    private static Result<T> Invalid<T>() => Fail<T>("ai.knowledge_evaluation_sets.input_invalid", ErrorType.Validation);
    private static Result<T> Missing<T>() => Fail<T>("ai.knowledge_evaluation_sets.not_found", ErrorType.NotFound);
    private static Result<T> Conflict<T>() => Fail<T>("ai.knowledge_evaluation_sets.version_conflict", ErrorType.Conflict);
    private static Result<T> Unavailable<T>() => Fail<T>("ai.knowledge_evaluation_sets.source_unavailable", ErrorType.Conflict);
}
