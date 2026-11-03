using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgePublications;

/// <summary>外部授权/文件和完整证明均在事务外，文档根锁内只核对原候选及审核绑定。</summary>
internal sealed class AiKnowledgePublicationService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database,
    IBackgroundSessionAuthorization authorization, AiKnowledgeReadAccess access, IAiKnowledgeAnswerSourceProof proof, AiKnowledgeVersionReader files)
{
    internal const int PolicyVersion = 1;
    private bool Scoped => AiKnowledgeScope.IsTenant(tenant);
    private static readonly string[] ReadPermissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgePublicationPermissions.Read];
    private SqlStatement Choose(SqlStatement host, SqlStatement scoped) => Scoped ? scoped : host;
    private async Task<bool> Authorized(SessionBindingSnapshot binding, string permission, CancellationToken token)
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("Publication authorization cannot join a transaction.");
        if (!Same(binding) || binding.SessionKind is not (SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication)) return false;
        var actor = await authorization.AuthorizeAllAsync(binding, [.. ReadPermissions, permission], token).ConfigureAwait(false);
        return Same(binding) && actor?.UserId == binding.UserId && actor.SessionId == binding.SessionId && actor.TenantId == binding.TenantId;
    }
    private bool Same(SessionBindingSnapshot binding) => binding.UserId != Guid.Empty && binding.TenantId == tenant.Id
        && (binding.TenantId.HasValue ? tenant.IsAvailable && !tenant.IsHost : tenant.IsHost);
    private async Task<Dictionary<string, object?>> Parameters(Guid knowledge, Guid document, SessionBindingSnapshot actor, CancellationToken token)
    {
        var p = await access.ParametersAsync(actor.UserId, token).ConfigureAwait(false);
        p["KnowledgeBaseId"] = knowledge; p["DocumentId"] = document; p["ActorUserId"] = actor.UserId;
        p["CurrentActorUserId"] = actor.UserId; p["CurrentSessionId"] = actor.SessionId; p["Now"] = clock.UtcNow;
        return p;
    }
    private Task<AiKnowledgeDocumentRecord?> Visible(Dictionary<string, object?> p, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeDocumentRecord>(AiKnowledgeDocumentSql.ReadFor(database.Value.Provider, Scoped), p, token);
    private Task<AiKnowledgePublicationHeadRecord?> Head(Dictionary<string, object?> p, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgePublicationHeadRecord>(Choose(AiKnowledgePublicationSql.HeadHost, AiKnowledgePublicationSql.HeadTenant), p, token);
    private Task<AiKnowledgePublicationRecord?> Find(Dictionary<string, object?> p, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgePublicationRecord>(Choose(AiKnowledgePublicationSql.FindHost, AiKnowledgePublicationSql.FindTenant), p, token);
    internal async Task<Result<AiKnowledgePublicationHeadResponse>> ReadHeadAsync(Guid knowledge, Guid document, SessionBindingSnapshot actor, CancellationToken token)
    {
        if (!await Authorized(actor, AiKnowledgePublicationPermissions.Read, token).ConfigureAwait(false)) return Forbidden<AiKnowledgePublicationHeadResponse>();
        var p = await Parameters(knowledge, document, actor, token).ConfigureAwait(false);
        if (!Same(actor) || await Visible(p, token).ConfigureAwait(false) is null) return Missing<AiKnowledgePublicationHeadResponse>();
        var head = await Head(p, token).ConfigureAwait(false);
        return Same(actor) ? Result<AiKnowledgePublicationHeadResponse>.Success(new(document, head?.PublicationId, head?.Version ?? 0)) : Forbidden<AiKnowledgePublicationHeadResponse>();
    }
    internal async Task<Result<AiKnowledgePublicationResponse>> ReadAsync(Guid knowledge, Guid document, Guid id, SessionBindingSnapshot actor, CancellationToken token)
    {
        if (!await Authorized(actor, AiKnowledgePublicationPermissions.Read, token).ConfigureAwait(false)) return Forbidden<AiKnowledgePublicationResponse>();
        var p = await Parameters(knowledge, document, actor, token).ConfigureAwait(false); p["Id"] = id;
        if (!Same(actor) || await Visible(p, token).ConfigureAwait(false) is null) return Missing<AiKnowledgePublicationResponse>();
        var row = await Find(p, token).ConfigureAwait(false);
        return row is not null && Same(actor) ? Success(row) : Missing<AiKnowledgePublicationResponse>();
    }
    internal async Task<Result<AiKnowledgePublicationResponse>> RequestAsync(Guid knowledge, Guid document, RequestAiKnowledgePublication request,
        SessionBindingSnapshot actor, CancellationToken token)
    {
        // MySQL 的微秒精度决定同身份重放的规范时刻，双库统一截断后再冻结和比较。
        request = request with { EffectiveFromUtc = CanonicalTime(request.EffectiveFromUtc), EffectiveToUtc = CanonicalTime(request.EffectiveToUtc) };
        if (!await Authorized(actor, AiKnowledgePublicationPermissions.Request, token).ConfigureAwait(false)) return Forbidden<AiKnowledgePublicationResponse>();
        if (request.RequestKey == Guid.Empty || request.VersionId == Guid.Empty || request.ExpectedHeadVersion < 0
            || request.ExpectedHeadVersion == int.MaxValue || request.EffectiveToUtc <= clock.UtcNow
            || request.EffectiveToUtc <= request.EffectiveFromUtc || request.EffectiveToUtc - request.EffectiveFromUtc > TimeSpan.FromDays(366)
            || request.EffectiveFromUtc > clock.UtcNow.AddDays(366)) return Invalid<AiKnowledgePublicationResponse>();
        var p = await Parameters(knowledge, document, actor, token).ConfigureAwait(false); p["RequestKey"] = request.RequestKey;
        if (await Visible(p, token).ConfigureAwait(false) is null) return Missing<AiKnowledgePublicationResponse>();
        var existing = await query.QuerySingleOrDefaultAsync<AiKnowledgePublicationRecord>(Choose(AiKnowledgePublicationSql.FindKeyHost, AiKnowledgePublicationSql.FindKeyTenant), p, token).ConfigureAwait(false);
        if (existing is not null) return Replay(existing, request, actor);
        var source = await Candidate(p, request.VersionId, actor, token).ConfigureAwait(false);
        if (source is null || source.OwnerUserId != actor.UserId) return Conflict<AiKnowledgePublicationResponse>();
        var json = JsonSerializer.Serialize(new AiKnowledgePublicationSnapshot(PolicyVersion, source), AiJsonSerializerContext.Default.AiKnowledgePublicationSnapshot);
        Bind(p, source); p["Id"] = ids.NewId(); p["SnapshotJson"] = json; p["SnapshotHash"] = Hash(json);
        p["ExpectedHeadVersion"] = request.ExpectedHeadVersion; p["EffectiveFromUtc"] = request.EffectiveFromUtc.ToUniversalTime(); p["EffectiveToUtc"] = request.EffectiveToUtc.ToUniversalTime();
        p["SessionId"] = actor.SessionId; p["SessionKind"] = actor.SessionKind; p["SecurityStamp"] = actor.SecurityStamp;
        p["ActorScope"] = actor.ActorScope; p["EffectiveScope"] = actor.EffectiveScope;
        if (!await Authorized(actor, AiKnowledgePublicationPermissions.Request, token).ConfigureAwait(false)) return Forbidden<AiKnowledgePublicationResponse>();
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await Lock(p, actor, ct).ConfigureAwait(false)) return Conflict<AiKnowledgePublicationResponse>();
            existing = await query.QuerySingleOrDefaultAsync<AiKnowledgePublicationRecord>(Choose(AiKnowledgePublicationSql.FindKeyHost, AiKnowledgePublicationSql.FindKeyTenant), p, ct).ConfigureAwait(false);
            if (existing is not null) return Replay(existing, request, actor);
            _ = await command.ExecuteAsync(Choose(AiKnowledgePublicationSql.EnsureHeadHost, AiKnowledgePublicationSql.EnsureHeadTenant), p, ct).ConfigureAwait(false);
            if (await command.ExecuteAsync(Choose(AiKnowledgePublicationSql.InsertHost, AiKnowledgePublicationSql.InsertTenant), p, ct).ConfigureAwait(false) != 1) return Conflict<AiKnowledgePublicationResponse>();
            await Audit(p, "requested", ct).ConfigureAwait(false);
            return Success((await Find(p, ct).ConfigureAwait(false))!);
        }, token).ConfigureAwait(false);
    }
    internal async Task<Result<AiKnowledgePublicationResponse>> ReviewAsync(Guid knowledge, Guid document, Guid id,
        ReviewAiKnowledgePublication request, SessionBindingSnapshot actor, CancellationToken token) =>
        await Advance(knowledge, document, id, request.Version, request.SnapshotHash, actor,
            request.Approve ? "approved" : "rejected", token).ConfigureAwait(false);
    internal async Task<Result<AiKnowledgePublicationResponse>> PublishAsync(Guid knowledge, Guid document, Guid id,
        PublishAiKnowledgePublication request, SessionBindingSnapshot actor, CancellationToken token) =>
        await Advance(knowledge, document, id, request.Version, request.SnapshotHash, actor, "published", token).ConfigureAwait(false);
    private async Task<Result<AiKnowledgePublicationResponse>> Advance(Guid knowledge, Guid document, Guid id, int version, string digest,
        SessionBindingSnapshot actor, string decision, CancellationToken token)
    {
        var publishing = decision == "published"; var permission = publishing ? AiKnowledgePublicationPermissions.Publish : AiKnowledgePublicationPermissions.Review;
        if (!await Authorized(actor, permission, token).ConfigureAwait(false)) return Forbidden<AiKnowledgePublicationResponse>();
        var p = await Parameters(knowledge, document, actor, token).ConfigureAwait(false); p["Id"] = id;
        if (await Visible(p, token).ConfigureAwait(false) is null) return Missing<AiKnowledgePublicationResponse>();
        var row = await Find(p, token).ConfigureAwait(false);
        if (row is null) return Missing<AiKnowledgePublicationResponse>();
        if (row.SnapshotHash != digest || version < 1 || row.StateKey != (publishing ? "approved" : "pending")
            || row.Version != version || publishing && !Original(row, actor) || !publishing && row.ActorUserId == actor.UserId) return Conflict<AiKnowledgePublicationResponse>();
        if (row.SnapshotJson.Length > 16384 || Hash(row.SnapshotJson) != row.SnapshotHash) return Conflict<AiKnowledgePublicationResponse>();
        AiKnowledgePublicationSnapshot? frozen;
        try { frozen = JsonSerializer.Deserialize(row.SnapshotJson, AiJsonSerializerContext.Default.AiKnowledgePublicationSnapshot); }
        catch (JsonException) { return Conflict<AiKnowledgePublicationResponse>(); }
        var current = await Candidate(p, row.VersionId, actor, token).ConfigureAwait(false);
        if (frozen?.PolicyVersion != PolicyVersion || current != frozen.Source || row.EffectiveToUtc <= clock.UtcNow) return Conflict<AiKnowledgePublicationResponse>();
        Bind(p, current!); p["ActorUserId"] = row.ActorUserId; p["Version"] = version; p["SnapshotHash"] = digest;
        p["ExpectedHeadVersion"] = row.ExpectedHeadVersion; p["ReviewerUserId"] = actor.UserId; p["DecisionKey"] = decision;
        if (!await Authorized(actor, permission, token).ConfigureAwait(false)) return Forbidden<AiKnowledgePublicationResponse>();
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await Lock(p, actor, ct).ConfigureAwait(false)) return Conflict<AiKnowledgePublicationResponse>();
            if (publishing)
            {
                var head = await Head(p, ct).ConfigureAwait(false); p["PreviousPublicationId"] = head?.PublicationId;
                if (head?.Version != row.ExpectedHeadVersion || await command.ExecuteAsync(Choose(AiKnowledgePublicationSql.PublishHeadHost, AiKnowledgePublicationSql.PublishHeadTenant), p, ct).ConfigureAwait(false) != 1) return Conflict<AiKnowledgePublicationResponse>();
                _ = await command.ExecuteAsync(Choose(AiKnowledgePublicationSql.SupersedeHost, AiKnowledgePublicationSql.SupersedeTenant), p, ct).ConfigureAwait(false);
                if (await command.ExecuteAsync(Choose(AiKnowledgePublicationSql.PublishedHost, AiKnowledgePublicationSql.PublishedTenant), p, ct).ConfigureAwait(false) != 1) return Conflict<AiKnowledgePublicationResponse>();
            }
            else if (await command.ExecuteAsync(Choose(AiKnowledgePublicationSql.DecideHost, AiKnowledgePublicationSql.DecideTenant), p, ct).ConfigureAwait(false) != 1) return Conflict<AiKnowledgePublicationResponse>();
            await Audit(p, decision, ct).ConfigureAwait(false); return Success((await Find(p, ct).ConfigureAwait(false))!);
        }, token).ConfigureAwait(false);
    }
    internal async Task<Result<AiKnowledgePublicationHeadResponse>> WithdrawAsync(Guid knowledge, Guid document, WithdrawAiKnowledgePublication request,
        SessionBindingSnapshot actor, CancellationToken token)
    {
        if (!await Authorized(actor, AiKnowledgePublicationPermissions.Withdraw, token).ConfigureAwait(false)) return Forbidden<AiKnowledgePublicationHeadResponse>();
        if (request.PublicationId == Guid.Empty || request.ExpectedHeadVersion < 1 || request.ExpectedHeadVersion == int.MaxValue) return Invalid<AiKnowledgePublicationHeadResponse>();
        var p = await Parameters(knowledge, document, actor, token).ConfigureAwait(false); p["Id"] = request.PublicationId; p["ExpectedHeadVersion"] = request.ExpectedHeadVersion;
        if (await Visible(p, token).ConfigureAwait(false) is null) return Missing<AiKnowledgePublicationHeadResponse>();
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await Lock(p, actor, ct).ConfigureAwait(false)) return Conflict<AiKnowledgePublicationHeadResponse>();
            if (await command.ExecuteAsync(Choose(AiKnowledgePublicationSql.WithdrawHeadHost, AiKnowledgePublicationSql.WithdrawHeadTenant), p, ct).ConfigureAwait(false) != 1) return Conflict<AiKnowledgePublicationHeadResponse>();
            if (await command.ExecuteAsync(Choose(AiKnowledgePublicationSql.WithdrawnHost, AiKnowledgePublicationSql.WithdrawnTenant), p, ct).ConfigureAwait(false) != 1) return Conflict<AiKnowledgePublicationHeadResponse>();
            await Audit(p, "withdrawn", ct).ConfigureAwait(false); return Result<AiKnowledgePublicationHeadResponse>.Success(new(document, null, request.ExpectedHeadVersion + 1));
        }, token).ConfigureAwait(false);
    }
    private async Task<AiKnowledgeIndexStatusRecord?> Candidate(Dictionary<string, object?> p, Guid version, SessionBindingSnapshot actor, CancellationToken token)
    {
        p["VersionId"] = version;
        var source = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.ForActive(database.Value.Provider, Scoped), p, token).ConfigureAwait(false);
        if (source is not { IsCurrent: 1, IsDeleted: 0, CleanupPending: 0, StateKey: "completed", TaskId: not null }
            || source.TaskId != source.ActiveTaskId || source.DataClassification is not ("public" or "internal" or "restricted") || !Same(actor)
            || !await proof.VerifyAsync(source, p, token).ConfigureAwait(false)) return null;
        var opened = await files.OpenAsync(source.KnowledgeBaseId, source.DocumentId, version, actor.UserId, token).ConfigureAwait(false);
        if (!opened.IsSuccess) return null;
        await opened.Value!.Content.DisposeAsync().ConfigureAwait(false);
        return Same(actor) ? source : null;
    }
    private async Task<bool> Lock(Dictionary<string, object?> p, SessionBindingSnapshot actor, CancellationToken token)
    { p["Now"] = clock.UtcNow; return Same(actor) && await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(database.Value.Provider, Scoped), p, token).ConfigureAwait(false) == (Guid)p["DocumentId"]! && Same(actor); }
    private async Task Audit(Dictionary<string, object?> p, string action, CancellationToken token)
    {
        p["ActionKey"] = action; p["EventId"] = ids.NewId();
        if (await command.ExecuteAsync(Choose(AiKnowledgePublicationSql.AuditHost, AiKnowledgePublicationSql.AuditTenant), p, token).ConfigureAwait(false) != 1) throw new InvalidOperationException("Publication audit must commit atomically.");
    }
    private static void Bind(Dictionary<string, object?> p, AiKnowledgeIndexStatusRecord source)
    { p["GenerationId"] = source.TaskId; p["VersionId"] = source.VersionId; p["DocumentVersion"] = source.DocumentVersion; p["KnowledgeBaseVersion"] = source.KnowledgeBaseVersion; p["ModelConfigId"] = source.ModelConfigId; p["ModelConfigVersion"] = source.ModelConfigVersion; }
    private static DateTimeOffset CanonicalTime(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
    private static bool Original(AiKnowledgePublicationRecord row, SessionBindingSnapshot actor) => row.ActorUserId == actor.UserId
        && row.SessionId == actor.SessionId && row.SessionKind == actor.SessionKind && row.SecurityStamp == actor.SecurityStamp && row.ActorScope == actor.ActorScope && row.EffectiveScope == actor.EffectiveScope;
    private static Result<AiKnowledgePublicationResponse> Replay(AiKnowledgePublicationRecord row, RequestAiKnowledgePublication request, SessionBindingSnapshot actor) =>
        Original(row, actor) && row.VersionId == request.VersionId && row.ExpectedHeadVersion == request.ExpectedHeadVersion
            && row.EffectiveFromUtc == request.EffectiveFromUtc && row.EffectiveToUtc == request.EffectiveToUtc ? Success(row) : Conflict<AiKnowledgePublicationResponse>();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static Result<AiKnowledgePublicationResponse> Success(AiKnowledgePublicationRecord row) => Result<AiKnowledgePublicationResponse>.Success(new(row.Id, row.DocumentId, row.VersionId, row.GenerationId, row.StateKey, row.SnapshotHash, PolicyVersion, row.ExpectedHeadVersion, row.Version, row.EffectiveFromUtc, row.EffectiveToUtc, row.ActorUserId, row.ReviewerUserId, row.CreatedAtUtc, row.ReviewedAtUtc));
    private static Result<T> Failure<T>(string code, ErrorType type) => Result<T>.Failure(new Error(code, "The knowledge publication operation is unavailable.", type));
    private static Result<T> Forbidden<T>() => Failure<T>("authorization.permission_denied", ErrorType.Forbidden);
    private static Result<T> Missing<T>() => Failure<T>("ai.knowledge.publication_not_found", ErrorType.NotFound);
    private static Result<T> Invalid<T>() => Failure<T>("ai.knowledge.publication_invalid", ErrorType.Validation);
    private static Result<T> Conflict<T>() => Failure<T>("ai.knowledge.publication_conflict", ErrorType.Conflict);
}
