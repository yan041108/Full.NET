#if FULLNET_AOT_COMPILE
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Seeding;
using global::Dapper;

namespace Full.NET.Modules.Identity.Persistence;

internal sealed partial class IdentityDapperAotMaterializerContributor
{
    /// <summary>只闭合迁移播种实际读取和写入的类型，不装配 HTTP、认证中间件或后台消费者。</summary>
    internal static void RegisterMigrationMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<IdentityUserRecord>(ReadIdentityUserRecord);
        registrar.Register<IdentityRoleRecord>(ReadIdentityRoleRecord);
        registrar.Register<TenantMemberRecord>(ReadTenantMemberRecord);
        registrar.Register<HostNavigationCatalogSyncService.HostMenuSyncRow>(ReadHostMenuSyncRow);
        registrar.Register<HostNavigationCatalogSyncService.HostMenuRouteNameRow>(ReadHostMenuRouteNameRow);
        registrar.Register<BootstrapAdminTenantMembershipSeedContributor.ActiveTenantSeedRow>(reader =>
            new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        // OIDC 显式开启时，迁移贡献者仍只通过静态 Store 读取签名材料、应用与 Scope。
        registrar.Register<IdentityOidcApplicationRow>(ReadIdentityOidcApplicationRow);
        registrar.Register<IdentityOidcScopeRow>(ReadIdentityOidcScopeRow);
        registrar.Register<IdentityOidcCountRow>(ReadIdentityOidcCountRow);
        registrar.Register<IdentityOidcSigningKeyRow>(reader => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 3), AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 4),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 5), reader.GetBoolean(6), reader.GetInt32(7)));
        registrar.Register<IdentityOidcSigningExpiryRow>(reader => new(AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 0)));
        DapperAotParameterRegistry.Register<IdentityUser>(user =>
        {
            var parameters = new DynamicParameters();
            parameters.Add("Id", user.Id);
            parameters.Add("TenantId", user.TenantId);
            parameters.Add("ScopeKey", user.ScopeKey);
            parameters.Add("Username", user.Username);
            parameters.Add("NormalizedUsername", user.NormalizedUsername);
            parameters.Add("DisplayName", user.DisplayName);
            parameters.Add("PasswordHash", user.PasswordHash);
            parameters.Add("IsActive", user.IsActive);
            parameters.Add("FailedLoginCount", user.FailedLoginCount);
            parameters.Add("LockoutEndUtc", user.LockoutEndUtc);
            parameters.Add("SecurityStamp", user.SecurityStamp);
            parameters.Add("CreatedAtUtc", user.CreatedAtUtc);
            parameters.Add("UpdatedAtUtc", user.UpdatedAtUtc);
            parameters.Add("Version", user.Version);
            parameters.Add("PreferredLocale", user.PreferredLocale);
            parameters.Add("ProfileVersion", user.ProfileVersion);
            parameters.Add("AccountType", user.AccountType);
            parameters.Add("MustChangePassword", user.MustChangePassword);
            parameters.Add("PasswordChangedAtUtc", user.PasswordChangedAtUtc);
            return parameters;
        });
        DapperAotParameterRegistry.Register<InsertIdentityRole>(BindInsertIdentityRole);
        DapperAotParameterRegistry.Register<IdentityUserRole>(BindIdentityUserRole);
        DapperAotParameterRegistry.Register<InsertIdentityNavigation>(BindInsertIdentityNavigation);
        DapperAotParameterRegistry.Register<UpdateIdentitySystemRole>(role =>
        {
            var parameters = new DynamicParameters();
            parameters.Add("Id", role.Id);
            parameters.Add("Name", role.Name);
            parameters.Add("IsSuperAdministrator", role.IsSuperAdministrator);
            parameters.Add("UpdatedAtUtc", role.UpdatedAtUtc);
            parameters.Add("Version", role.Version);
            return parameters;
        });
    }
}
#endif
