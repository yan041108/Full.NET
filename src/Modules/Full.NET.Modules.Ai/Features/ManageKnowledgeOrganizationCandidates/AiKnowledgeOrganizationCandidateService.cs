using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeOrganizationCandidates;

/// <summary>先核对资源所有权，再通过最小 Port 读取候选；不启动跨模块事务。</summary>
/// <param name="query">仅查询 AI 自有资源。</param>
/// <param name="tenant">可信 Host/租户范围。</param>
/// <param name="units">Organization 拥有的活动机构目录。</param>
internal sealed class AiKnowledgeOrganizationCandidateService(IQueryExecutor query, ICurrentTenant tenant, ICurrentTenantOrganizationUnitDirectory units)
{
    internal async Task<Result<PagedResult<AiKnowledgeOrganizationCandidateResponse>>> ListAsync(Guid knowledgeBaseId, Guid? documentId,
        Guid actor, int page, int pageSize, CancellationToken token)
    {
        var isTenant = AiKnowledgeScope.IsTenant(tenant);
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty) return NotFound(documentId);
        // 成员的读取资格不允许枚举机构目录，所有者校验优先于分页与跨模块查询。
        if (documentId is { } id)
        {
            var document = await query.QuerySingleOrDefaultAsync<AiKnowledgeDocumentRecord>(isTenant ? AiKnowledgeDocumentSql.FindTenant : AiKnowledgeDocumentSql.FindHost,
                AiSqlParameters.Create(("KnowledgeBaseId", knowledgeBaseId), ("DocumentId", id), ("OwnerUserId", actor)), token).ConfigureAwait(false);
            if (document is null) return NotFound(documentId);
        }
        else
        {
            var knowledge = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(isTenant ? AiKnowledgeSql.FindTenant : AiKnowledgeSql.FindHost,
                AiSqlParameters.Create(("Id", knowledgeBaseId), ("OwnerUserId", actor)), token).ConfigureAwait(false);
            if (knowledge is null) return NotFound(documentId);
        }
        var offset = ((long)page - 1) * pageSize;
        if (page < 1 || pageSize is < 1 or > 100 || offset > 100_000)
            return Result<PagedResult<AiKnowledgeOrganizationCandidateResponse>>.Failure(new Error(AiKnowledgeErrorCodes.PageInvalid,
                "Page size must be 1..100 and offset must not exceed 100000.", ErrorType.Validation));
        // Host 没有租户机构，所有权与输入校验完成后返回空候选。
        if (!isTenant) return Result<PagedResult<AiKnowledgeOrganizationCandidateResponse>>.Success(new([], page, pageSize, 0));
        var candidates = await units.ListActiveUnitsAsync(page, pageSize, token).ConfigureAwait(false);
        return Result<PagedResult<AiKnowledgeOrganizationCandidateResponse>>.Success(new(
            candidates.Items.Select(unit => new AiKnowledgeOrganizationCandidateResponse(unit.Id, unit.Code, unit.Name)).ToArray(),
            candidates.Page, candidates.PageSize, candidates.Total));
    }

    private static Result<PagedResult<AiKnowledgeOrganizationCandidateResponse>> NotFound(Guid? documentId) =>
        Result<PagedResult<AiKnowledgeOrganizationCandidateResponse>>.Failure(new Error(documentId is null
            ? AiKnowledgeErrorCodes.NotFound : AiKnowledgeErrorCodes.DocumentNotFound, "The knowledge resource was not found.", ErrorType.NotFound));
}
