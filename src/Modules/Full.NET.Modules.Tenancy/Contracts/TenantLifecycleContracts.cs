namespace Full.NET.Modules.Tenancy.Contracts;

public static class TenancyTenantLifecyclePermissions
{
    public const string Read = "tenancy.tenant_lifecycle.read";
    public const string Suspend = "tenancy.tenant_lifecycle.suspend";
    public const string Reactivate = "tenancy.tenant_lifecycle.reactivate";
    public const string Close = "tenancy.tenant_lifecycle.close";
    public const string TransferOwnership = "tenancy.tenant_lifecycle.transfer_ownership";
}

public static class TenantLifecycleStatuses
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Closing = "Closing";
    public const string Closed = "Closed";
}

public static class TenantProvisioningStatuses
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public static class TenantProvisioningSteps
{
    public const string CreatingTenant = "CreatingTenant";
    public const string BindingOwner = "BindingOwner";
    public const string Activating = "Activating";
}

public sealed record SuspendTenantRequest(int Version);
public sealed record ReactivateTenantRequest(int Version);
public sealed record CloseTenantRequest(int Version);
public sealed record TransferTenantOwnershipRequest(Guid NewOwnerUserId, int Version);
