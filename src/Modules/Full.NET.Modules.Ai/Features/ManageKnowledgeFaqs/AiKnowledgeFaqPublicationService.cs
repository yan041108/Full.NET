using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;

/// <summary>准确 FAQ 修订、完整索引代次和有效期一起审核；两个发布指针与问法占用同事务推进。</summary>
internal sealed class AiKnowledgeFaqPublicationService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IIdGenerator ids, AiKnowledgeFaqLifecycleAccess gate, AiKnowledgeFaqIndexInput input, AiKnowledgeIndexActivationStore proof,
    AiKnowledgeFaqPublishedSourceReader? publishedSources = null)
{
    private const string Kind = "faq-publication-v1";

    internal Task<Result<AiKnowledgeFaqPublication>> RequestAsync(Guid knowledge, Guid faq, RequestAiKnowledgeFaqPublication request,
        SessionBindingSnapshot actor, CancellationToken token) => Run(async ct =>
    {
        if (!await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Publish, ct, AiKnowledgePublicationPermissions.Request).ConfigureAwait(false)) return Denied<AiKnowledgeFaqPublication>();
        AiKnowledgeFaqDraftPolicy.Key(request.RequestKey); AiKnowledgeFaqDraftPolicy.Key(request.ExpectedRevisionId); AiKnowledgeFaqDraftPolicy.Key(request.VersionId);
        var from = Time(request.EffectiveFromUtc); var until = Time(request.EffectiveToUtc);
        if (request.ExpectedPublicationVersion is < 0 or int.MaxValue || request.ExpectedHeadVersion < 1 || until <= gate.Now || until <= from
            || until - from > TimeSpan.FromDays(366) || from > gate.Now.AddDays(366)) return Invalid<AiKnowledgeFaqPublication>();
        var p = await gate.ParametersAsync(knowledge, faq, actor, ct).ConfigureAwait(false);
        var root = await gate.ReadAsync(p, request.ExpectedRevisionId, ct, true).ConfigureAwait(false);
        if (root is null) return Missing<AiKnowledgeFaqPublication>();
        if (root.HeadRevisionId != request.ExpectedRevisionId || root.HeadVersion != request.ExpectedHeadVersion) return Conflict<AiKnowledgeFaqPublication>();
        var content = AiKnowledgeFaqLifecycleAccess.Content(root);
        if (content.ValidFromUtc is { } lower && from < lower || content.ExpiresAtUtc is { } upper && until > upper) return Invalid<AiKnowledgeFaqPublication>();
        if (!await gate.ProveAsync(root, actor, AiKnowledgeFaqPermissions.Publish, true, ct).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqPublication>();
        var requestHash = AiKnowledgeFaqDraftPolicy.Hash(FormattableString.Invariant($"faq_publication_v1\n{faq:N}\n{request.ExpectedRevisionId:N}\n{request.VersionId:N}\n{request.ExpectedHeadVersion}\n{request.ExpectedPublicationVersion}\n{from:O}\n{until:O}\n{root.ContentHash}\n{root.SourceHash}"));
        p["RequestKey"] = request.RequestKey;
        var existing = await query.QuerySingleOrDefaultAsync<AiKnowledgePublicationRecord>(gate.Choose(AiKnowledgePublicationSql.FindKeyHost, AiKnowledgePublicationSql.FindKeyTenant), p, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            p["Id"] = existing.Id; var link = await Link(p, ct).ConfigureAwait(false);
            if (link is null || link.RequestHash != requestHash || !Original(existing, actor)) return Conflict<AiKnowledgeFaqPublication>();
            return await GetCore(p, existing.Id, actor, AiKnowledgeFaqPermissions.Publish, ct).ConfigureAwait(false);
        }
        var source = await gate.CandidateAsync(root, request.VersionId, p, actor, AiKnowledgeFaqPermissions.Publish, true, ct).ConfigureAwait(false);
        if (source is null || source.OwnerUserId != actor.UserId) return Conflict<AiKnowledgeFaqPublication>();
        var snapshot = new AiKnowledgeFaqPublicationSnapshot(Kind, faq, root.RevisionId, root.ContentHash, root.SourceHash, from, until, source);
        var json = JsonSerializer.Serialize(snapshot, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqPublicationSnapshot);
        Bind(p, source); p["Id"] = ids.NewId(); p["RevisionId"] = root.RevisionId; p["ContentHash"] = root.ContentHash;
        p["RequestHash"] = requestHash; p["SnapshotJson"] = json; p["SnapshotHash"] = AiKnowledgeFaqDraftPolicy.Hash(json);
        p["ExpectedHeadVersion"] = request.ExpectedPublicationVersion; p["EffectiveFromUtc"] = from; p["EffectiveToUtc"] = until;
        p["SessionId"] = actor.SessionId; p["SessionKind"] = actor.SessionKind; p["SecurityStamp"] = actor.SecurityStamp;
        p["ActorScope"] = actor.ActorScope; p["EffectiveScope"] = actor.EffectiveScope;
        if (!await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Publish, ct, AiKnowledgePublicationPermissions.Request).ConfigureAwait(false)
            || !await gate.ProveAsync(root, actor, AiKnowledgeFaqPermissions.Publish, true, ct).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqPublication>();
        var stored = await transaction.ExecuteResultAsync(async tx =>
        {
            if (!await Lock(p, source.OwnerUserId, tx).ConfigureAwait(false)) return Conflict<bool>();
            _ = await command.ExecuteAsync(gate.Choose(AiKnowledgePublicationSql.EnsureHeadHost, AiKnowledgePublicationSql.EnsureHeadTenant), p, tx).ConfigureAwait(false);
            var head = await Head(p, tx).ConfigureAwait(false);
            if (head?.Version != request.ExpectedPublicationVersion
                || await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqLifecycleSql.InsertHost, AiKnowledgeFaqLifecycleSql.InsertTenant), p, tx).ConfigureAwait(false) != 1
                || await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqLifecycleSql.BindHost, AiKnowledgeFaqLifecycleSql.BindTenant), p, tx).ConfigureAwait(false) != 1) return Conflict<bool>();
            await Audit(p, "requested", tx).ConfigureAwait(false); return Result<bool>.Success(true);
        }, ct).ConfigureAwait(false);
        if (!stored.IsSuccess) return Result<AiKnowledgeFaqPublication>.Failure(stored.Error!);
        return await GetCore(p, (Guid)p["Id"]!, actor, AiKnowledgeFaqPermissions.Publish, ct).ConfigureAwait(false);
    }, token);

    internal Task<Result<AiKnowledgeFaqPublication>> ReadAsync(Guid knowledge, Guid faq, Guid id, SessionBindingSnapshot actor, CancellationToken token) => Run(async ct =>
    {
        var operation = await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Review, ct, AiKnowledgePublicationPermissions.Read).ConfigureAwait(false) ? AiKnowledgeFaqPermissions.Review
            : await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Publish, ct, AiKnowledgePublicationPermissions.Read).ConfigureAwait(false) ? AiKnowledgeFaqPermissions.Publish : null;
        if (operation is null) return Denied<AiKnowledgeFaqPublication>();
        var p = await gate.ParametersAsync(knowledge, faq, actor, ct).ConfigureAwait(false);
        if (await gate.ReadAsync(p, null, ct, false).ConfigureAwait(false) is null) return Missing<AiKnowledgeFaqPublication>();
        return await GetCore(p, id, actor, operation, ct).ConfigureAwait(false);
    }, token);

    internal Task<Result<AiKnowledgeFaqPublication>> ReviewAsync(Guid knowledge, Guid faq, Guid id, ReviewAiKnowledgePublication request,
        SessionBindingSnapshot actor, CancellationToken token) => Decide(knowledge, faq, id, request.Version, request.SnapshotHash, false, request.Approve, actor, token);
    internal Task<Result<AiKnowledgeFaqPublication>> PublishAsync(Guid knowledge, Guid faq, Guid id, PublishAiKnowledgePublication request,
        SessionBindingSnapshot actor, CancellationToken token) => Decide(knowledge, faq, id, request.Version, request.SnapshotHash, true, true, actor, token);

    private Task<Result<AiKnowledgeFaqPublication>> Decide(Guid knowledge, Guid faq, Guid id, int version, string hash, bool publishing, bool approved,
        SessionBindingSnapshot actor, CancellationToken token) => Run(async ct =>
    {
        var operation = publishing ? AiKnowledgeFaqPermissions.Publish : AiKnowledgeFaqPermissions.Review;
        if (!await gate.AuthorizedAsync(actor, operation, ct, publishing ? AiKnowledgePublicationPermissions.Publish : AiKnowledgePublicationPermissions.Review).ConfigureAwait(false)) return Denied<AiKnowledgeFaqPublication>();
        if (id == Guid.Empty || version < 1 || hash is not { Length: 64 }) return Invalid<AiKnowledgeFaqPublication>();
        var p = await gate.ParametersAsync(knowledge, faq, actor, ct).ConfigureAwait(false);
        if (await gate.ReadAsync(p, null, ct, false).ConfigureAwait(false) is null) return Missing<AiKnowledgeFaqPublication>();
        p["Id"] = id; var link = await Link(p, ct).ConfigureAwait(false); var row = await Find(p, ct).ConfigureAwait(false);
        if (link is null || row is null || row.SnapshotHash != hash || publishing && !Original(row, actor) || !publishing && row.ActorUserId == actor.UserId) return Conflict<AiKnowledgeFaqPublication>();
        if (publishing && row.StateKey == "published" && row.Version == version + 1)
            return await GetCore(p, id, actor, operation, ct).ConfigureAwait(false);
        if (row.Version != version || row.StateKey != (publishing ? "approved" : "pending") || row.EffectiveToUtc <= gate.Now) return Conflict<AiKnowledgeFaqPublication>();
        var root = await gate.ReadAsync(p, link.RevisionId, ct, false).ConfigureAwait(false);
        if (root is null || root.HeadRevisionId != link.RevisionId || root.SourceHash != link.SourceHash || root.ContentHash != link.ContentHash) return Conflict<AiKnowledgeFaqPublication>();
        var frozen = Decode(row, link);
        var source = await gate.CandidateAsync(root, link.VersionId, p, actor, operation, publishing, ct).ConfigureAwait(false);
        if (source is null || source != frozen.Source) return Conflict<AiKnowledgeFaqPublication>();
        Bind(p, source); p["ActorUserId"] = row.ActorUserId; p["ReviewerUserId"] = actor.UserId;
        p["Version"] = version; p["SnapshotHash"] = hash; p["RevisionId"] = link.RevisionId; p["ExpectedHeadVersion"] = row.ExpectedHeadVersion;
        p["DecisionKey"] = approved ? "approved" : "rejected"; p["Now"] = gate.Now;
        if (!await gate.AuthorizedAsync(actor, operation, ct, publishing ? AiKnowledgePublicationPermissions.Publish : AiKnowledgePublicationPermissions.Review).ConfigureAwait(false)
            || !await gate.ProveAsync(root, actor, operation, publishing, ct).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqPublication>();
        var stored = await transaction.ExecuteResultAsync(async tx =>
        {
            if (!await Lock(p, source.OwnerUserId, tx).ConfigureAwait(false)) return Conflict<bool>();
            if (publishing)
            {
                var head = await Head(p, tx).ConfigureAwait(false); p["PreviousPublicationId"] = head?.PublicationId;
                if (head?.Version != row.ExpectedHeadVersion) return Conflict<bool>();
                _ = await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqLifecycleSql.ExpireQueriesHost, AiKnowledgeFaqLifecycleSql.ExpireQueriesTenant), p, tx).ConfigureAwait(false);
                _ = await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqLifecycleSql.DeleteQueriesHost, AiKnowledgeFaqLifecycleSql.DeleteQueriesTenant), p, tx).ConfigureAwait(false);
                var content = AiKnowledgeFaqLifecycleAccess.Content(root);
                foreach (var question in content.Aliases.Prepend(content.Question))
                {
                    p["ClaimId"] = ids.NewId(); p["QueryHash"] = AiKnowledgeFaqDraftPolicy.Hash("faq_question_v1\n" + AiKnowledgeFaqDraftPolicy.QuestionKey(question));
                    if (await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqLifecycleSql.ClaimQueryHost, AiKnowledgeFaqLifecycleSql.ClaimQueryTenant), p, tx).ConfigureAwait(false) != 1) return Conflict<bool>();
                }
                if (await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqLifecycleSql.PublishHeadHost, AiKnowledgeFaqLifecycleSql.PublishHeadTenant), p, tx).ConfigureAwait(false) != 1
                    || await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqLifecycleSql.AdvanceHost, AiKnowledgeFaqLifecycleSql.AdvanceTenant), p, tx).ConfigureAwait(false) != 1) return Conflict<bool>();
                _ = await command.ExecuteAsync(gate.Choose(AiKnowledgePublicationSql.SupersedeHost, AiKnowledgePublicationSql.SupersedeTenant), p, tx).ConfigureAwait(false);
                if (await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqLifecycleSql.PublishedStateHost, AiKnowledgeFaqLifecycleSql.PublishedStateTenant), p, tx).ConfigureAwait(false) != 1) return Conflict<bool>();
            }
            else if (await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqLifecycleSql.DecideHost, AiKnowledgeFaqLifecycleSql.DecideTenant), p, tx).ConfigureAwait(false) != 1) return Conflict<bool>();
            await Audit(p, publishing ? "published" : approved ? "approved" : "rejected", tx).ConfigureAwait(false); return Result<bool>.Success(true);
        }, ct).ConfigureAwait(false);
        if (!stored.IsSuccess) return Result<AiKnowledgeFaqPublication>.Failure(stored.Error!);
        return await GetCore(p, id, actor, operation, ct).ConfigureAwait(false);
    }, token);

    internal Task<Result<AiKnowledgePublicationHeadResponse>> WithdrawAsync(Guid knowledge, Guid faq, WithdrawAiKnowledgePublication request,
        SessionBindingSnapshot actor, CancellationToken token) => Run(async ct =>
    {
        if (!await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Publish, ct, AiKnowledgePublicationPermissions.Withdraw).ConfigureAwait(false)) return Denied<AiKnowledgePublicationHeadResponse>();
        if (request.PublicationId == Guid.Empty || request.ExpectedHeadVersion is < 1 or int.MaxValue) return Invalid<AiKnowledgePublicationHeadResponse>();
        var p = await gate.ParametersAsync(knowledge, faq, actor, ct).ConfigureAwait(false);
        // 关闭服务不读取正文，也不因叶来源已撤权而阻止当前所有者撤销发布。
        var document = await query.QuerySingleOrDefaultAsync<Guid>(gate.Choose(AiKnowledgeFaqLifecycleSql.OwnedDocumentHost, AiKnowledgeFaqLifecycleSql.OwnedDocumentTenant), p, ct).ConfigureAwait(false);
        if (document == Guid.Empty) return Missing<AiKnowledgePublicationHeadResponse>();
        p["DocumentId"] = document; p["Id"] = request.PublicationId; p["ExpectedHeadVersion"] = request.ExpectedHeadVersion;
        if (await Link(p, ct).ConfigureAwait(false) is null) return Missing<AiKnowledgePublicationHeadResponse>();
        if (!await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Publish, ct, AiKnowledgePublicationPermissions.Withdraw).ConfigureAwait(false)) return Denied<AiKnowledgePublicationHeadResponse>();
        return await transaction.ExecuteResultAsync(async tx =>
        {
            if (!await Lock(p, actor.UserId, tx).ConfigureAwait(false)
                || await command.ExecuteAsync(gate.Choose(AiKnowledgePublicationSql.WithdrawHeadHost, AiKnowledgePublicationSql.WithdrawHeadTenant), p, tx).ConfigureAwait(false) != 1
                || await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqLifecycleSql.WithdrawHost, AiKnowledgeFaqLifecycleSql.WithdrawTenant), p, tx).ConfigureAwait(false) != 1
                || await command.ExecuteAsync(gate.Choose(AiKnowledgePublicationSql.WithdrawnHost, AiKnowledgePublicationSql.WithdrawnTenant), p, tx).ConfigureAwait(false) != 1) return Conflict<AiKnowledgePublicationHeadResponse>();
            _ = await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqLifecycleSql.DeleteQueriesHost, AiKnowledgeFaqLifecycleSql.DeleteQueriesTenant), p, tx).ConfigureAwait(false);
            await Audit(p, "withdrawn", tx).ConfigureAwait(false);
            return Result<AiKnowledgePublicationHeadResponse>.Success(new(document, null, request.ExpectedHeadVersion + 1));
        }, ct).ConfigureAwait(false);
    }, token);

    internal Task<Result<AiKnowledgeFaqLifecycle>> LifecycleAsync(Guid knowledge, Guid faq, SessionBindingSnapshot actor, CancellationToken token) => Run(async ct =>
    {
        if (!await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Read, ct).ConfigureAwait(false)) return Denied<AiKnowledgeFaqLifecycle>();
        var p = await gate.ParametersAsync(knowledge, faq, actor, ct).ConfigureAwait(false);
        var root = await gate.ReadAsync(p, null, ct, true).ConfigureAwait(false);
        if (root is null) return Missing<AiKnowledgeFaqLifecycle>();
        if (!await gate.ProveAsync(root, actor, AiKnowledgeFaqPermissions.Read, true, ct).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqLifecycle>();
        p["RevisionId"] = root.RevisionId;
        var index = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqIndexSourceRecord>(gate.Choose(AiKnowledgeFaqIndexSql.RevisionHost, AiKnowledgeFaqIndexSql.RevisionTenant), p, ct).ConfigureAwait(false);
        AiKnowledgeIndexStatusRecord? status = null;
        var state = "pending_processing";
        if (index is not null)
        {
            p["VersionId"] = index.Id;
            status = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.ForFaq(gate.Provider, gate.Scoped), p, ct).ConfigureAwait(false);
            state = status is null ? "stale" : status.StateKey == "completed"
                ? await gate.CandidateAsync(root, index.Id, p, actor, AiKnowledgeFaqPermissions.Read, true, ct).ConfigureAwait(false) is not null ? "ready" : "failed"
                : status.StateKey == "running" ? "processing" : status.StateKey ?? "pending_processing";
        }
        var head = await Head(p, ct).ConfigureAwait(false); AiKnowledgePublicationRecord? publication = null; AiKnowledgeFaqReleaseRecord? published = null;
        if (head?.PublicationId is { } id)
        {
            p["Id"] = id; publication = await Find(p, ct).ConfigureAwait(false); published = await Link(p, ct).ConfigureAwait(false);
        }
        var publicationState = publication is null ? "unpublished" : publication.StateKey != "published" ? "unavailable"
            : publication.EffectiveToUtc <= gate.Now ? "expired" : publication.EffectiveFromUtc > gate.Now ? "scheduled" : "published";
        var pending = await query.QuerySingleOrDefaultAsync<Guid?>(AiKnowledgeFaqLifecycleSql.PendingFor(gate.Provider, gate.Scoped), p, ct).ConfigureAwait(false);
        if (!await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Read, ct).ConfigureAwait(false)
            || !await gate.ProveAsync(root, actor, AiKnowledgeFaqPermissions.Read, true, ct).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqLifecycle>();
        return Result<AiKnowledgeFaqLifecycle>.Success(new(root.Id, root.ReservedDocumentId, root.RevisionId, root.HeadVersion, index?.Id, status?.TaskId,
            state, head?.PublicationId, head?.Version ?? 0, published?.RevisionId, publicationState, publication?.EffectiveFromUtc, publication?.EffectiveToUtc, pending));
    }, token);

    internal Task<Result<AiKnowledgeFaqPublishedContent>> PublishedAsync(Guid knowledge, Guid faq, SessionBindingSnapshot actor, CancellationToken token) => Run(async ct =>
    {
        if (!await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Read, ct).ConfigureAwait(false)) return Denied<AiKnowledgeFaqPublishedContent>();
        var p = await gate.ParametersAsync(knowledge, faq, actor, ct).ConfigureAwait(false);
        if (await gate.ReadAsync(p, null, ct, false).ConfigureAwait(false) is null) return Missing<AiKnowledgeFaqPublishedContent>();
        var link = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqReleaseRecord>(AiKnowledgeFaqLifecycleSql.PublishedFor(gate.Provider, gate.Scoped), p, ct).ConfigureAwait(false);
        if (link is null) return Missing<AiKnowledgeFaqPublishedContent>();
        p["Id"] = link.Id; var row = await Find(p, ct).ConfigureAwait(false); var root = await gate.ReadAsync(p, link.RevisionId, ct, false).ConfigureAwait(false);
        if (row is null || root is null || root.SourceHash != link.SourceHash || root.ContentHash != link.ContentHash) return SourceMissing<AiKnowledgeFaqPublishedContent>();
        var frozen = Decode(row, link);
        if (!await PublishedProof(root, row, frozen, p, actor, AiKnowledgeFaqPermissions.Read, ct).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqPublishedContent>();
        p["Now"] = gate.Now;
        var final = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqReleaseRecord>(AiKnowledgeFaqLifecycleSql.PublishedFor(gate.Provider, gate.Scoped), p, ct).ConfigureAwait(false);
        if (final != link || !await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Read, ct).ConfigureAwait(false)
            || !await gate.ProveAsync(root, actor, AiKnowledgeFaqPermissions.Read, false, ct).ConfigureAwait(false) || row.EffectiveToUtc <= gate.Now) return SourceMissing<AiKnowledgeFaqPublishedContent>();
        return Result<AiKnowledgeFaqPublishedContent>.Success(new(faq, root.RevisionId, row.Id, root.OriginKey, root.Basis, root.SourceHash,
            AiKnowledgeFaqLifecycleAccess.Content(root), AiKnowledgeFaqLifecycleAccess.PublicSources(root), row.EffectiveFromUtc, row.EffectiveToUtc)
            { Graph = AiKnowledgeFaqGraphs.PublicGraph(AiKnowledgeFaqProvenanceCodec.Decode(root).Graph), Feedback = AiKnowledgeFaqProvenanceCodec.Decode(root).Feedback?.Public(), PersonalSchedules = AiKnowledgeFaqBusinessSources.Public(AiKnowledgeFaqProvenanceCodec.Decode(root)) });
    }, token);

    private async Task<Result<AiKnowledgeFaqPublication>> GetCore(Dictionary<string, object?> p, Guid id, SessionBindingSnapshot actor, string operation, CancellationToken token)
    {
        p["Id"] = id; var link = await Link(p, token).ConfigureAwait(false); var row = await Find(p, token).ConfigureAwait(false);
        if (link is null || row is null) return Missing<AiKnowledgeFaqPublication>();
        var root = await gate.ReadAsync(p, link.RevisionId, token, false).ConfigureAwait(false);
        if (root is null || root.SourceHash != link.SourceHash || root.ContentHash != link.ContentHash
            || !await gate.ProveAsync(root, actor, operation, false, token).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqPublication>();
        var snapshot = Decode(row, link); bool current;
        if (row.StateKey == "published") current = await PublishedProof(root, row, snapshot, p, actor, operation, token).ConfigureAwait(false);
        else current = await gate.CandidateAsync(root, link.VersionId, p, actor, operation, false, token).ConfigureAwait(false) == snapshot.Source && row.EffectiveToUtc > gate.Now;
        if (!await gate.ProveAsync(root, actor, operation, false, token).ConfigureAwait(false)
            || !await gate.AuthorizedAsync(actor, operation, token).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqPublication>();
        return Result<AiKnowledgeFaqPublication>.Success(new(row.Id, link.FaqId, link.RevisionId, link.VersionId, row.GenerationId, row.SnapshotHash,
            row.StateKey, row.EffectiveFromUtc, row.EffectiveToUtc, row.ExpectedHeadVersion, row.ActorUserId, row.ReviewerUserId, row.Version,
            AiKnowledgeFaqLifecycleAccess.Content(root), root.OriginKey, root.Basis, root.SourceHash, AiKnowledgeFaqLifecycleAccess.PublicSources(root), current)
            { Graph = AiKnowledgeFaqGraphs.PublicGraph(AiKnowledgeFaqProvenanceCodec.Decode(root).Graph), Feedback = AiKnowledgeFaqProvenanceCodec.Decode(root).Feedback?.Public(), PersonalSchedules = AiKnowledgeFaqBusinessSources.Public(AiKnowledgeFaqProvenanceCodec.Decode(root)) });
    }

    private async Task<bool> PublishedProof(AiKnowledgeFaqDraftRecord root, AiKnowledgePublicationRecord row, AiKnowledgeFaqPublicationSnapshot snapshot,
        Dictionary<string, object?> p, SessionBindingSnapshot actor, string operation, CancellationToken token)
    {
        if (publishedSources is not null)
            return await publishedSources.ReadAsync(snapshot.Source with { PublicationId = row.Id }, actor.UserId, token, verifyPayloads: true).ConfigureAwait(false) is not null
                && await gate.AuthorizedAsync(actor, operation, token).ConfigureAwait(false);
        if (row.StateKey != "published" || row.EffectiveFromUtc > gate.Now || row.EffectiveToUtc <= gate.Now
            || !await gate.ProveAsync(root, actor, operation, false, token).ConfigureAwait(false)) return false;
        var source = snapshot.Source with { PublicationId = row.Id };
        p["VersionId"] = source.VersionId; p["TaskId"] = source.TaskId; p["PublicationId"] = row.Id;
        if (await input.ClassificationAsync(root, actor.UserId, token).ConfigureAwait(false) != source.DataClassification
            || !await proof.IsReadyForReadAsync(source, p, token).ConfigureAwait(false)) return false;
        p["Now"] = gate.Now;
        var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqReleaseRecord>(AiKnowledgeFaqLifecycleSql.PublishedFor(gate.Provider, gate.Scoped), p, token).ConfigureAwait(false);
        return current?.Id == row.Id && current.RevisionId == root.RevisionId && await gate.ProveAsync(root, actor, operation, false, token).ConfigureAwait(false);
    }

    internal static AiKnowledgeFaqPublicationSnapshot Decode(AiKnowledgePublicationRecord row, AiKnowledgeFaqReleaseRecord link)
    {
        if (row.SnapshotJson.Length > 32768 || AiKnowledgeFaqDraftPolicy.Hash(row.SnapshotJson) != row.SnapshotHash) throw new InvalidDataException();
        var value = JsonSerializer.Deserialize(row.SnapshotJson, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqPublicationSnapshot) ?? throw new InvalidDataException();
        if (value.Kind != Kind || value.FaqId != link.FaqId || value.RevisionId != link.RevisionId || value.SourceHash != link.SourceHash || value.ContentHash != link.ContentHash
            || value.EffectiveFromUtc != row.EffectiveFromUtc || value.EffectiveToUtc != row.EffectiveToUtc || value.Source.VersionId != link.VersionId
            || value.Source.DocumentId != row.DocumentId || value.Source.TaskId != row.GenerationId || value.Source.FaqRevisionId != link.RevisionId
            || value.Source.FileId is not null || value.Source.PublicationId is not null || value.Source.IsCurrent != 1 || value.Source.StateKey != "completed") throw new InvalidDataException();
        return value;
    }
    private Task<AiKnowledgeFaqReleaseRecord?> Link(Dictionary<string, object?> p, CancellationToken token) => query.QuerySingleOrDefaultAsync<AiKnowledgeFaqReleaseRecord>(AiKnowledgeFaqLifecycleSql.ReleaseFor(gate.Provider, gate.Scoped), p, token);
    private Task<AiKnowledgePublicationRecord?> Find(Dictionary<string, object?> p, CancellationToken token) => query.QuerySingleOrDefaultAsync<AiKnowledgePublicationRecord>(gate.Choose(AiKnowledgePublicationSql.FindHost, AiKnowledgePublicationSql.FindTenant), p, token);
    private Task<AiKnowledgePublicationHeadRecord?> Head(Dictionary<string, object?> p, CancellationToken token) => query.QuerySingleOrDefaultAsync<AiKnowledgePublicationHeadRecord>(gate.Choose(AiKnowledgePublicationSql.HeadHost, AiKnowledgePublicationSql.HeadTenant), p, token);
    private async Task<bool> Lock(Dictionary<string, object?> p, Guid owner, CancellationToken token)
    {
        p["Now"] = gate.Now; var owned = new Dictionary<string, object?>(p) { ["OwnerUserId"] = owner };
        return await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgeFaqDraftSql.LockFor(gate.Provider, gate.Scoped), owned, token).ConfigureAwait(false) == (Guid)p["KnowledgeBaseId"]!
            && await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(gate.Provider, gate.Scoped), p, token).ConfigureAwait(false) == (Guid)p["DocumentId"]!;
    }
    private async Task Audit(Dictionary<string, object?> p, string action, CancellationToken token)
    {
        p["EventId"] = ids.NewId(); p["ActionKey"] = action;
        if (await command.ExecuteAsync(gate.Choose(AiKnowledgePublicationSql.AuditHost, AiKnowledgePublicationSql.AuditTenant), p, token).ConfigureAwait(false) != 1) throw new InvalidOperationException("FAQ 发布事件必须与状态一起提交。");
    }
    private static void Bind(Dictionary<string, object?> p, AiKnowledgeIndexStatusRecord source)
    {
        p["GenerationId"] = source.TaskId; p["VersionId"] = source.VersionId; p["DocumentVersion"] = source.DocumentVersion;
        p["KnowledgeBaseVersion"] = source.KnowledgeBaseVersion; p["ModelConfigId"] = source.ModelConfigId; p["ModelConfigVersion"] = source.ModelConfigVersion;
    }
    private static bool Original(AiKnowledgePublicationRecord row, SessionBindingSnapshot actor) => row.ActorUserId == actor.UserId && row.SessionId == actor.SessionId
        && row.SessionKind == actor.SessionKind && row.SecurityStamp == actor.SecurityStamp && row.ActorScope == actor.ActorScope && row.EffectiveScope == actor.EffectiveScope;
    private static DateTimeOffset Time(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
    private static async Task<Result<T>> Run<T>(Func<CancellationToken, Task<Result<T>>> action, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try { return await action(deadline.Token).ConfigureAwait(false); }
        catch (UnauthorizedAccessException) { return Denied<T>(); }
        catch (DataCommandException error) when (error.Kind is DataCommandFailureKind.UniqueConstraint or DataCommandFailureKind.Deadlock) { return Conflict<T>(); }
        catch (Exception error) when (error is JsonException or ArgumentException) { return Invalid<T>(); }
        catch (InvalidDataException) { return Conflict<T>(); }
    }
    private static Result<T> Missing<T>() => AiKnowledgeFaqLifecycleAccess.Missing<T>();
    private static Result<T> Conflict<T>() => AiKnowledgeFaqLifecycleAccess.Conflict<T>();
    private static Result<T> Invalid<T>() => AiKnowledgeFaqLifecycleAccess.Failure<T>("ai.knowledge_faqs.input_invalid", ErrorType.Validation);
    private static Result<T> Denied<T>() => AiKnowledgeFaqLifecycleAccess.Failure<T>("authorization.permission_denied", ErrorType.Forbidden);
    private static Result<T> SourceMissing<T>() => AiKnowledgeFaqLifecycleAccess.Failure<T>("ai.knowledge_faqs.source_unavailable", ErrorType.Conflict);
}
