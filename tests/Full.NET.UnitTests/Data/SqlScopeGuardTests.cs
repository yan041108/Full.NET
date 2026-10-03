using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class SqlScopeGuardTests
{
    [TestMethod]
    [DataRow("SELECT 1 -- WHERE TenantId = @TenantId")]
    [DataRow("SELECT 'WHERE TenantId = @TenantId'")]
    [DataRow("SELECT 1 /* WHERE TenantId = @TenantId */")]
    [DataRow("SELECT @TenantId AS TenantId")]
    [DataRow("UPDATE fn_example SET TenantId = @TenantId")]
    [DataRow("SELECT 1 FROM fn_example WHERE @TenantId IS NOT NULL")]
    [DataRow("SELECT 1 FROM fn_example WHERE OtherId = @TenantId")]
    [DataRow("SELECT 1 WHERE TenantId = @TenantIdentifier")]
    [DataRow("SELECT [@TenantId] FROM fn_example")]
    [DataRow("SELECT `@TenantId` FROM fn_example")]
    [DataRow("SELECT 1 FROM fn_example WHERE Id = @TenantId")]
    [DataRow("SELECT 1 FROM fn_example WHERE @TenantId = Id")]
    [DataRow("SELECT 1 FROM fn_example WHERE [Id] = @TenantId")]
    [DataRow("SELECT 1 FROM fn_example WHERE `Id` = @TenantId")]
    [DataRow("UPDATE fn_example SET Name = @Name WHERE Id = @TenantId")]
    [DataRow("SELECT 'fn_tenancy_tenant' FROM fn_example WHERE Id = @TenantId")]
    [DataRow("SELECT 1 FROM fn_example /* FROM fn_tenancy_tenant */ WHERE Id = @TenantId")]
    [DataRow("SELECT 1 FROM fn_example AS fn_tenancy_tenant WHERE Id = @TenantId")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant_archive WHERE Id = @TenantId")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant JOIN fn_example e ON e.Id = @TenantId")]
    [DataRow("SELECT 1 FROM fn_example WHERE EXISTS (SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId)")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE other.Id = @TenantId")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE @TenantId = other.Id")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId OR 1 = 1")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE NOT (Id = @TenantId)")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId UNION SELECT 1 FROM fn_example")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId; SELECT 1 FROM fn_example")]
    [DataRow("UPDATE fn_example SET Name = (SELECT Name FROM fn_tenancy_tenant WHERE Id = @TenantId)")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE (Id = @TenantId) = 0")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE 0 = (Id = @TenantId)")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId AND Name = @Name XOR 1 = 1")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId AND Name = @Name || 1 = 1")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId /*! OR 1 = 1 */")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId /* /* */ OR 1 = 1 # */")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant /*! JOIN fn_example e ON 1 = 1 */ WHERE Id = @TenantId")]
    [DataRow("SELECT '\\'; SELECT 1 FROM fn_example; -- ' FROM fn_tenancy_tenant WHERE Id = @TenantId")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId --x OR 1 = 1")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId AND 1 = 1 EXEC(@Sql)")]
    [DataRow("UPDATE fn_tenancy_tenant SET Name = @Name WHERE Id = @TenantId AND 1 = 1 DROP TABLE fn_example")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId AND IsActive = 1 EXEC(@Sql)")]
    public void Tenant_statement_rejects_parameter_references_outside_a_safe_clause(
        string sql)
    {
        var accessor = new CurrentTenantAccessor();
        accessor.SetTenant(new TenantContext(Guid.CreateVersion7(), "acme", "Acme"));
        var statement = new SqlStatement(
            "tenant.unsafe_reference",
            sql,
            SqlDataScope.TenantRequired,
            SqlTenantBinding.CurrentTenantId);

        Assert.Throws<TenantScopeViolationException>(() =>
            SqlScopeGuard.Validate(statement, accessor));
    }

    [TestMethod]
    [DataRow("SELECT 1 FROM fn_example WHERE TenantId = @TenantId")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId")]
    [DataRow("SELECT 1 FROM fn_example WHERE @TenantId = TenantId")]
    [DataRow("SELECT 1 FROM fn_example WHERE e.TenantId = @TenantId")]
    [DataRow("SELECT 1 FROM fn_example WHERE [TenantId] = @TenantId")]
    [DataRow("SELECT 1 FROM fn_example WHERE `TenantId` = @TenantId")]
    [DataRow("SELECT 1 FROM fn_example e JOIN fn_child c ON c.TenantId = @TenantId")]
    [DataRow("INSERT INTO fn_example (TenantId) VALUES (@TenantId)")]
    [DataRow("SELECT 1 /* @TenantId */ FROM fn_example WHERE TenantId = @TenantId")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE @TenantId = Id")]
    [DataRow("SELECT 1 FROM [dbo].[fn_tenancy_tenant] WHERE [Id] = @TenantId AND IsActive = 1;")]
    [DataRow("SELECT 1 FROM `fn_tenancy_tenant` WHERE `Id` = @TenantId")]
    [DataRow("UPDATE fn_tenancy_tenant SET Name = @Name WHERE Id = @TenantId AND Version = @Version")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE Id = @TenantId AND LogoFileId IS NOT NULL")]
    [DataRow("SELECT 'OR NOT JOIN' FROM fn_tenancy_tenant /* JOIN fn_example */ WHERE Id = @TenantId")]
    [DataRow("SELECT 1 FROM fn_tenancy_tenant WHERE /* 当前可信租户 */ Id = @TenantId /* 必须先隔离 */ AND IsActive = 1")]
    public void Tenant_statement_accepts_parameter_in_a_safe_clause(string sql)
    {
        var accessor = new CurrentTenantAccessor();
        accessor.SetTenant(new TenantContext(Guid.CreateVersion7(), "acme", "Acme"));
        var statement = new SqlStatement(
            "tenant.safe_reference",
            sql,
            SqlDataScope.TenantRequired,
            SqlTenantBinding.CurrentTenantId);

        SqlScopeGuard.Validate(statement, accessor);
    }

    [TestMethod]
    public void Tenant_statement_requires_an_available_tenant_and_trusted_binding()
    {
        var missingBinding = new SqlStatement(
            Name: "tenant.read",
            Text: "select * from fn_example where TenantId = @TenantId",
            Scope: SqlDataScope.TenantRequired);
        missingBinding.Deconstruct(
            Name: out var name,
            Text: out var text,
            Scope: out var scope);

        Assert.AreEqual("tenant.read", name);
        Assert.AreEqual("select * from fn_example where TenantId = @TenantId", text);
        Assert.AreEqual(SqlDataScope.TenantRequired, scope);
        Assert.AreEqual(SqlTenantBinding.None, missingBinding.TenantBinding);

        Assert.Throws<TenantContextMissingException>(() =>
            SqlScopeGuard.Validate(missingBinding, new CurrentTenantAccessor()));

        var accessor = new CurrentTenantAccessor();
        accessor.SetTenant(new TenantContext(Guid.CreateVersion7(), "acme", "Acme"));
        Assert.Throws<TenantScopeViolationException>(() =>
            SqlScopeGuard.Validate(missingBinding, accessor));

        var tenantStatement = missingBinding with
        {
            TenantBinding = SqlTenantBinding.CurrentTenantId,
        };
        SqlScopeGuard.Validate(tenantStatement, accessor);

        var missingPredicate = tenantStatement with
        {
            Text = "select * from fn_example",
        };
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

        var invalidBinding = hostStatement with
        {
            TenantBinding = SqlTenantBinding.CurrentTenantId,
        };
        Assert.Throws<TenantScopeViolationException>(() =>
            SqlScopeGuard.Validate(invalidBinding, accessor));
    }

    [TestMethod]
    public void Global_statement_does_not_require_a_tenant_context()
    {
        var statement = new SqlStatement("global.read", "select 1", SqlDataScope.Global);

        SqlScopeGuard.Validate(statement, new CurrentTenantAccessor());

        var invalidBinding = statement with
        {
            TenantBinding = SqlTenantBinding.CurrentTenantId,
        };
        Assert.Throws<TenantScopeViolationException>(() =>
            SqlScopeGuard.Validate(invalidBinding, new CurrentTenantAccessor()));
    }
}
