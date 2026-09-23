using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;

internal static class TenantEntitlementSql
{
    public static readonly SqlStatement ListCatalog = new(
        "tenancy.entitlements.list_catalog",
        """
        SELECT Id, Code, Name, Description, EntitlementType, IsActive, Version
        FROM fn_tenancy_entitlement_catalog
        ORDER BY Code, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindCatalogById = new(
        "tenancy.entitlements.find_catalog_by_id",
        """
        SELECT Id, Code, Name, Description, EntitlementType, IsActive, Version
        FROM fn_tenancy_entitlement_catalog
        WHERE Id = @EntitlementId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindCatalogByCode = new(
        "tenancy.entitlements.find_catalog_by_code",
        """
        SELECT Id, Code, Name, Description, EntitlementType, IsActive, Version
        FROM fn_tenancy_entitlement_catalog
        WHERE Code = @Code
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertCatalog = new(
        "tenancy.entitlements.insert_catalog",
        """
        INSERT INTO fn_tenancy_entitlement_catalog
            (Id, Code, Name, Description, EntitlementType, IsActive, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @Code, @Name, @Description, @EntitlementType, 1, @CreatedAtUtc, @UpdatedAtUtc, 1)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListBindings = new(
        "tenancy.entitlements.list_bindings",
        """
        SELECT binding.Id,
               binding.TenantId,
               binding.EntitlementId,
               catalog.Code AS EntitlementCode,
               catalog.Name AS EntitlementName,
               binding.EffectiveFromUtc,
               binding.EffectiveToUtc,
               binding.SourcePackageId,
               binding.Version
        FROM fn_tenancy_tenant_entitlement_binding AS binding
        INNER JOIN fn_tenancy_entitlement_catalog AS catalog
            ON catalog.Id = binding.EntitlementId
        WHERE binding.TenantId = @TenantId
        ORDER BY binding.EffectiveFromUtc DESC, binding.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertBinding = new(
        "tenancy.entitlements.insert_binding",
        """
        INSERT INTO fn_tenancy_tenant_entitlement_binding
            (Id, TenantId, EntitlementId, EffectiveFromUtc, EffectiveToUtc,
             SourcePackageId, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @EntitlementId, @EffectiveFromUtc, @EffectiveToUtc,
             @SourcePackageId, @CreatedAtUtc, @UpdatedAtUtc, 1)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetEnforcementPhase = new(
        "tenancy.settings.get_enforcement_phase",
        """
        SELECT EntitlementEnforcementPhase, Version
        FROM fn_tenancy_settings
        WHERE Id = @SettingsId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateEnforcementPhase = new(
        "tenancy.settings.update_enforcement_phase",
        """
        UPDATE fn_tenancy_settings
        SET EntitlementEnforcementPhase = @Phase,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @SettingsId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);
}

internal sealed record TenantEntitlementCatalogRecord(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string EntitlementType,
    bool IsActive,
    int Version);

internal sealed record TenantEntitlementBindingRecord(
    Guid Id,
    Guid TenantId,
    Guid EntitlementId,
    string EntitlementCode,
    string EntitlementName,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    Guid? SourcePackageId,
    int Version);

internal sealed record TenantEntitlementEnforcementRecord(string EntitlementEnforcementPhase, int Version);
