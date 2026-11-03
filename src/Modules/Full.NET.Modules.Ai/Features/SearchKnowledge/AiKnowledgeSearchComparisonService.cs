using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.SearchKnowledge;

/// <summary>显式收费比较；两组结果绑定同一原查询、完整候选和来源，失败不交付部分排名。</summary>
internal sealed class AiKnowledgeSearchComparisonService(IQueryExecutor query, ICurrentTenant tenant, IDataTransactionState transactions,
    AiKnowledgePermissionGuard permissions, AiKnowledgeRetrievalService retrieval, AiKnowledgePaidAnswerService paid)
{
    internal static readonly string[] RequiredPermissions = [.. AiKnowledgeSearchService.ReadPermissions, AiKnowledgeSearchPermissions.Compare];
    internal async Task<Result<AiKnowledgeSearchComparisonResponse>> CompareAsync(CompareAiKnowledgeSearchRequest request, Guid actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("策略比较不能加入已有事务。");
        Guid[] ids; AiKnowledgeSearchAnswerScope scope;
        try
        {
            if (request is null || actor == Guid.Empty || request.EmbeddingModelConfigId == Guid.Empty || request.Limit is < 1 or > 20) throw new InvalidDataException();
            _ = AiKnowledgeKeywordRanker.Terms(request.Query, token); ids = AiKnowledgeRetrievalService.FreezeSelection(request.KnowledgeBaseIds, token);
            scope = AiKnowledgeSearchAnswerSelection.Freeze(request.SearchScope, ids, token) ?? throw new InvalidDataException();
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentNullException) { return Fail(AiKnowledgeErrorCodes.SearchInputInvalid, ErrorType.Validation); }
        var entryTenant = tenant.Id; var entryScope = AiKnowledgeScope.IsTenant(tenant);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(3)); var work = deadline.Token;
        try
        {
            string[] required = request.IncludeDiagnostics ? [.. RequiredPermissions, AiKnowledgeSearchPermissions.Diagnostics] : RequiredPermissions;
            if (!await permissions.IsAuthorizedAsync(required, actor, work).ConfigureAwait(false)) throw new UnauthorizedAccessException(); Check();
            var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(entryScope ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
                AiSqlParameters.Create(("ModelConfigId", request.EmbeddingModelConfigId)), work).ConfigureAwait(false); Check();
            if (model is null) return Fail(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict);
            if (model.Version != scope.EmbeddingModelVersion) return Fail(AiKnowledgeErrorCodes.SearchSourceChanged, ErrorType.Conflict);
            var embedding = await paid.EmbedSearchAsync(ids, actor, model, scope, request.Query, request.IncludeDiagnostics, work).ConfigureAwait(false); Check();
            var captured = await retrieval.RetrieveComparisonAsync(ids, actor, model, scope, request.Query, embedding.Vector, request.IncludeDiagnostics, work).ConfigureAwait(false);
            var documents = await retrieval.ReadSearchDocumentsAsync(captured, work).ConfigureAwait(false); Check();
            var comparison = captured.Comparison ?? throw new InvalidDataException();
            var sources = Array.AsReadOnly(captured.Sources.Select(row => Source(row, captured)).ToArray());
            var keyword = Rank(comparison.Keyword, AiKnowledgeKeywordRanker.Strategy, "keyword_occurrences");
            var vector = Rank(comparison.Vector, "vector_cosine_v1", "cosine_similarity");
            var response = new AiKnowledgeSearchComparisonResponse("same_candidates_v1", request.Query, sources, keyword, vector, embedding.Usage,
                request.IncludeDiagnostics ? new(sources.Count, captured.CandidateCount, "separate_keyword_and_cosine") : null);
            // 投影和标题读取之后再核证；预算已结算不表示失效来源仍可展示。
            await paid.RevalidateSearchDeliveryAsync(embedding, captured, work).ConfigureAwait(false); Check();
            return Result<AiKnowledgeSearchComparisonResponse>.Success(response);
            AiKnowledgeSearchComparisonRanking Rank(IReadOnlyList<AiKnowledgeRankedChunk> rows, string strategy, string scoreKind) => new(strategy, scoreKind, rows.Count > request.Limit,
                Array.AsReadOnly(rows.Take(request.Limit).Select(row =>
                {
                    var chunk = row.Chunk; var document = documents.Single(value => value.Id == chunk.Source.DocumentId);
                    return new AiKnowledgeSearchHit(Source(chunk.Source, captured), document.Title, document.Version, chunk.Sequence, chunk.Text, row.Score, chunk.PageNumber,
                        chunk.Correction is null ? chunk.Offset : null, chunk.Correction is null ? chunk.Length : null, chunk.SourceLocation, chunk.DocxLocation,
                        AiKnowledgeCorrectionCitations.Capture(chunk.Correction));
                }).ToArray()));
        }
        catch (AiKnowledgeSearchSourceChangedException) { return Fail(AiKnowledgeErrorCodes.SearchSourceChanged, ErrorType.Conflict); }
        catch (AiBudgetException error) { return Fail(error.Code, error.Code == "ai.budget.operation_conflict" ? ErrorType.Conflict : ErrorType.Forbidden); }
        catch (UnauthorizedAccessException) { return Fail("authorization.permission_denied", ErrorType.Forbidden); }
        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return Fail(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict); }
        catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException or HttpRequestException)
        { return Fail(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict); }
        void Check() { work.ThrowIfCancellationRequested(); if (transactions.HasTransaction || tenant.Id != entryTenant || AiKnowledgeScope.IsTenant(tenant) != entryScope) throw new UnauthorizedAccessException(); }
    }
    private static AiKnowledgeSearchSource Source(AiKnowledgeIndexStatusRecord row, AiKnowledgeRetrievalSnapshot snapshot) => AiKnowledgeSearchSources.Capture(row, snapshot.FaqSources.GetValueOrDefault(row.DocumentId));
    private static Result<AiKnowledgeSearchComparisonResponse> Fail(string code, ErrorType type) => Result<AiKnowledgeSearchComparisonResponse>.Failure(new Error(code, "The comparison could not be delivered.", type));
}
