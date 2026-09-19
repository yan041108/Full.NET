using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Persistence;

internal static class TenantSql
{
    public static readonly SqlStatement FindByIdentifier = new(
        "tenancy.find_by_identifier",
        """
        SELECT COUNT(*)
        FROM fn_tenancy_tenant
        WHERE Identifier = @Identifier
        """,
        SqlDataScope.Global);

    // Seeder 只在 Migrator 的可信宿主上下文使用该 Global 查询，并以自然键判断幂等状态。
    public static readonly SqlStatement FindSummaryByIdentifier = new(
        "tenancy.tenant.find_summary_by_identifier",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version
        FROM fn_tenancy_tenant
        WHERE Identifier = @Identifier
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountByDomain = new(
        "tenancy.count_by_domain",
        """
        SELECT COUNT(*)
        FROM fn_tenancy_tenant
        WHERE Domain = @Domain
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindByDomain = new(
        "tenancy.find_by_domain",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE Domain = @Domain
        """,
        SqlDataScope.Global);

    // 按 ID 的 Global 查询只服务于宿主管理员上下文切换，调用方必须先通过权限策略。
    public static readonly SqlStatement FindById = new(
        "tenancy.find_by_explicit_id",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE Id = @TenantId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement GetAvailable = new(
        "tenancy.get_available_for_host_administrator",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE IsActive = 1
        ORDER BY Name, Identifier, Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Insert = new(
        "tenancy.insert",
        """
        INSERT INTO fn_tenancy_tenant
            (Id, Identifier, Name, Domain, IsActive, CreatedAtUtc, Version, DefaultLocale, TenantPackageId,
             LifecycleStatus, ProvisioningStatus, ProvisioningStep)
        VALUES
            (@Id, @Identifier, @Name, @Domain, @IsActive, @CreatedAtUtc, @Version, @DefaultLocale, @TenantPackageId,
             @LifecycleStatus, @ProvisioningStatus, @ProvisioningStep)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetCurrent = new(
        "tenancy.get_current",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE Id = @TenantId AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountHostTenants = new(
        "tenancy.count_host_tenants",
        """
        SELECT COUNT(1)
        FROM fn_tenancy_tenant
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostTenantsSqlServer = new(
        "tenancy.list_host_tenants.sql_server",
        """
        SELECT tenant.Id,
               tenant.Identifier,
               tenant.Name,
               tenant.Domain,
               tenant.IsActive,
               tenant.Version,
               tenant.DefaultLocale,
               tenant.TenantPackageId,
               package.Code AS TenantPackageCode,
               package.Name AS TenantPackageName,
               tenant.LifecycleStatus,
               tenant.OwnerUserId,
               tenant.ProvisioningStatus,
               tenant.ProvisioningStep
        FROM fn_tenancy_tenant AS tenant
        LEFT JOIN fn_tenancy_tenant_package AS package
            ON package.Id = tenant.TenantPackageId
        ORDER BY tenant.Name, tenant.Identifier, tenant.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostTenantsMySql = new(
        "tenancy.list_host_tenants.mysql",
        """
        SELECT tenant.Id,
               tenant.Identifier,
               tenant.Name,
               tenant.Domain,
               tenant.IsActive,
               tenant.Version,
               tenant.DefaultLocale,
               tenant.TenantPackageId,
               package.Code AS TenantPackageCode,
               package.Name AS TenantPackageName,
               tenant.LifecycleStatus,
               tenant.OwnerUserId,
               tenant.ProvisioningStatus,
               tenant.ProvisioningStep
        FROM fn_tenancy_tenant AS tenant
        LEFT JOIN fn_tenancy_tenant_package AS package
            ON package.Id = tenant.TenantPackageId
        ORDER BY tenant.Name, tenant.Identifier, tenant.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostTenantById = new(
        "tenancy.find_host_tenant_by_id",
        """
        SELECT tenant.Id,
               tenant.Identifier,
               tenant.Name,
               tenant.Domain,
               tenant.IsActive,
               tenant.Version,
               tenant.DefaultLocale,
               tenant.TenantPackageId,
               package.Code AS TenantPackageCode,
               package.Name AS TenantPackageName,
               tenant.LifecycleStatus,
               tenant.OwnerUserId,
               tenant.ProvisioningStatus,
               tenant.ProvisioningStep
        FROM fn_tenancy_tenant AS tenant
        LEFT JOIN fn_tenancy_tenant_package AS package
            ON package.Id = tenant.TenantPackageId
        WHERE tenant.Id = @TenantId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement AssignHostTenantPackage = new(
        "tenancy.assign_host_tenant_package",
        """
        UPDATE fn_tenancy_tenant
        SET TenantPackageId = @TenantPackageId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveTenants = new(
        "tenancy.count_active_tenants",
        """
        SELECT COUNT(1)
        FROM fn_tenancy_tenant
        WHERE IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostTenantName = new(
        "tenancy.update_host_tenant_name",
        """
        UPDATE fn_tenancy_tenant
        SET Name = @Name,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DisableHostTenant = new(
        "tenancy.disable_host_tenant",
        """
        UPDATE fn_tenancy_tenant
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement EnableHostTenant = new(
        "tenancy.enable_host_tenant",
        """
        UPDATE fn_tenancy_tenant
        SET IsActive = 1,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND IsActive = 0
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindTenantBrandingById = new(
        "tenancy.tenant_branding.find_by_id",
        """
        SELECT Id AS TenantId,
               SystemTitle,
               LogoFileId,
               ContactPhone,
               ContactEmail,
               ContactAddress,
               Copyright,
               Version
        FROM fn_tenancy_tenant
        WHERE Id = @TenantId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindTenantBrandingCurrent = new(
        "tenancy.tenant_branding.find_current",
        """
        SELECT Id AS TenantId,
               SystemTitle,
               LogoFileId,
               ContactPhone,
               ContactEmail,
               ContactAddress,
               Copyright,
               Version
        FROM fn_tenancy_tenant
        WHERE Id = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateTenantBranding = new(
        "tenancy.tenant_branding.update",
        """
        UPDATE fn_tenancy_tenant
        SET SystemTitle = @SystemTitle,
            ContactPhone = @ContactPhone,
            ContactEmail = @ContactEmail,
            ContactAddress = @ContactAddress,
            Copyright = @Copyright,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateTenantBrandingCurrent = new(
        "tenancy.tenant_branding.update_current",
        """
        UPDATE fn_tenancy_tenant
        SET SystemTitle = @SystemTitle,
            ContactPhone = @ContactPhone,
            ContactEmail = @ContactEmail,
            ContactAddress = @ContactAddress,
            Copyright = @Copyright,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateTenantLogo = new(
        "tenancy.tenant_branding.update_logo",
        """
        UPDATE fn_tenancy_tenant
        SET LogoFileId = @LogoFileId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateTenantLogoCurrent = new(
        "tenancy.tenant_branding.update_logo_current",
        """
        UPDATE fn_tenancy_tenant
        SET LogoFileId = @LogoFileId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ClearTenantLogo = new(
        "tenancy.tenant_branding.clear_logo",
        """
        UPDATE fn_tenancy_tenant
        SET LogoFileId = NULL,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND LogoFileId IS NOT NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ClearTenantLogoCurrent = new(
        "tenancy.tenant_branding.clear_logo_current",
        """
        UPDATE fn_tenancy_tenant
        SET LogoFileId = NULL,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND LogoFileId IS NOT NULL
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement TenantLogoExists = new(
        "tenancy.tenant_branding.logo_exists",
        """
        SELECT CASE
            WHEN EXISTS (
                SELECT 1
                FROM fn_tenancy_tenant
                WHERE Id = @TenantId
                  AND LogoFileId = @FileId)
            THEN 1
            ELSE 0
        END
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement IsTenantLogoReferenced = new(
        "tenancy.tenant_branding.is_logo_referenced",
        """
        SELECT CASE
            WHEN EXISTS (
                SELECT 1
                FROM fn_tenancy_tenant
                WHERE LogoFileId = @FileId)
            THEN 1
            ELSE 0
        END
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateProvisioningStatus = new(
        "tenancy.update_provisioning_status",
        """
        UPDATE fn_tenancy_tenant
        SET ProvisioningStatus = @ProvisioningStatus,
            ProvisioningStep = @ProvisioningStep,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateLifecycleStatus = new(
        "tenancy.update_lifecycle_status",
        """
        UPDATE fn_tenancy_tenant
        SET LifecycleStatus = @LifecycleStatus,
            IsActive = @IsActive,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateOwnerUser = new(
        "tenancy.update_owner_user",
        """
        UPDATE fn_tenancy_tenant
        SET OwnerUserId = @OwnerUserId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);
}
