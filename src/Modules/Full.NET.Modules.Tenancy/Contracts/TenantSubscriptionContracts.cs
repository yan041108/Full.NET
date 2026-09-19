namespace Full.NET.Modules.Tenancy.Contracts;

public static class TenancyTenantSubscriptionPermissions
{
    public const string Read = "tenancy.tenant_subscriptions.read";
    public const string Manage = "tenancy.tenant_subscriptions.manage";
}

public static class TenantSubscriptionStatuses
{
    public const string Trial = "Trial";
    public const string Active = "Active";
    public const string PastDue = "PastDue";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";
}

public sealed record TenantSubscriptionResponse(
    Guid Id,
    Guid TenantId,
    Guid? PackageId,
    string Status,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset CurrentPeriodStartUtc,
    DateTimeOffset CurrentPeriodEndUtc,
    DateTimeOffset? CancelledAtUtc,
    int Version);

public sealed record CreateTenantSubscriptionRequest(
    Guid? PackageId,
    string Status,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset CurrentPeriodStartUtc,
    DateTimeOffset CurrentPeriodEndUtc);

public sealed record CancelTenantSubscriptionRequest(int Version);
