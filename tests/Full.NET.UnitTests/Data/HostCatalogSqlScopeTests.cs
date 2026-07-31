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
        Assert.AreEqual(
            SqlDataScope.Global,
            IdentitySql.CountActiveHostUserSelections.Scope);
        Assert.AreEqual(
            SqlDataScope.Global,
            IdentitySql.ListActiveHostUserSelectionsSqlServer.Scope);
        Assert.AreEqual(
            SqlDataScope.Global,
            IdentitySql.ListActiveHostUserSelectionsMySql.Scope);
        StringAssert.Contains(
            IdentitySql.ListActiveHostUserSelectionsSqlServer.Text,
            "ScopeKey = 'host'");
        StringAssert.Contains(
            IdentitySql.ListActiveHostUserSelectionsSqlServer.Text,
            "TenantId IS NULL");
        StringAssert.Contains(
            IdentitySql.ListActiveHostUserSelectionsSqlServer.Text,
            "IsActive = 1");
        foreach (var statement in new[]
                 {
                     IdentitySql.CountActiveHostUserSelections,
                     IdentitySql.ListActiveHostUserSelectionsMySql
                 })
        {
            StringAssert.Contains(statement.Text, "ScopeKey = 'host'");
            StringAssert.Contains(statement.Text, "TenantId IS NULL");
            StringAssert.Contains(statement.Text, "IsActive = 1");
        }

        var accessor = new CurrentTenantAccessor();
        accessor.SetTenant(new TenantContext(Guid.CreateVersion7(), "local", "Full.NET Local"));
        SqlScopeGuard.Validate(IdentitySql.FindHostUserById, accessor);
        SqlScopeGuard.Validate(IdentitySql.ListActiveHostUserSelectionsSqlServer, accessor);
        SqlScopeGuard.Validate(IdentitySql.ListActiveHostUserSelectionsMySql, accessor);
    }

    [TestMethod]
    public void Active_role_data_scopes_are_global_so_tenant_queries_can_resolve_host_roles()
    {
        // 数据范围在租户业务查询中解析，但角色目录属于 Host；SQL 必须显式限定 Host 行并允许租户上下文读取。
        Assert.AreEqual(SqlDataScope.Global, IdentitySql.GetUserActiveRoleDataScopes.Scope);
        StringAssert.Contains(IdentitySql.GetUserActiveRoleDataScopes.Text, "roleObject.ScopeKey = 'host'");
        StringAssert.Contains(IdentitySql.GetUserActiveRoleDataScopes.Text, "roleObject.TenantId IS NULL");

        var accessor = new CurrentTenantAccessor();
        accessor.SetTenant(new TenantContext(Guid.CreateVersion7(), "local", "Full.NET Local"));
        SqlScopeGuard.Validate(IdentitySql.GetUserActiveRoleDataScopes, accessor);
    }

    [TestMethod]
    public void Api_key_authentication_statements_are_global_and_keep_explicit_host_filters()
    {
        // API Key 查询发生在认证主体建立之前，必须依靠 SQL 行过滤表达 Host 边界，不能依赖尚不存在的 Host 上下文。
        Assert.AreEqual(SqlDataScope.Global, ApiKeySql.FindForAuthentication.Scope);
        Assert.AreEqual(SqlDataScope.Global, ApiKeySql.TouchLastUsed.Scope);
        StringAssert.Contains(ApiKeySql.FindForAuthentication.Text, "identityUser.ScopeKey = 'host'");
        StringAssert.Contains(ApiKeySql.FindForAuthentication.Text, "identityUser.TenantId IS NULL");
        StringAssert.Contains(ApiKeySql.TouchLastUsed.Text, "identityUser.ScopeKey = 'host'");
        StringAssert.Contains(ApiKeySql.TouchLastUsed.Text, "identityUser.TenantId IS NULL");
    }
}
