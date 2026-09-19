using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantQuota;

internal sealed class TenantQuotaManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<IReadOnlyList<TenantQuotaMetricResponse>>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ListCoreAsync(tenantId, token),
            cancellationToken);

    public Task<Result<TenantQuotaMetricResponse>> UpsertAsync(
        Guid tenantId,
        UpsertTenantQuotaMetricRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpsertCoreAsync(tenantId, request, token),
            cancellationToken);

    private async Task<Result<IReadOnlyList<TenantQuotaMetricResponse>>> ListCoreAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<TenantQuotaMetricRecord>(
                TenantQuotaSql.ListMetricsByTenant,
                Tenancy.Persistence.TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<TenantQuotaMetricResponse>>.Success(
            rows.Select(MapMetric).ToArray());
    }

    private async Task<Result<TenantQuotaMetricResponse>> UpsertCoreAsync(
        Guid tenantId,
        UpsertTenantQuotaMetricRequest request,
        CancellationToken cancellationToken)
    {
        var metricCode = request.MetricCode?.Trim() ?? string.Empty;
        var periodKey = request.PeriodKey?.Trim() ?? string.Empty;
        if (metricCode.Length is < 1 or > 64 || periodKey.Length is < 1 or > 32 || request.LimitValue < 0)
        {
            return Result<TenantQuotaMetricResponse>.Failure(new Error(
                TenancyErrorCodes.QuotaRequestInvalid,
                "Quota metric request is invalid.",
                ErrorType.Validation));
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(
                TenantQuotaSql.FindMetric,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("MetricCode", metricCode),
                    ("PeriodKey", periodKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        if (existing is null)
        {
            var id = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                    TenantQuotaSql.InsertMetric,
                    Tenancy.Persistence.TenancySqlParameters.Create(
                        ("Id", id),
                        ("TenantId", tenantId),
                        ("MetricCode", metricCode),
                        ("PeriodKey", periodKey),
                        ("LimitValue", request.LimitValue),
                        ("CreatedAtUtc", now),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<TenantQuotaMetricResponse>.Success(
                new TenantQuotaMetricResponse(
                    id,
                    tenantId,
                    metricCode,
                    periodKey,
                    request.LimitValue,
                    0,
                    0,
                    1));
        }

        var affected = await commandExecutor.ExecuteAsync(
                TenantQuotaSql.UpdateMetricLimit,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("MetricId", existing.Id),
                    ("LimitValue", request.LimitValue),
                    ("UpdatedAtUtc", now),
                    ("Version", existing.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<TenantQuotaMetricResponse>.Failure(new Error(
                TenancyErrorCodes.QuotaMetricNotFound,
                "The quota metric was not found.",
                ErrorType.Conflict));
        }

        return Result<TenantQuotaMetricResponse>.Success(
            new TenantQuotaMetricResponse(
                existing.Id,
                tenantId,
                metricCode,
                periodKey,
                request.LimitValue,
                existing.UsedValue,
                existing.ReservedValue,
                existing.Version + 1));
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
}
