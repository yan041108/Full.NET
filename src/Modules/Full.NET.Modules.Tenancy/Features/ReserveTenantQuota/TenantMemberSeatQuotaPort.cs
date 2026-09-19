using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;

internal sealed class TenantMemberSeatQuotaPort(
    ITenantQuotaReservationService quotaReservationService) : ITenantMemberSeatQuotaPort
{
    public Task<Result<bool>> TryReserveAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        ReserveCoreAsync(tenantId, operationId, cancellationToken);

    public async Task<Result<bool>> ConfirmAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var result = await quotaReservationService.ConfirmAsync(
                tenantId,
                new ConfirmTenantQuotaRequest(operationId),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(result.Error!);
    }

    public async Task<Result<bool>> ReleaseAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var result = await quotaReservationService.ReleaseAsync(
                tenantId,
                new ReleaseTenantQuotaRequest(operationId),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(result.Error!);
    }

    private async Task<Result<bool>> ReserveCoreAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken)
    {
        var normalizedOperationId = operationId?.Trim() ?? string.Empty;
        if (normalizedOperationId.Length is < 1 or > 128)
        {
            return Result<bool>.Failure(new Error(
                TenancyErrorCodes.QuotaRequestInvalid,
                "Seat quota operation id is invalid.",
                ErrorType.Validation));
        }

        var result = await quotaReservationService.ReserveAsync(
                tenantId,
                new ReserveTenantQuotaRequest(
                    TenantQuotaMetricCodes.IdentitySeats,
                    normalizedOperationId,
                    1),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(result.Error!);
    }
}