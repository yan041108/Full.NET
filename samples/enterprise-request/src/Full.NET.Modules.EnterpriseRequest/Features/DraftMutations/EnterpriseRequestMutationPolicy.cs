using Full.NET.Abstractions.Results;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;

namespace Full.NET.Modules.EnterpriseRequest.Features.DraftMutations;

/// <summary>业务规则位于开发者拥有的代码中，再生成 CRUD 时仍保留。</summary>
internal sealed class EnterpriseRequestMutationPolicy(EnterpriseRequestDraftGuard guard) : IEnterpriseRequestMutationPolicy
{
    public Task<Error?> ValidateCreateAsync(CreateEnterpriseRequestRequest request, Guid actorUserId, Guid? organizationUnitId, CancellationToken cancellationToken) =>
        Task.FromResult(request.Status == EnterpriseRequestStatusKeys.Draft ? null : EnterpriseRequestDraftGuard.InvalidStatus());

    public Task<Error?> ValidateUpdateAsync(Guid id, UpdateEnterpriseRequestRequest request, Guid actorUserId, CancellationToken cancellationToken) =>
        request.Status != EnterpriseRequestStatusKeys.Draft ? Task.FromResult<Error?>(EnterpriseRequestDraftGuard.InvalidStatus())
            : guard.EnsureDraftAsync(id, null, cancellationToken);

    public Task<Error?> ValidateDeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken) =>
        guard.EnsureDraftAsync(id, null, cancellationToken);
}
