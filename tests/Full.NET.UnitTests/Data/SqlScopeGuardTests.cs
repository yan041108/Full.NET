using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class SqlScopeGuardTests
{
    [TestMethod]
    public void Tenant_statement_requires_an_available_tenant_and_tenant_predicate()
    {
        var tenantStatement = new SqlStatement(
            "tenant.read",
            "select * from fn_example where TenantId = @TenantId",
            SqlDataScope.TenantRequired);

        Assert.Throws<TenantContextMissingException>(() =>
            SqlScopeGuard.Validate(tenantStatement, new CurrentTenantAccessor()));

        var accessor = new CurrentTenantAccessor();
        accessor.SetTenant(new TenantContext(Guid.CreateVersion7(), "acme", "Acme"));
        SqlScopeGuard.Validate(tenantStatement, accessor);

        var missingPredicate = tenantStatement with { Text = "select * from fn_example" };
        Assert.Throws<TenantScopeViolationException>(() =>
            SqlScopeGuard.Validate(missingPredicate, accessor));
    }

    [TestMethod]
    public void Host_statement_rejects_a_tenant_context_and_accepts_a_host_context()
    {
        var accessor = new CurrentTenantAccessor();
        accessor.SetTenant(new TenantContext(Guid.CreateVersion7(), "acme", "Acme"));

        var hostStatement = new SqlStatement("host.read", "select 1", SqlDataScope.HostOnly);
        Assert.Throws<HostContextRequiredException>(() =>
            SqlScopeGuard.Validate(hostStatement, accessor));

        accessor.SetHost();
        SqlScopeGuard.Validate(hostStatement, accessor);
    }

    [TestMethod]
    public void Global_statement_does_not_require_a_tenant_context()
    {
        var statement = new SqlStatement("global.read", "select 1", SqlDataScope.Global);

        SqlScopeGuard.Validate(statement, new CurrentTenantAccessor());
    }
}
