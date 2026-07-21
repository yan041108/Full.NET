using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class HostCatalogSqlScopeTests
{
    [TestMethod]
    public void List_active_host_menus_is_global_so_tenant_context_can_compose_navigation()
    {
        // 导航投影在 Host/Tenant 请求中都会加载 Host 菜单目录；HostOnly 会在租户上下文中抛 HostContextRequiredException。
        Assert.AreEqual(SqlDataScope.Global, IdentitySql.ListActiveHostMenus.Scope);

        var accessor = new CurrentTenantAccessor();
        accessor.SetTenant(new TenantContext(Guid.CreateVersion7(), "local", "Full.NET Local"));
        SqlScopeGuard.Validate(IdentitySql.ListActiveHostMenus, accessor);
    }

    [TestMethod]
    public void Find_host_user_by_id_is_global_so_tenant_modules_can_validate_host_users()
    {
        // Organization 等模块在租户上下文中通过 IHostUserDirectory 校验 Host 用户；HostOnly 会阻断分配写路径。
        Assert.AreEqual(SqlDataScope.Global, IdentitySql.FindHostUserById.Scope);

        var accessor = new CurrentTenantAccessor();
        accessor.SetTenant(new TenantContext(Guid.CreateVersion7(), "local", "Full.NET Local"));
        SqlScopeGuard.Validate(IdentitySql.FindHostUserById, accessor);
    }
}
