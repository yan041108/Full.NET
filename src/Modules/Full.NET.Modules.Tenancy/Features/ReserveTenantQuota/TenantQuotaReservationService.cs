using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;

internal sealed class TenantQuotaReservationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator) : ITenantQuotaReservationService
{
    public Task<Result<ReserveTenantQuotaResponse>> ReserveAsync(
        Guid tenantId,
        ReserveTenantQuotaRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ReserveCoreAsync(tenantId, request, token),
            cancellationToken);

    public Task<Result<TenantQuotaMetricResponse>> ConfirmAsync(
        Guid tenantId,
        ConfirmTenantQuotaRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ConfirmCoreAsync(tenantId, request, token),
            cancellationToken);

    public Task<Result<TenantQuotaMetricResponse>> ReleaseAsync(
        Guid tenantId,
        ReleaseTenantQuotaRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ReleaseCoreAsync(tenantId, request, token),
            cancellationToken);

    internal static Result<(string MetricCode, string OperationId, long Amount)> ValidateReserveRequest(
        ReserveTenantQuotaRequest request)
    {
        var metricCode = request.MetricCode?.Trim() ?? string.Empty;
        var operationId = request.OperationId?.Trim() ?? string.Empty;
        if (metricCode.Length is < 1 or > 64 || operationId.Length is < 1 or > 128 || request.Amount <= 0)
        {
            return Result<(string, string, long)>.Failure(new Error(
                TenancyErrorCodes.QuotaRequestInvalid,
                "Quota reservation request is invalid.",
                ErrorType.Validation));
        }

        return Result<(string, string, long)>.Success((metricCode, operationId, request.Amount));
    }

