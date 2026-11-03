using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.SearchKnowledge;

/// <summary>独立的无模型调用搜索；资源证明与问答复用，响应前核验全部实际来源。</summary>
internal sealed class AiKnowledgeSearchService(IQueryExecutor query, ICurrentTenant tenant, IDataTransactionState transactions,
    AiKnowledgePermissionGuard permissions, AiKnowledgeRetrievalService retrieval)
{
    internal static readonly string[] ReadPermissions = [AiKnowledgeSearchPermissions.Read, AiKnowledgePermissions.Read,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];

    internal async Task<Result<AiKnowledgeSearchResponse>> SearchAsync(SearchAiKnowledgeRequest request, Guid actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("独立搜索不能加入外部事务。");
        Guid[] knowledgeIds; Guid[] documentIds; AiKnowledgeSearchSource[]? expected;
        try
        {
            if (request is null || actor == Guid.Empty || request.EmbeddingModelConfigId == Guid.Empty || request.EmbeddingModelVersion < 1
                || request.Limit is < 1 or > 20 || request.DocumentIds is null || request.DocumentIds.Count > 8) throw new InvalidDataException();
            _ = AiKnowledgeKeywordRanker.Terms(request.Query, token);
            knowledgeIds = AiKnowledgeRetrievalService.FreezeSelection(request.KnowledgeBaseIds, token);
            documentIds = request.DocumentIds.Count == 0 ? [] : AiKnowledgeRetrievalService.FreezeSelection(request.DocumentIds, token);
            if (request.ExpectedSources is { Count: < 1 or > 8 }) throw new InvalidDataException();
            expected = request.ExpectedSources?.ToArray();
            if (expected is not null && (expected.Any(row => !AiKnowledgeSearchSources.Valid(row) || row is null || row.KnowledgeBaseId == Guid.Empty || row.DocumentId == Guid.Empty || row.VersionId == Guid.Empty
                    || row.PublicationId == Guid.Empty || row.GenerationId == Guid.Empty || row.KnowledgeBaseVersion < 1 || row.DocumentVersion < 1
                    || row.ManifestHash is not { Length: 64 } || row.ManifestHash.Any(character => !Uri.IsHexDigit(character)))
                || expected.Select(row => row.DocumentId).Distinct().Count() != expected.Length)) throw new InvalidDataException();
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentNullException)
        { return Failure(AiKnowledgeErrorCodes.SearchInputInvalid, ErrorType.Validation); }
        var entryTenant = tenant.Id; var entryScope = AiKnowledgeScope.IsTenant(tenant);
        string[] required = request.IncludeDiagnostics ? [.. ReadPermissions, AiKnowledgeSearchPermissions.Diagnostics] : ReadPermissions;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            if (!await permissions.IsAuthorizedAsync(required, actor, deadline.Token).ConfigureAwait(false)) throw new UnauthorizedAccessException();
            CheckScope();
            var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(entryScope ? AiModelConfigSql.FindAvailableForTenantChat
                : AiModelConfigSql.FindAvailableForHostChat, AiSqlParameters.Create(("ModelConfigId", request.EmbeddingModelConfigId)), deadline.Token).ConfigureAwait(false);
            CheckScope();
            if (model is null || model.Version != request.EmbeddingModelVersion) return Failure(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict);
            var captured = await retrieval.RetrieveKeywordAsync(knowledgeIds, documentIds, actor, model, request.Query, request.IncludeDiagnostics, deadline.Token).ConfigureAwait(false);
            var sources = captured.Sources.Select(row => Source(row, captured)).ToArray();
            if (expected is not null && !sources.OrderBy(row => row.DocumentId).SequenceEqual(expected.OrderBy(row => row.DocumentId)))
                return Failure(AiKnowledgeErrorCodes.SearchSourceChanged, ErrorType.Conflict);
            var documents = await retrieval.ReadSearchDocumentsAsync(captured, deadline.Token).ConfigureAwait(false);
            CheckScope();
            var hits = captured.Chunks.Take(request.Limit).Select(row => new AiKnowledgeSearchHit(Source(row.Chunk.Source, captured),
                documents.Single(document => document.Id == row.Chunk.Source.DocumentId).Title,
                documents.Single(document => document.Id == row.Chunk.Source.DocumentId).Version, row.Chunk.Sequence, row.Chunk.Text,
                row.Score, row.Chunk.PageNumber, row.Chunk.Correction is null ? row.Chunk.Offset : null,
                row.Chunk.Correction is null ? row.Chunk.Length : null, row.Chunk.SourceLocation, row.Chunk.DocxLocation, row.Chunk.Correction)).ToArray();
            return Result<AiKnowledgeSearchResponse>.Success(new(AiKnowledgeKeywordRanker.Strategy, Array.AsReadOnly(sources), captured.Chunks.Count,
                Array.AsReadOnly(hits), request.IncludeDiagnostics ? new(sources.Length, captured.CandidateCount, "keyword_occurrences") : null));
        }
        catch (UnauthorizedAccessException) { return Failure("authorization.permission_denied", ErrorType.Forbidden); }
        catch (InvalidDataException) { return Failure(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict); }
        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return Failure(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict); }

        void CheckScope()
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (transactions.HasTransaction || tenant.Id != entryTenant || AiKnowledgeScope.IsTenant(tenant) != entryScope) throw new UnauthorizedAccessException();
        }
    }
    private static AiKnowledgeSearchSource Source(AiKnowledgeIndexStatusRecord row, AiKnowledgeRetrievalSnapshot snapshot) => AiKnowledgeSearchSources.Capture(row, snapshot.FaqSources.GetValueOrDefault(row.DocumentId));
    private static Result<AiKnowledgeSearchResponse> Failure(string code, ErrorType type) =>
        Result<AiKnowledgeSearchResponse>.Failure(new Error(code, "The current knowledge search could not be delivered.", type));
}
