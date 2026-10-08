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
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageAnswerFeedback;

/// <summary>本人未审核反馈；外部授权在事务外，锁内复核消息绑定及反馈版本，不调用模型。</summary>
internal sealed class AiKnowledgeAnswerFeedbackService(IQueryExecutor query, ICommandExecutor commands,
    ICommandTransaction transaction, IDataTransactionState transactions, ICurrentTenant tenant,
    ICurrentSessionAuthorization authorization, AiKnowledgeAnswerHistoryGuard history, IOptions<DatabaseOptions> database,
    IIdGenerator ids, IClock clock)
{
    /// <summary>只交付本人清除所需的版本与状态；无需来源资格，不交付反馈载荷。</summary>
    internal async Task<Result<AiKnowledgeAnswerFeedbackStateResponse>> GetStateAsync(Guid sessionId, Guid messageId, Guid actor, CancellationToken token)
    {
        EnsureOutsideTransaction();
        var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(AiKnowledgeAnswerFeedbackPermissions.Withdraw, actor, token).ConfigureAwait(false) || !SameScope(scope))
            return Result<AiKnowledgeAnswerFeedbackStateResponse>.Failure(new Error("authorization.permission_denied", "The answer feedback is unavailable.", ErrorType.Forbidden));
        var row = await FindAsync(scope, Parameters(sessionId, messageId, actor), token).ConfigureAwait(false);
        if (!SameScope(scope) || row is null || row.MessageId != messageId || row.Version < 1 || row.StatusKey is not ("pending_review" or "withdrawn"))
            return Result<AiKnowledgeAnswerFeedbackStateResponse>.Failure(new Error(AiKnowledgeErrorCodes.FeedbackNotFound, "The answer feedback is unavailable.", ErrorType.NotFound));
        if (!await AuthorizedAsync(AiKnowledgeAnswerFeedbackPermissions.Withdraw, actor, token).ConfigureAwait(false) || !SameScope(scope))
            return Result<AiKnowledgeAnswerFeedbackStateResponse>.Failure(new Error("authorization.permission_denied", "The answer feedback is unavailable.", ErrorType.Forbidden));
        return Result<AiKnowledgeAnswerFeedbackStateResponse>.Success(new(messageId, row.StatusKey, row.Version));
    }

    /// <summary>先取得反馈行，最后验证当前完整来源，撤权后不交付旧引用或评价。</summary>
    internal async Task<Result<AiKnowledgeAnswerFeedbackResponse>> GetAsync(Guid sessionId, Guid messageId, Guid actor, CancellationToken token)
    {
        EnsureOutsideTransaction();
        var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(AiKnowledgeAnswerFeedbackPermissions.Read, actor, token).ConfigureAwait(false) || !SameScope(scope)) return Forbidden();
        var p = Parameters(sessionId, messageId, actor);
        var message = await FindMessageAsync(scope, p, token).ConfigureAwait(false);
        if (!SameScope(scope) || message?.KnowledgeLineage is null) return NotFound();
        var row = await FindAsync(scope, p, token).ConfigureAwait(false);
        if (!SameScope(scope) || row is null) return NotFound();
        var proven = await history.AuthorizeBatchAsync([new(messageId, sessionId, message.KnowledgeLineage)], actor, token).ConfigureAwait(false);
        if (!SameScope(scope) || !proven.TryGetValue(messageId, out var lineage)) return NotFound();
        if (!await AuthorizedAsync(AiKnowledgeAnswerFeedbackPermissions.Read, actor, token).ConfigureAwait(false) || !SameScope(scope)) return Forbidden();
        return Map(row, lineage.References.Select(reference => reference.Id).ToArray(), Hash(message.KnowledgeLineage));
    }

    /// <summary>固定标签与真实引用绑定；丢失响应的同版本同载荷重放不增加版本。</summary>
    internal async Task<Result<AiKnowledgeAnswerFeedbackResponse>> SetAsync(Guid sessionId, Guid messageId, Guid actor,
        SetAiKnowledgeAnswerFeedbackRequest request, CancellationToken token)
    {
        EnsureOutsideTransaction();
        var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(AiKnowledgeAnswerFeedbackPermissions.Submit, actor, token).ConfigureAwait(false) || !SameScope(scope)) return Forbidden();
        var p = Parameters(sessionId, messageId, actor);
        var message = await FindMessageAsync(scope, p, token).ConfigureAwait(false);
        if (!SameScope(scope) || message?.KnowledgeLineage is null) return NotFound();
        var proven = await history.AuthorizeBatchAsync([new(messageId, sessionId, message.KnowledgeLineage)], actor, token).ConfigureAwait(false);
        if (!SameScope(scope) || !proven.TryGetValue(messageId, out var lineage)) return NotFound();
        var references = lineage.References.Select(reference => reference.Id).ToArray();
        if (!AiKnowledgeAnswerFeedbackPolicy.IsValid(request, references)) return Invalid();
        if (!await AuthorizedAsync(AiKnowledgeAnswerFeedbackPermissions.Submit, actor, token).ConfigureAwait(false) || !SameScope(scope)) return Forbidden();
        var canonical = JsonSerializer.Serialize(request.CitationIds.Order().ToArray(), AiJsonSerializerContext.Default.GuidArray);
        var hash = Hash(message.KnowledgeLineage);
        p["Id"] = ids.NewId(); p["RatingKey"] = request.RatingKey; p["ReasonKey"] = request.ReasonKey;
        p["CitationIdsJson"] = canonical; p["LineageHash"] = hash; p["Version"] = request.Version; p["Now"] = clock.UtcNow;
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await LockAsync(scope, sessionId, p, ct).ConfigureAwait(false)) return NotFound();
            var currentMessage = await FindMessageAsync(scope, p, ct).ConfigureAwait(false);
            if (!SameScope(scope) || currentMessage?.KnowledgeLineage != message.KnowledgeLineage) return NotFound();
            var current = await FindAsync(scope, p, ct).ConfigureAwait(false);
            if (!SameScope(scope)) return Forbidden();
            if (current is not null && current.Version == request.Version + 1 && current.StatusKey == "pending_review"
                && current.RatingKey == request.RatingKey && current.ReasonKey == request.ReasonKey
                && current.CitationIdsJson == canonical && current.LineageHash == hash) return Map(current, references, hash);
            if (current is null ? request.Version != 0 : current.Version != request.Version) return Conflict();
            ct.ThrowIfCancellationRequested();
            var affected = await commands.ExecuteAsync(current is null ? AiKnowledgeAnswerFeedbackSql.InsertFor(scope.TenantId.HasValue)
                : AiKnowledgeAnswerFeedbackSql.UpdateFor(scope.TenantId.HasValue, false), p, ct).ConfigureAwait(false);
            if (affected != 1 || !SameScope(scope)) return Conflict();
            var written = await FindAsync(scope, p, ct).ConfigureAwait(false);
            if (!SameScope(scope) || written is null) return NotFound();
            ct.ThrowIfCancellationRequested();
            return Map(written, references, hash);
        }, token).ConfigureAwait(false);
    }

    /// <summary>来源撤权后仍可清除本人反馈；不返回或重新授权来源，墓碑防止旧请求复活内容。</summary>
    internal async Task<Result<AiKnowledgeAnswerFeedbackResponse>> WithdrawAsync(Guid sessionId, Guid messageId, Guid actor, int version, CancellationToken token)
    {
        EnsureOutsideTransaction();
        var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(AiKnowledgeAnswerFeedbackPermissions.Withdraw, actor, token).ConfigureAwait(false) || !SameScope(scope)) return Forbidden();
        if (version is < 1 or int.MaxValue) return Invalid();
        var p = Parameters(sessionId, messageId, actor); p["Version"] = version; p["Now"] = clock.UtcNow;
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await LockAsync(scope, sessionId, p, ct).ConfigureAwait(false)) return NotFound();
            var current = await FindAsync(scope, p, ct).ConfigureAwait(false);
            if (!SameScope(scope) || current is null) return NotFound();
            if (current.StatusKey == "withdrawn" && current.Version == version + 1) return Map(current, [], "");
            if (current.Version != version) return Conflict();
            ct.ThrowIfCancellationRequested();
            if (await commands.ExecuteAsync(AiKnowledgeAnswerFeedbackSql.UpdateFor(scope.TenantId.HasValue, true), p, ct).ConfigureAwait(false) != 1 || !SameScope(scope)) return Conflict();
            var withdrawn = await FindAsync(scope, p, ct).ConfigureAwait(false);
            if (!SameScope(scope) || withdrawn is null) return NotFound();
            ct.ThrowIfCancellationRequested();
            return Map(withdrawn, [], "");
        }, token).ConfigureAwait(false);
    }

    private async Task<bool> AuthorizedAsync(string permission, Guid actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (actor == Guid.Empty) return false;
        foreach (var code in new[] { AiChatPermissions.Read, permission })
        {
            var current = await authorization.AuthorizeAsync(code, token).ConfigureAwait(false);
            if (current?.UserId != actor || current.TenantId != tenant.Id) return false;
        }
        return true;
    }
    private bool SameScope(AiChatScope scope) => AiChatScope.Resolve(tenant) == scope;
    private void EnsureOutsideTransaction() { if (transactions.HasTransaction) throw new InvalidOperationException("反馈授权不能加入已有事务。"); }
    private static Dictionary<string, object?> Parameters(Guid session, Guid message, Guid actor) => new(AiSqlParameters.Create(("SessionId", session), ("MessageId", message), ("OwnerUserId", actor)));
    private Task<AiChatMessageRecord?> FindMessageAsync(AiChatScope scope, Dictionary<string, object?> p, CancellationToken ct) => query.QuerySingleOrDefaultAsync<AiChatMessageRecord>(AiKnowledgeAnswerFeedbackSql.MessageFor(scope.TenantId.HasValue), p, ct);
    private Task<AiKnowledgeAnswerFeedbackRecord?> FindAsync(AiChatScope scope, Dictionary<string, object?> p, CancellationToken ct) => query.QuerySingleOrDefaultAsync<AiKnowledgeAnswerFeedbackRecord>(AiKnowledgeAnswerFeedbackSql.FindFor(scope.TenantId.HasValue), p, ct);
    private async Task<bool> LockAsync(AiChatScope scope, Guid sessionId, Dictionary<string, object?> p, CancellationToken ct) =>
        await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgeAnswerFeedbackSql.LockFor(database.Value.Provider, scope.TenantId.HasValue), p, ct).ConfigureAwait(false) == sessionId && SameScope(scope);
    private static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private static Result<AiKnowledgeAnswerFeedbackResponse> Map(AiKnowledgeAnswerFeedbackRecord row, IReadOnlyList<Guid> references, string hash)
    {
        try
        {
            if (row.Id == Guid.Empty || row.MessageId == Guid.Empty || row.Version < 1 || row.CitationIdsJson.Length > 4096) return NotFound();
            var ids = JsonSerializer.Deserialize(row.CitationIdsJson, AiJsonSerializerContext.Default.GuidArray);
            if (ids is null) return NotFound();
            if (row.StatusKey == "pending_review")
            {
                if (row.LineageHash != hash || !AiKnowledgeAnswerFeedbackPolicy.IsValid(new(row.RatingKey!, row.ReasonKey!, ids, row.Version == int.MaxValue ? int.MaxValue - 1 : row.Version), references)) return NotFound();
            }
            else if (row.StatusKey != "withdrawn" || row.RatingKey is not null || row.ReasonKey is not null || row.LineageHash is not null || ids.Length != 0) return NotFound();
            return Result<AiKnowledgeAnswerFeedbackResponse>.Success(new(row.Id, row.MessageId, row.RatingKey, row.ReasonKey, Array.AsReadOnly(ids), row.StatusKey, row.Version, row.UpdatedAtUtc));
        }
        catch (JsonException) { return NotFound(); }
    }
    private static Result<AiKnowledgeAnswerFeedbackResponse> Error(string code, ErrorType type) => Result<AiKnowledgeAnswerFeedbackResponse>.Failure(new Error(code, "The answer feedback is unavailable or invalid.", type));
    private static Result<AiKnowledgeAnswerFeedbackResponse> NotFound() => Error(AiKnowledgeErrorCodes.FeedbackNotFound, ErrorType.NotFound);
    private static Result<AiKnowledgeAnswerFeedbackResponse> Forbidden() => Error("authorization.permission_denied", ErrorType.Forbidden);
    private static Result<AiKnowledgeAnswerFeedbackResponse> Invalid() => Error(AiKnowledgeErrorCodes.FeedbackInvalid, ErrorType.Validation);
    private static Result<AiKnowledgeAnswerFeedbackResponse> Conflict() => Error(AiKnowledgeErrorCodes.FeedbackConflict, ErrorType.Conflict);
}
