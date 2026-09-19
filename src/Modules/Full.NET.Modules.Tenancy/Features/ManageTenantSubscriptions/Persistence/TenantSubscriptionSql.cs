using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;

internal static class TenantSubscriptionSql
{
    public static readonly SqlStatement ListByTenant = new(
        "tenancy.subscriptions.list_by_tenant",
        @"
        SELECT Id, TenantId, PackageId, Status, TrialEndsAtUtc,
               CurrentPeriodStartUtc, CurrentPeriodEndUtc, CancelledAtUtc, Version
        FROM fn_tenancy_tenant_subscription
        WHERE TenantId = @TenantId
        ORDER BY CurrentPeriodStartUtc DESC, Id
        ",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "tenancy.subscriptions.insert",
        @"
        INSERT INTO fn_tenancy_tenant_subscription
            (Id, TenantId, PackageId, Status, TrialEndsAtUtc,
             CurrentPeriodStartUtc, CurrentPeriodEndUtc, CancelledAtUtc,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @PackageId, @Status, @TrialEndsAtUtc,
             @CurrentPeriodStartUtc, @CurrentPeriodEndUtc, NULL,
             @CreatedAtUtc, @UpdatedAtUtc, 1)
        ",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Cancel = new(
        "tenancy.subscriptions.cancel",
        @"
        UPDATE fn_tenancy_tenant_subscription
        SET Status = @Status,
            CancelledAtUtc = @CancelledAtUtc,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId = @TenantId
          AND Version = @Version
        ",
        SqlDataScope.HostOnly);
}

internal sealed record TenantSubscriptionRecord(
    Guid Id,
    Guid TenantId,
    Guid? PackageId,
    string Status,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset CurrentPeriodStartUtc,
    DateTimeOffset CurrentPeriodEndUtc,
    DateTimeOffset? CancelledAtUtc,
    int Version);
