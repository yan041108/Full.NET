namespace Full.NET.Modules.Tenancy.Contracts;

public static class TenancyTenantQuotaPermissions
{
    public const string Read = "tenancy.tenant_quota.read";
    public const string Manage = "tenancy.tenant_quota.manage";
    public const string Reserve = "tenancy.tenant_quota.reserve";
}

public static class TenantQuotaMetricCodes
{
    public const string IdentitySeats = "identity.seats";
    public const string FilesStorageBytes = "files.storage_bytes";
}

public static class TenantQuotaDefaults
{
    public const string PeriodKey = "default";
    public const long IdentitySeatsLimit = 100;
}

public static class TenantQuotaReservationStatuses
{
    public const string Reserved = "Reserved";
    public const string Confirmed = "Confirmed";
    public const string Released = "Released";
    public const string Expired = "Expired";
}

public sealed record TenantQuotaMetricResponse(
    Guid Id, Guid TenantId, string MetricCode, string PeriodKey,
    long LimitValue, long UsedValue, long ReservedValue, int Version);

public sealed record ReserveTenantQuotaRequest(
    string MetricCode, string OperationId, long Amount, int ExpiresInMinutes = 30);

public sealed record ReserveTenantQuotaResponse(
    Guid ReservationId, Guid TenantId, string MetricCode, string OperationId,
    long Amount, string Status, DateTimeOffset ExpiresAtUtc);

public sealed record ConfirmTenantQuotaRequest(string OperationId);
public sealed record ReleaseTenantQuotaRequest(string OperationId);

public sealed record UpsertTenantQuotaMetricRequest(
    string MetricCode,
    string PeriodKey,
    long LimitValue);

public sealed record ListTenantQuotaMetricsResponse(
    IReadOnlyList<TenantQuotaMetricResponse> Items);

public interface ITenantQuotaReservationService
{
    Task<Abstractions.Results.Result<ReserveTenantQuotaResponse>> ReserveAsync(
        Guid tenantId, ReserveTenantQuotaRequest request,
        CancellationToken cancellationToken = default);

    Task<Abstractions.Results.Result<TenantQuotaMetricResponse>> ConfirmAsync(
        Guid tenantId, ConfirmTenantQuotaRequest request,
        CancellationToken cancellationToken = default);

    Task<Abstractions.Results.Result<TenantQuotaMetricResponse>> ReleaseAsync(
        Guid tenantId, ReleaseTenantQuotaRequest request,
        CancellationToken cancellationToken = default);
}