    private async Task<Result<ReserveTenantQuotaResponse>> ReserveCoreAsync(
        Guid tenantId,
        ReserveTenantQuotaRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateReserveRequest(request);
        if (!validation.IsSuccess)
        {
            return Result<ReserveTenantQuotaResponse>.Failure(validation.Error!);
        }

        var (metricCode, operationId, amount) = validation.Value;
        var existingReservation = await queryExecutor.QuerySingleOrDefaultAsync<TenantQuotaReservationRecord>(
                TenantQuotaSql.FindReservationByOperation,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("MetricCode", metricCode),
                    ("OperationId", operationId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existingReservation is not null)
        {
            return Result<ReserveTenantQuotaResponse>.Success(new ReserveTenantQuotaResponse(
                existingReservation.Id,
                tenantId,
                metricCode,
                operationId,
                existingReservation.Amount,
                existingReservation.Status,
                existingReservation.ExpiresAtUtc));
        }

        var metric = await FindMetricAsync(tenantId, metricCode, cancellationToken)
            .ConfigureAwait(false);
        if (metric is null)
        {
            return Result<ReserveTenantQuotaResponse>.Failure(new Error(
                TenancyErrorCodes.QuotaMetricNotFound,
                "The quota metric was not found.",
                ErrorType.NotFound));
        }

        if (metric.LimitValue <= 0)
        {
            return Result<ReserveTenantQuotaResponse>.Failure(new Error(
                TenancyErrorCodes.QuotaExceeded,
                "The tenant quota limit has been exceeded.",
                ErrorType.BusinessRule));
        }

        var now = clock.UtcNow;
        var reservationId = idGenerator.NewId();
        var expiresAt = now.AddMinutes(Math.Clamp(request.ExpiresInMinutes, 1, 1440));
        await commandExecutor.ExecuteAsync(
                TenantQuotaSql.InsertReservation,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("Id", reservationId),
                    ("TenantId", tenantId),
                    ("MetricCode", metricCode),
                    ("OperationId", operationId),
                    ("Amount", amount),
                    ("Status", TenantQuotaReservationStatuses.Reserved),
                    ("ExpiresAtUtc", expiresAt),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        var affected = await commandExecutor.ExecuteAsync(
                TenantQuotaSql.ReserveMetric,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("MetricId", metric.Id),
                    ("Amount", amount),
                    ("UpdatedAtUtc", now),
                    ("Version", metric.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<ReserveTenantQuotaResponse>.Failure(new Error(
                TenancyErrorCodes.QuotaExceeded,
                "The tenant quota limit has been exceeded.",
                ErrorType.BusinessRule));
        }

        return Result<ReserveTenantQuotaResponse>.Success(new ReserveTenantQuotaResponse(
            reservationId,
            tenantId,
            metricCode,
            operationId,
            amount,
            TenantQuotaReservationStatuses.Reserved,
            expiresAt));
    }

    private async Task<Result<TenantQuotaMetricResponse>> ConfirmCoreAsync(
        Guid tenantId,
        ConfirmTenantQuotaRequest request,
        CancellationToken cancellationToken)
    {
        var operationId = request.OperationId?.Trim() ?? string.Empty;
        if (operationId.Length is < 1 or > 128)
        {
            return MetricFailure(TenancyErrorCodes.QuotaRequestInvalid, ErrorType.Validation);
        }

        return await CompleteReservationAsync(
                tenantId,
                operationId,
                TenantQuotaReservationStatuses.Confirmed,
                TenantQuotaSql.ConfirmMetric,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantQuotaMetricResponse>> ReleaseCoreAsync(
        Guid tenantId,
        ReleaseTenantQuotaRequest request,
        CancellationToken cancellationToken)
    {
        var operationId = request.OperationId?.Trim() ?? string.Empty;
        if (operationId.Length is < 1 or > 128)
        {
            return MetricFailure(TenancyErrorCodes.QuotaRequestInvalid, ErrorType.Validation);
        }

        return await CompleteReservationAsync(
                tenantId,
                operationId,
                TenantQuotaReservationStatuses.Released,
                TenantQuotaSql.ReleaseMetric,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantQuotaMetricResponse>> CompleteReservationAsync(
        Guid tenantId,
        string operationId,
        string targetStatus,
        SqlStatement metricUpdate,
        CancellationToken cancellationToken)
    {
        var reservation = await queryExecutor.QuerySingleOrDefaultAsync<TenantQuotaReservationRecord>(
                TenantQuotaSql.FindReservationByTenantOperation,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("OperationId", operationId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (reservation is null || reservation.Status != TenantQuotaReservationStatuses.Reserved)
        {
            return MetricFailure(TenancyErrorCodes.QuotaReservationNotFound, ErrorType.NotFound);
        }

        if (reservation.ExpiresAtUtc <= clock.UtcNow)
        {
            return MetricFailure(TenancyErrorCodes.QuotaReservationNotFound, ErrorType.NotFound);
        }

        var metric = await FindMetricAsync(tenantId, reservation.MetricCode, cancellationToken)
            .ConfigureAwait(false);
        if (metric is null)
        {
            return MetricFailure(TenancyErrorCodes.QuotaMetricNotFound, ErrorType.NotFound);
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                metricUpdate,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("MetricId", metric.Id),
                    ("Amount", reservation.Amount),
                    ("UpdatedAtUtc", now),
                    ("Version", metric.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return MetricFailure(TenancyErrorCodes.QuotaExceeded, ErrorType.BusinessRule);
        }

        var statusAffected = await commandExecutor.ExecuteAsync(
                TenantQuotaSql.UpdateReservationStatus,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("ReservationId", reservation.Id),
                    ("Status", targetStatus),
                    ("UpdatedAtUtc", now),
                    ("Version", reservation.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (statusAffected != 1)
        {
            return MetricFailure(TenancyErrorCodes.QuotaReservationNotFound, ErrorType.Conflict);
        }

        var updatedMetric = await FindMetricAsync(tenantId, reservation.MetricCode, cancellationToken)
            .ConfigureAwait(false);
        if (updatedMetric is null)
        {
            return MetricFailure(TenancyErrorCodes.QuotaMetricNotFound, ErrorType.NotFound);
        }

        return Result<TenantQuotaMetricResponse>.Success(MapMetric(updatedMetric));
    }

    private async Task<TenantQuotaMetricRecord?> FindMetricAsync(
        Guid tenantId,
        string metricCode,
        CancellationToken cancellationToken)
    {
        var periodKeys = new[]
        {
            TenantQuotaDefaults.PeriodKey,
            clock.UtcNow.ToString("yyyy-MM"),
        };
        foreach (var periodKey in periodKeys.Distinct(StringComparer.Ordinal))
        {
            var metric = await queryExecutor.QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(
                    TenantQuotaSql.FindMetric,
                    Tenancy.Persistence.TenancySqlParameters.Create(
                        ("TenantId", tenantId),
                        ("MetricCode", metricCode),
                        ("PeriodKey", periodKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (metric is not null)
            {
                return metric;
            }
        }

        return null;
    }

    private static TenantQuotaMetricResponse MapMetric(TenantQuotaMetricRecord metric) =>
        new(
            metric.Id,
            metric.TenantId,
            metric.MetricCode,
            metric.PeriodKey,
            metric.LimitValue,
            metric.UsedValue,
            metric.ReservedValue,
            metric.Version);

    private static Result<TenantQuotaMetricResponse> MetricFailure(string code, ErrorType type) =>
        Result<TenantQuotaMetricResponse>.Failure(new Error(
            code,
            "Quota operation failed.",
            type));
}
