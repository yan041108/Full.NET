using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.SearchKnowledge;

/// <summary>公开真实嵌入关系补全；读取和交付均核证完整发布，不调用模型或创建预算。</summary>
internal sealed class AiKnowledgeSearchRelationsService(IQueryExecutor query, ICurrentTenant tenant, IDataTransactionState transactions,
    AiKnowledgePermissionGuard permissions, AiKnowledgeRetrievalService retrieval)
{
    internal async Task<Result<AiKnowledgeSearchRelationsResponse>> ReadAsync(ReadAiKnowledgeSearchRelationsRequest request, Guid actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("真实关系读取不能加入外部事务。");
        if (request is null || request.Source is not { } source || actor == Guid.Empty || request.EmbeddingModelConfigId == Guid.Empty
            || request.EmbeddingModelVersion < 1 || request.AnchorSequence is < 0 or > 255
            || source.KnowledgeBaseId == Guid.Empty || source.DocumentId == Guid.Empty || source.VersionId == Guid.Empty || source.PublicationId == Guid.Empty
            || !AiKnowledgeSearchSources.Valid(source) || source.GenerationId == Guid.Empty || source.KnowledgeBaseVersion < 1 || source.DocumentVersion < 1
            || source.ManifestHash is not { Length: 64 } || source.ManifestHash.Any(character => !Uri.IsHexDigit(character)))
            return Fail(AiKnowledgeErrorCodes.SearchInputInvalid, ErrorType.Validation);
        var entryTenant = tenant.Id; var entryScope = AiKnowledgeScope.IsTenant(tenant);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            if (!await permissions.IsAuthorizedAsync(AiKnowledgeSearchService.ReadPermissions, actor, deadline.Token).ConfigureAwait(false)) throw new UnauthorizedAccessException();
            Check();
            var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(entryScope ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
                AiSqlParameters.Create(("ModelConfigId", request.EmbeddingModelConfigId)), deadline.Token).ConfigureAwait(false);
            Check();
            if (model is null || model.Version != request.EmbeddingModelVersion) return Fail(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict);
            var captured = await retrieval.RetrieveRelationsAsync(source, actor, model, request.AnchorSequence, deadline.Token).ConfigureAwait(false);
            var documents = await retrieval.ReadSearchDocumentsAsync(captured, deadline.Token).ConfigureAwait(false); Check();
            var document = documents.Single(); var related = captured.Relations ?? throw new InvalidDataException();
            var blocks = related.Blocks.Select(chunk => new AiKnowledgeSearchContextBlock(chunk.Sequence, chunk.Text, chunk.PageNumber,
                chunk.Correction is null ? chunk.Offset : null, chunk.Correction is null ? chunk.Length : null, chunk.SourceLocation, chunk.DocxLocation,
                AiKnowledgeCorrectionCitations.Capture(chunk.Correction))).ToArray();
            var response = new AiKnowledgeSearchRelationsResponse(AiKnowledgeSourceRelations.Strategy, source, document.Title, document.Version,
                request.AnchorSequence, related.State, related.Relations, Array.AsReadOnly(blocks));
            await retrieval.RevalidateAsync(captured, deadline.Token).ConfigureAwait(false); Check();
            return Result<AiKnowledgeSearchRelationsResponse>.Success(response);
        }
        catch (AiKnowledgeSearchSourceChangedException) { return Fail(AiKnowledgeErrorCodes.SearchSourceChanged, ErrorType.Conflict); }
        catch (UnauthorizedAccessException) { return Fail("authorization.permission_denied", ErrorType.Forbidden); }
        catch (InvalidDataException) { return Fail(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict); }
        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return Fail(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict); }
        void Check() { deadline.Token.ThrowIfCancellationRequested(); if (transactions.HasTransaction || tenant.Id != entryTenant || AiKnowledgeScope.IsTenant(tenant) != entryScope) throw new UnauthorizedAccessException(); }
    }
    private static Result<AiKnowledgeSearchRelationsResponse> Fail(string code, ErrorType type) =>
        Result<AiKnowledgeSearchRelationsResponse>.Failure(new Error(code, "The published relationships are unavailable.", type));
}
