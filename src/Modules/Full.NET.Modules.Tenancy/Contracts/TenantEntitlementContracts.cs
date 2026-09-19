namespace Full.NET.Modules.Tenancy.Contracts;

public static class TenancyTenantEntitlementPermissions
{
    public const string Read = "tenancy.tenant_entitlements.read";
    public const string ManageCatalog = "tenancy.tenant_entitlements.manage_catalog";
    public const string ManageBindings = "tenancy.tenant_entitlements.manage_bindings";
    public const string ManageEnforcement = "tenancy.tenant_entitlements.manage_enforcement";
}

public static class TenantEntitlementTypes
{
    public const string Feature = "Feature";
    public const string Limit = "Limit";
}

public static class TenantEntitlementEnforcementPhases
{
    public const string Compatibility = "Compatibility";
    public const string Shadow = "Shadow";
    public const string Enforced = "Enforced";
}

public sealed record TenantEntitlementCatalogResponse(
    Guid Id, string Code, string Name, string? Description,
    string EntitlementType, bool IsActive, int Version);

public sealed record TenantEntitlementBindingResponse(
    Guid Id, Guid TenantId, Guid EntitlementId, string EntitlementCode,
    string EntitlementName, DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc, Guid? SourcePackageId, int Version);

public sealed record CreateTenantEntitlementCatalogRequest(
    string Code, string Name, string? Description, string EntitlementType);

public sealed record CreateTenantEntitlementBindingRequest(
    Guid EntitlementId, DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc, Guid? SourcePackageId);

public sealed record TenantEntitlementEnforcementResponse(string Phase, int Version);
public sealed record UpdateTenantEntitlementEnforcementRequest(string Phase, int Version);
