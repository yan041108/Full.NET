using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.SearchKnowledge;

/// <summary>公开相邻上下文与搜索共用授权证明；不调用模型，不创建预算，不交付失效窗口。</summary>
internal sealed class AiKnowledgeSearchContextService(IQueryExecutor query, ICurrentTenant tenant, IDataTransactionState transactions,
    AiKnowledgePermissionGuard permissions, AiKnowledgeRetrievalService retrieval)
{
    internal async Task<Result<AiKnowledgeSearchContextResponse>> ReadAsync(ReadAiKnowledgeSearchContextRequest request, Guid actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("独立上下文读取不能加入外部事务。");
        if (request is null || request.Source is not { } source || actor == Guid.Empty || request.EmbeddingModelConfigId == Guid.Empty
            || request.EmbeddingModelVersion < 1 || request.AnchorSequence is < 0 or > 255 || request.Radius is < 0 or > 2
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
            var captured = await retrieval.RetrieveContextAsync(source, actor, model, request.AnchorSequence, request.Radius, deadline.Token).ConfigureAwait(false);
            var documents = await retrieval.ReadSearchDocumentsAsync(captured, deadline.Token).ConfigureAwait(false); Check();
            var document = documents.Single(); var window = captured.Context ?? throw new InvalidDataException();
            var blocks = window.Blocks.Select(chunk => new AiKnowledgeSearchContextBlock(chunk.Sequence, chunk.Text, chunk.PageNumber,
                chunk.Correction is null ? chunk.Offset : null, chunk.Correction is null ? chunk.Length : null, chunk.SourceLocation, chunk.DocxLocation,
                AiKnowledgeCorrectionCitations.Capture(chunk.Correction))).ToArray();
            Check();
            return Result<AiKnowledgeSearchContextResponse>.Success(new(AiKnowledgeContextWindow.Strategy, source, document.Title, document.Version,
                request.AnchorSequence, request.Radius, window.HasPrevious, window.HasNext, Array.AsReadOnly(blocks)));
        }
        catch (AiKnowledgeSearchSourceChangedException) { return Fail(AiKnowledgeErrorCodes.SearchSourceChanged, ErrorType.Conflict); }
        catch (UnauthorizedAccessException) { return Fail("authorization.permission_denied", ErrorType.Forbidden); }
        catch (InvalidDataException) { return Fail(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict); }
        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return Fail(AiKnowledgeErrorCodes.SearchUnavailable, ErrorType.Conflict); }
        void Check() { deadline.Token.ThrowIfCancellationRequested(); if (transactions.HasTransaction || tenant.Id != entryTenant || AiKnowledgeScope.IsTenant(tenant) != entryScope) throw new UnauthorizedAccessException(); }
    }
    private static Result<AiKnowledgeSearchContextResponse> Fail(string code, ErrorType type) =>
        Result<AiKnowledgeSearchContextResponse>.Failure(new Error(code, "The published context is unavailable.", type));
}
