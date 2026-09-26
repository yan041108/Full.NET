using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;

internal sealed class TenantFileStorageQuotaPort(
    ITenantQuotaReservationService quotaReservationService) : ITenantFileStorageQuotaPort
{
    public Task<Result<bool>> TryReserveAsync(
        Guid tenantId,
        string operationId,
        long byteCount,
        CancellationToken cancellationToken = default) =>
        ReserveCoreAsync(tenantId, operationId, byteCount, cancellationToken);

    public Task<Result<bool>> ConfirmAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        ConfirmCoreAsync(tenantId, operationId, cancellationToken);

    public Task<Result<bool>> ReleaseAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        ReleaseCoreAsync(tenantId, operationId, cancellationToken);

    private async Task<Result<bool>> ReserveCoreAsync(
        Guid tenantId,
        string operationId,
        long byteCount,
        CancellationToken cancellationToken)
    {
        if (byteCount <= 0)
        {
            return Result<bool>.Failure(new Error(
                TenancyErrorCodes.QuotaRequestInvalid,
                "Storage quota byte count is invalid.",
                ErrorType.Validation));
        }

        var result = await quotaReservationService.ReserveAsync(
                tenantId,
                new ReserveTenantQuotaRequest(
                    TenantQuotaMetricCodes.FilesStorageBytes,
                    operationId,
                    byteCount),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(result.Error!);
    }

    private async Task<Result<bool>> ConfirmCoreAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken)
    {
        var result = await quotaReservationService.ConfirmAsync(
                tenantId,
                new ConfirmTenantQuotaRequest(operationId)
                {
                    MetricCode = TenantQuotaMetricCodes.FilesStorageBytes,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(result.Error!);
    }

    private async Task<Result<bool>> ReleaseCoreAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken)
    {
        var result = await quotaReservationService.ReleaseAsync(
                tenantId,
                new ReleaseTenantQuotaRequest(operationId)
                {
                    MetricCode = TenantQuotaMetricCodes.FilesStorageBytes,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(result.Error!);
    }
}
