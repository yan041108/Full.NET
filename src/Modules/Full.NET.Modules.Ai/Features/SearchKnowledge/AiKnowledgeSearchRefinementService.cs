using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.SearchKnowledge;

/// <summary>受控实验串行使用原收费边界；所有排名绑定同一完整来源，不保存答案或替换原查询。</summary>
internal sealed class AiKnowledgeSearchRefinementService(IQueryExecutor query, ICurrentTenant tenant, IDataTransactionState transactions,
    AiKnowledgePermissionGuard permissions, AiKnowledgeRetrievalService retrieval, AiKnowledgePaidAnswerService paid)
{
    internal static readonly string[] RequiredPermissions = [.. AiKnowledgeSearchService.ReadPermissions, AiKnowledgeSearchPermissions.Compare, AiKnowledgeSearchPermissions.Refine];
    internal async Task<Result<AiKnowledgeSearchRefinementResponse>> RefineAsync(RefineAiKnowledgeSearchRequest request, Guid actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("搜索实验不能加入已有事务。");
        Guid[] bases; AiKnowledgeSearchAnswerScope scope;
        try
        {
            if (request is null || actor == Guid.Empty || request.EmbeddingModelConfigId == Guid.Empty || request.ProcessingModelConfigId == Guid.Empty
                || request.ProcessingModelVersion < 1 || request.Limit is < 1 or > 8 || request.Mode is not ("rewrite" or "rerank" or "rewrite_rerank")) throw new InvalidDataException();
            _ = AiKnowledgeKeywordRanker.Terms(request.Query, token); bases = AiKnowledgeRetrievalService.FreezeSelection(request.KnowledgeBaseIds, token);
            scope = AiKnowledgeSearchAnswerSelection.Freeze(request.SearchScope, bases, token) ?? throw new InvalidDataException();
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentNullException) { return Fail(AiKnowledgeErrorCodes.SearchInputInvalid, ErrorType.Validation); }
        var entryTenant = tenant.Id; var entryScope = AiKnowledgeScope.IsTenant(tenant);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(6)); var work = deadline.Token;
        try
        {
            string[] required = request.IncludeDiagnostics ? [.. RequiredPermissions, AiKnowledgeSearchPermissions.Diagnostics] : RequiredPermissions;
            if (!await permissions.IsAuthorizedAsync(required, actor, work).ConfigureAwait(false)) throw new UnauthorizedAccessException(); Check();
            var embedding = await Model(request.EmbeddingModelConfigId).ConfigureAwait(false); Check();
            var processing = await Model(request.ProcessingModelConfigId).ConfigureAwait(false); Check();
            if (embedding is null || processing is null) return Fail(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict);
            if (embedding.Version != scope.EmbeddingModelVersion || processing.Version != request.ProcessingModelVersion)
                return Fail(AiKnowledgeErrorCodes.SearchSourceChanged, ErrorType.Conflict);
            var preflight = await paid.BeginRefinementAsync(bases, actor, embedding, processing, scope, request.IncludeDiagnostics, work).ConfigureAwait(false); Check();
            var operations = new List<AiKnowledgeSearchRefinementOperation>();
            var originalEmbedding = await paid.EmbedRefinementAsync(preflight, request.Query, "original_embedding", work).ConfigureAwait(false); Check(); operations.Add(originalEmbedding.Operation);
            var original = await retrieval.RetrieveRefinementAsync(preflight, request.Query, originalEmbedding.Vector, work).ConfigureAwait(false); Check();
            await paid.RevalidateRefinementDeliveryAsync(preflight, original, work).ConfigureAwait(false); Check();
            var current = original; var effective = request.Query; string? refused = null;
            AiKnowledgeRetrievalSnapshot? rewritten = null; AiKnowledgeRefinementRerank? reranked = null;
            if (request.Mode != "rerank")
            {
                var reply = await paid.RewriteRefinementAsync(preflight, request.Query, work).ConfigureAwait(false); Check(); operations.Add(reply.Operation);
                if (reply.Refused) refused = "rewrite";
                else
                {
                    effective = reply.Query;
                    var rewrittenEmbedding = await paid.EmbedRefinementAsync(preflight, effective, "rewritten_embedding", work).ConfigureAwait(false); Check(); operations.Add(rewrittenEmbedding.Operation);
                    rewritten = await retrieval.RetrieveRefinementAsync(preflight, effective, rewrittenEmbedding.Vector, work).ConfigureAwait(false); Check();
                    await paid.RevalidateRefinementDeliveryAsync(preflight, rewritten, work).ConfigureAwait(false); Check(); current = rewritten;
                }
            }
            if (request.Mode != "rewrite" && refused is null)
            {
                reranked = await paid.RerankRefinementAsync(preflight, effective, current.Comparison!.Vector, work).ConfigureAwait(false); Check(); operations.Add(reranked.Operation);
                if (reranked.Refused) refused = "rerank";
            }
            if (operations.Select(operation => operation.OperationId).Distinct().Count() != operations.Count) throw new InvalidDataException();
            var documents = await retrieval.ReadSearchDocumentsAsync(current, work).ConfigureAwait(false); Check();
            var sources = Array.AsReadOnly(preflight.Sources.Select(row => Source(row, preflight)).ToArray());
            var response = new AiKnowledgeSearchRefinementResponse("controlled_refinement_v1", request.Mode, request.Query, effective, processing.Id, processing.Version, refused, sources,
                Rank(original.Comparison!.Keyword, AiKnowledgeKeywordRanker.Strategy, "keyword_occurrences"), Rank(original.Comparison.Vector, "vector_cosine_v1", "cosine_similarity"),
                rewritten is null ? null : Rank(rewritten.Comparison!.Keyword, AiKnowledgeKeywordRanker.Strategy, "keyword_occurrences"),
                rewritten is null ? null : Rank(rewritten.Comparison!.Vector, "vector_cosine_v1", "cosine_similarity"),
                reranked is null || reranked.Refused ? null : Rank(reranked.Ranked, "model_order_prefix_v1", "ordinal_priority", reranked.HasMore), reranked?.CandidateCount,
                operations.AsReadOnly(), request.IncludeDiagnostics ? new(sources.Count, current.CandidateCount, "separate_keyword_cosine_and_model_order") : null);
            await paid.RevalidateRefinementDeliveryAsync(preflight, original, work).ConfigureAwait(false);
            await paid.RevalidateRefinementDeliveryAsync(preflight, current, work).ConfigureAwait(false); Check();
            return Result<AiKnowledgeSearchRefinementResponse>.Success(response);
            AiKnowledgeSearchComparisonRanking Rank(IReadOnlyList<AiKnowledgeRankedChunk> rows, string strategy, string scoreKind, bool more = false) => new(strategy, scoreKind, more || rows.Count > request.Limit,
                Array.AsReadOnly(rows.Take(request.Limit).Select(row =>
                {
                    var chunk = row.Chunk; var document = documents.Single(value => value.Id == chunk.Source.DocumentId);
                    return new AiKnowledgeSearchHit(Source(chunk.Source, current), document.Title, document.Version, chunk.Sequence, chunk.Text, row.Score, chunk.PageNumber,
                        chunk.Correction is null ? chunk.Offset : null, chunk.Correction is null ? chunk.Length : null, chunk.SourceLocation, chunk.DocxLocation,
                        AiKnowledgeCorrectionCitations.Capture(chunk.Correction));
                }).ToArray()));
        }
        catch (AiKnowledgeSearchSourceChangedException) { return Fail(AiKnowledgeErrorCodes.SearchSourceChanged, ErrorType.Conflict); }
        catch (AiBudgetException error) { return Fail(error.Code, error.Code == "ai.budget.operation_conflict" ? ErrorType.Conflict : ErrorType.Forbidden); }
        catch (UnauthorizedAccessException) { return Fail("authorization.permission_denied", ErrorType.Forbidden); }
        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return Fail(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict); }
        catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException or HttpRequestException or ArgumentException)
        { return Fail(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict); }
        Task<AiModelConfigRecord?> Model(Guid id) => query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(entryScope ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            AiSqlParameters.Create(("ModelConfigId", id)), work);
        void Check() { work.ThrowIfCancellationRequested(); if (transactions.HasTransaction || tenant.Id != entryTenant || AiKnowledgeScope.IsTenant(tenant) != entryScope) throw new UnauthorizedAccessException(); }
    }
    private static AiKnowledgeSearchSource Source(AiKnowledgeIndexStatusRecord row, AiKnowledgeRetrievalSnapshot snapshot) => AiKnowledgeSearchSources.Capture(row, snapshot.FaqSources.GetValueOrDefault(row.DocumentId));
    private static Result<AiKnowledgeSearchRefinementResponse> Fail(string code, ErrorType type) => Result<AiKnowledgeSearchRefinementResponse>.Failure(new Error(code, "The controlled search experiment is unavailable.", type));
}
