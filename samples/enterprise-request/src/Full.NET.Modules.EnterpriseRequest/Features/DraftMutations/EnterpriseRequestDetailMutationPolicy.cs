using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Generated;

namespace Full.NET.Modules.EnterpriseRequest.Features.DraftMutations;

/// <summary>独立明细接口与聚合接口共享主单草稿约束，不允许用改挂父单绕过审批冻结。</summary>
internal sealed class EnterpriseRequestDetailMutationPolicy(IQueryExecutor queries, EnterpriseRequestDraftGuard guard) : IEnterpriseRequestDetailMutationPolicy
{
    public Task<Error?> ValidateCreateAsync(CreateEnterpriseRequestDetailRequest request, Guid actorUserId, Guid? organizationUnitId, CancellationToken cancellationToken) =>
        guard.EnsureDraftAsync(request.RequestId, organizationUnitId, cancellationToken);

    public Task<Error?> ValidateUpdateAsync(Guid id, UpdateEnterpriseRequestDetailRequest request, Guid actorUserId, CancellationToken cancellationToken) =>
        CheckExistingAsync(id, request.RequestId, cancellationToken);

    public Task<Error?> ValidateDeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken) =>
        CheckExistingAsync(id, null, cancellationToken);

    private async Task<Error?> CheckExistingAsync(Guid id, Guid? requestedParentId, CancellationToken token)
    {
        var row = await queries.QuerySingleOrDefaultAsync<EnterpriseRequestDetailRecord>(EnterpriseRequestDetailSql.FindByIdStatement,
            new Dictionary<string, object?> { ["Id"] = id }, token).ConfigureAwait(false);
        if (row is null) return new Error("enterprise_request.enterprise_request_lines.not_found", "The request line was not found.", ErrorType.NotFound);
        if (requestedParentId is not null && requestedParentId != row.RequestId)
            return new Error("enterprise_request.enterprise_request_lines.parent_immutable", "A request line cannot be moved to another request.", ErrorType.Conflict);
        return await guard.EnsureDraftAsync(row.RequestId, row.OrganizationUnitId, token).ConfigureAwait(false);
    }
}
