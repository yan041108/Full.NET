using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;

/// <summary>模块内端口适配或空实现；供测试或未启用能力时闭合 DI。</summary>
namespace Full.NET.Modules.Identity.Features.AcceptTenantInvitation;

internal sealed class NullTenantMemberSeatQuotaPort : ITenantMemberSeatQuotaPort
{
    public Task<Result<bool>> TryReserveAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Failure(new Error(
            IdentityErrorCodes.TenantSeatQuotaUnavailable,
            "Tenancy module is not available.",
            ErrorType.BusinessRule)));

    public Task<Result<bool>> ConfirmAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        TryReserveAsync(tenantId, operationId, cancellationToken);

    public Task<Result<bool>> ReleaseAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        TryReserveAsync(tenantId, operationId, cancellationToken);
}