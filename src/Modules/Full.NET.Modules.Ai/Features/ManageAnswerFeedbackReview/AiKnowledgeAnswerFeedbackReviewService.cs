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

namespace Full.NET.Modules.Ai.Features.ManageAnswerFeedbackReview;

/// <summary>独立结构化标签审核；当前评审者授权与原所属人绑定分离，失败结果回滚写入。</summary>
internal sealed class AiKnowledgeAnswerFeedbackReviewService(IQueryExecutor query, ICommandExecutor commands,
    ICommandTransaction transaction, IDataTransactionState transactions, ICurrentTenant tenant,
    ICurrentSessionAuthorization authorization, AiKnowledgeAnswerHistoryGuard history, IOptions<DatabaseOptions> database,
    IIdGenerator ids, IClock clock, AiKnowledgeAnswerFeedbackReviewCursor cursor)
{
    /// <summary>最多扫描二十五项；当前来源与反馈绑定在最终联合读取后才释放标签。</summary>
    internal async Task<Result<AiKnowledgeAnswerFeedbackReviewPage>> GetAsync(Guid actor, string? after, CancellationToken token)
    {
        EnsureOutsideTransaction(); var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(AiKnowledgeAnswerFeedbackReviewPermissions.Read, actor, token).ConfigureAwait(false) || !SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackReviewPage>(ErrorType.Forbidden);
        Guid? position = null;
        if (after is not null && (position = cursor.Read(after, actor, scope.TenantId)) is null) return Failure<AiKnowledgeAnswerFeedbackReviewPage>(ErrorType.Validation);
        var parameters = new Dictionary<string, object?> { ["ReviewerUserId"] = actor, ["AfterId"] = position };
        var rows = await PageAsync(scope, parameters, false, token).ConfigureAwait(false);
        if (!SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackReviewPage>(ErrorType.Forbidden);
        var window = rows.Take(25).ToArray(); var proven = await ProveAsync(window, actor, token).ConfigureAwait(false);
        if (!SameScope(scope) || !await AuthorizedAsync(AiKnowledgeAnswerFeedbackReviewPermissions.Read, actor, token).ConfigureAwait(false) || !SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackReviewPage>(ErrorType.Forbidden);
        var authority = await CurrentParametersAsync(window, proven, actor, token).ConfigureAwait(false);
        if (authority is null || !SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackReviewPage>(ErrorType.Forbidden);
        var current = await CurrentAsync(scope, authority, token).ConfigureAwait(false);
        if (!SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackReviewPage>(ErrorType.Forbidden);
        var items = window.Where(row => current.Contains(row) && proven.ContainsKey(row.MessageId)).Select(Map).ToArray();
        token.ThrowIfCancellationRequested();
        return Result<AiKnowledgeAnswerFeedbackReviewPage>.Success(new(items, rows.Count > 25 ? cursor.Protect(window[^1].FeedbackId, actor, scope.TenantId) : null));
    }

    /// <summary>固定批准窗口的当前统计；无客户端筛选维度，绝不释放正文或主体标识。</summary>
    internal async Task<Result<AiKnowledgeAnswerFeedbackExportResponse>> ExportAsync(Guid actor, CancellationToken token)
    {
        EnsureOutsideTransaction(); var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(AiKnowledgeAnswerFeedbackReviewPermissions.Export, actor, token).ConfigureAwait(false) || !SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackExportResponse>(ErrorType.Forbidden);
        var parameters = new Dictionary<string, object?>(); var rows = await PageAsync(scope, parameters, true, token).ConfigureAwait(false);
        if (!SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackExportResponse>(ErrorType.Forbidden);
        var window = rows.Take(64).ToArray(); var proven = await ProveAsync(window, actor, token).ConfigureAwait(false);
        if (!SameScope(scope) || !await AuthorizedAsync(AiKnowledgeAnswerFeedbackReviewPermissions.Export, actor, token).ConfigureAwait(false) || !SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackExportResponse>(ErrorType.Forbidden);
        var authority = await CurrentParametersAsync(window, proven, actor, token).ConfigureAwait(false);
        if (authority is null || !SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackExportResponse>(ErrorType.Forbidden);
        var current = await CurrentAsync(scope, authority, token).ConfigureAwait(false);
        if (!SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackExportResponse>(ErrorType.Forbidden);
        var values = window.Where(row => current.Contains(row) && proven.ContainsKey(row.MessageId) && IsCurrentReview(row) && row.DecisionKey == "approved")
            .Select(row => new AiKnowledgeAnswerFeedbackExportValue(row.OwnerUserId, row.RatingKey, row.ReasonKey, CitationCount(row))).ToArray();
        token.ThrowIfCancellationRequested();
        // 始终声明有界统计，避免完整性布尔值暴露被来源资格过滤的候选数量。
        return Result<AiKnowledgeAnswerFeedbackExportResponse>.Success(new(AiKnowledgeAnswerFeedbackReviewPolicy.ExportPolicyVersion, true, AiKnowledgeAnswerFeedbackReviewPolicy.Aggregate(values)));
    }

    /// <summary>双版本写入在同一会话锁内串行化；所有外部授权先于事务，联合 SQL 复核实时来源。</summary>
    internal async Task<Result<AiKnowledgeAnswerFeedbackReviewItem>> ReviewAsync(Guid feedbackId, Guid actor,
        ReviewAiKnowledgeAnswerFeedbackRequest request, CancellationToken token)
    {
        EnsureOutsideTransaction(); var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(AiKnowledgeAnswerFeedbackReviewPermissions.Review, actor, token).ConfigureAwait(false) || !SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.Forbidden);
        if (feedbackId == Guid.Empty || !AiKnowledgeAnswerFeedbackReviewPolicy.IsValid(request)) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.Validation);
        var parameters = new Dictionary<string, object?> { ["FeedbackId"] = feedbackId };
        var row = await FindAsync(scope, parameters, token).ConfigureAwait(false);
        if (!SameScope(scope) || row is null || row.OwnerUserId == actor) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.NotFound);
        var proven = await ProveAsync([row], actor, token).ConfigureAwait(false);
        if (!SameScope(scope) || !proven.ContainsKey(row.MessageId)) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.NotFound);
        if (!await AuthorizedAsync(AiKnowledgeAnswerFeedbackReviewPermissions.Review, actor, token).ConfigureAwait(false) || !SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.Forbidden);
        if (row.FeedbackVersion != request.FeedbackVersion) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.Conflict);
        var authority = await CurrentParametersAsync([row], proven, actor, token).ConfigureAwait(false);
        if (authority is null || !SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.Forbidden);
        foreach (var pair in authority) parameters[pair.Key] = pair.Value;
        parameters["Id"] = ids.NewId(); parameters["SessionId"] = row.SessionId;
        parameters["FeedbackVersion"] = request.FeedbackVersion; parameters["ReviewVersion"] = request.ReviewVersion;
        parameters["LineageHash"] = row.LineageHash; parameters["ReviewerUserId"] = actor; parameters["DecisionKey"] = request.DecisionKey; parameters["Now"] = clock.UtcNow;
        return await transaction.ExecuteResultAsync(async ct =>
        {
            // 会话锁使用原所属人；来源联合查询仍使用评审者，两个参数集合不能混用。
            var lockParameters = new Dictionary<string, object?>(parameters) { ["OwnerUserId"] = row.OwnerUserId };
            if (await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgeAnswerFeedbackSql.LockFor(database.Value.Provider, scope.TenantId.HasValue), lockParameters, ct).ConfigureAwait(false) != row.SessionId || !SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.NotFound);
            var current = (await CurrentAsync(scope, parameters, ct).ConfigureAwait(false)).SingleOrDefault();
            if (!SameScope(scope) || current is null) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.NotFound);
            if (current != row) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.Conflict);
            if (IsCurrentReview(current) && current.ReviewVersion == request.ReviewVersion + 1 && current.ReviewerUserId == actor && current.DecisionKey == request.DecisionKey)
                return Result<AiKnowledgeAnswerFeedbackReviewItem>.Success(Map(current));
            if ((current.ReviewVersion ?? 0) != request.ReviewVersion) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.Conflict);
            ct.ThrowIfCancellationRequested();
            var affected = await commands.ExecuteAsync(current.ReviewVersion.HasValue ? AiKnowledgeAnswerFeedbackReviewSql.UpdateFor(scope.TenantId.HasValue)
                : AiKnowledgeAnswerFeedbackReviewSql.InsertFor(scope.TenantId.HasValue), parameters, ct).ConfigureAwait(false);
            if (affected != 1 || !SameScope(scope)) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.Conflict);
            var written = (await CurrentAsync(scope, parameters, ct).ConfigureAwait(false)).SingleOrDefault();
            if (!SameScope(scope) || written is null || FeedbackBinding(written) != FeedbackBinding(row) || !IsCurrentReview(written) || written.ReviewVersion != request.ReviewVersion + 1
                || written.ReviewerUserId != actor || written.DecisionKey != request.DecisionKey) return Failure<AiKnowledgeAnswerFeedbackReviewItem>(ErrorType.Conflict);
            ct.ThrowIfCancellationRequested(); return Result<AiKnowledgeAnswerFeedbackReviewItem>.Success(Map(written));
        }, token).ConfigureAwait(false);
    }

    /// <summary>评估仅接受当前批准且全部依赖仍可读的原反馈；冻结行在授权等待后再次联合复核。</summary>
    internal async Task<AiKnowledgeAnswerFeedbackReviewRecord?> ProveEvaluationAsync(Guid feedbackId, Guid actor,
        int feedbackVersion, int reviewVersion, string permission, Guid originalSessionId, CancellationToken token)
    {
        EnsureOutsideTransaction(); var scope = AiChatScope.Resolve(tenant);
        if (permission is not (AiFeedbackEvaluationPermissions.Prepare or AiFeedbackEvaluationPermissions.Admit)
            || originalSessionId == Guid.Empty || !await AuthorizedAsync(permission, actor, token, originalSessionId).ConfigureAwait(false) || !SameScope(scope)) return null;
        var parameters = new Dictionary<string, object?> { ["FeedbackId"] = feedbackId };
        var row = await FindAsync(scope, parameters, token).ConfigureAwait(false);
        if (!SameScope(scope) || row is null || row.OwnerUserId == actor || row.FeedbackVersion != feedbackVersion
            || row.ReviewVersion != reviewVersion || !IsCurrentReview(row) || row.DecisionKey != "approved") return null;
        var proven = await ProveAsync([row], actor, token).ConfigureAwait(false);
        if (!SameScope(scope) || !proven.ContainsKey(row.MessageId)) return null;
        var authority = await CurrentParametersAsync([row], proven, actor, token).ConfigureAwait(false);
        if (authority is null || !SameScope(scope)) return null;
        foreach (var pair in authority) parameters[pair.Key] = pair.Value;
        var current = (await CurrentAsync(scope, parameters, token).ConfigureAwait(false)).SingleOrDefault();
        if (current != row || !SameScope(scope) || !await AuthorizedAsync(permission, actor, token, originalSessionId).ConfigureAwait(false)
            || !SameScope(scope)) return null;
        current = (await CurrentAsync(scope, parameters, token).ConfigureAwait(false)).SingleOrDefault();
        token.ThrowIfCancellationRequested(); EnsureOutsideTransaction();
        return SameScope(scope) && current == row ? row : null;
    }

    private async Task<IReadOnlyDictionary<Guid, AiKnowledgeAnswerLineage>> ProveAsync(IReadOnlyList<AiKnowledgeAnswerFeedbackReviewRecord> rows, Guid actor, CancellationToken token)
    {
        var candidates = rows.Where(ValidProjection).ToArray();
        var proven = await history.AuthorizeFeedbackReviewBatchAsync(candidates.Select(row => new AiKnowledgeAnswerFeedbackHistoryEntry(row.MessageId, row.SessionId, row.OwnerUserId, row.KnowledgeLineage)).ToArray(), actor, token).ConfigureAwait(false);
        return proven.Where(pair => candidates.Any(row => row.MessageId == pair.Key && AiKnowledgeAnswerFeedbackPolicy.IsValid(
            new(row.RatingKey, row.ReasonKey, JsonSerializer.Deserialize(row.CitationIdsJson, AiJsonSerializerContext.Default.GuidArray)!, 0), pair.Value.References.Select(reference => reference.Id).ToArray())))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }
    private static bool ValidProjection(AiKnowledgeAnswerFeedbackReviewRecord row)
    {
        try
        {
            if (row.FeedbackId == Guid.Empty || row.MessageId == Guid.Empty || row.SessionId == Guid.Empty || row.OwnerUserId == Guid.Empty
                || row.FeedbackVersion < 1 || row.ReviewVersion is < 1 || row.CitationIdsJson.Length > 4096 || row.KnowledgeLineage.Length > AiKnowledgeAnswerLineageCodec.MaxBytes
                || row.LineageHash != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(row.KnowledgeLineage)))) return false;
            var references = JsonSerializer.Deserialize(row.CitationIdsJson, AiJsonSerializerContext.Default.GuidArray);
            return references is not null && AiKnowledgeAnswerFeedbackPolicy.IsValid(new(row.RatingKey, row.ReasonKey, references, 0), references);
        }
        catch (JsonException) { return false; }
    }
    private static int CitationCount(AiKnowledgeAnswerFeedbackReviewRecord row) => JsonSerializer.Deserialize(row.CitationIdsJson, AiJsonSerializerContext.Default.GuidArray)!.Length;
    private static AiKnowledgeAnswerFeedbackReviewRecord FeedbackBinding(AiKnowledgeAnswerFeedbackReviewRecord row) => row with
    { ReviewFeedbackVersion = null, ReviewLineageHash = null, ReviewerUserId = null, DecisionKey = null, ReviewVersion = null };
    private static bool IsCurrentReview(AiKnowledgeAnswerFeedbackReviewRecord row) => row.ReviewFeedbackVersion == row.FeedbackVersion
        && row.ReviewLineageHash == row.LineageHash && row.ReviewVersion >= 1 && row.ReviewerUserId.HasValue && row.ReviewerUserId != Guid.Empty
        && row.ReviewerUserId != row.OwnerUserId && row.DecisionKey is "approved" or "rejected";
    private static AiKnowledgeAnswerFeedbackReviewItem Map(AiKnowledgeAnswerFeedbackReviewRecord row) => new(row.FeedbackId, row.RatingKey, row.ReasonKey, CitationCount(row), row.FeedbackVersion, IsCurrentReview(row) ? row.DecisionKey! : "pending_review", row.ReviewVersion ?? 0);
    private async Task<Dictionary<string, object?>?> CurrentParametersAsync(IReadOnlyList<AiKnowledgeAnswerFeedbackReviewRecord> rows,
        IReadOnlyDictionary<Guid, AiKnowledgeAnswerLineage> proven, Guid actor, CancellationToken token)
    {
        var parameters = await history.CurrentReviewParametersAsync(actor, token).ConfigureAwait(false);
        if (parameters is null) return null;
        var bindings = rows.Where(row => proven.ContainsKey(row.MessageId)).SelectMany(row => proven[row.MessageId].Dependencies
            .Select(dependency => new AiKnowledgeAnswerFeedbackReviewBinding(row.FeedbackId, row.MessageId, dependency))).ToArray();
        parameters["ReviewBindingsJson"] = JsonSerializer.Serialize(bindings, AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerFeedbackReviewBindingArray);
        parameters["DependenciesJson"] = JsonSerializer.Serialize(bindings.Select(binding => binding.Dependency).Distinct().ToArray(), AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerDependencyArray);
        return parameters;
    }
    private Task<IReadOnlyList<AiKnowledgeAnswerFeedbackReviewRecord>> CurrentAsync(AiChatScope scope, Dictionary<string, object?> parameters, CancellationToken token) => query.QueryAsync<AiKnowledgeAnswerFeedbackReviewRecord>(AiKnowledgeAnswerFeedbackReviewSql.CurrentFor(database.Value.Provider, scope.TenantId.HasValue), parameters, token);
    private Task<IReadOnlyList<AiKnowledgeAnswerFeedbackReviewRecord>> PageAsync(AiChatScope scope, Dictionary<string, object?> parameters, bool export, CancellationToken token) => query.QueryAsync<AiKnowledgeAnswerFeedbackReviewRecord>(AiKnowledgeAnswerFeedbackReviewSql.PageFor(database.Value.Provider, scope.TenantId.HasValue, export), parameters, token);
    private Task<AiKnowledgeAnswerFeedbackReviewRecord?> FindAsync(AiChatScope scope, Dictionary<string, object?> parameters, CancellationToken token) => query.QuerySingleOrDefaultAsync<AiKnowledgeAnswerFeedbackReviewRecord>(AiKnowledgeAnswerFeedbackReviewSql.FindFor(scope.TenantId.HasValue), parameters, token);
    private async Task<bool> AuthorizedAsync(string permission, Guid actor, CancellationToken token, Guid? originalSessionId = null)
    {
        token.ThrowIfCancellationRequested(); if (actor == Guid.Empty) return false;
        foreach (var code in new[] { AiChatPermissions.Read, permission })
        { var current = await authorization.AuthorizeAsync(code, token).ConfigureAwait(false); if (current?.UserId != actor || current.TenantId != tenant.Id
                || (originalSessionId.HasValue && current.SessionId != originalSessionId.Value)) return false; }
        return true;
    }
    private bool SameScope(AiChatScope scope) => AiChatScope.Resolve(tenant) == scope;
    private void EnsureOutsideTransaction() { if (transactions.HasTransaction) throw new InvalidOperationException("审核授权不能加入已有事务。"); }
    private static Result<T> Failure<T>(ErrorType type) => Result<T>.Failure(new(type == ErrorType.Forbidden ? "authorization.permission_denied" : type == ErrorType.Conflict ? AiKnowledgeErrorCodes.FeedbackConflict : type == ErrorType.Validation ? AiKnowledgeErrorCodes.FeedbackInvalid : AiKnowledgeErrorCodes.FeedbackNotFound, "The answer feedback is unavailable or invalid.", type));
}
