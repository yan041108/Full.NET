using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CrudAuthorizationScopeTests
{
    [TestMethod]
    [DataRow(FullNetCrudDataScope.TenantRequired, true, "Tenant", 2)]
    [DataRow(FullNetCrudDataScope.TenantRequired, false, "Tenant", 4)]
    [DataRow(FullNetCrudDataScope.HostOnly, true, "Host", 2)]
    [DataRow(FullNetCrudDataScope.HostOnly, false, "Host", 4)]
    [DataRow(FullNetCrudDataScope.Global, true, "Host", 2)]
    [DataRow(FullNetCrudDataScope.Global, false, "Host", 4)]
    public void Generated_permissions_follow_tenant_scope_without_broadening_other_scopes(
        FullNetCrudDataScope scope, bool legacy, string expectedScope, int permissionCount)
    {
        var fragment = CrudArtifactGenerator.Generate(CreateSchema(scope, legacy))
            .Single(artifact => artifact.RelativePath.EndsWith("AuthorizationContributor.fragment.cs", StringComparison.Ordinal)).Content;
        Assert.AreEqual(permissionCount, fragment.Split($"AuthorizationScope.{expectedScope}", StringSplitOptions.None).Length - 1);
        var otherScope = expectedScope == "Host" ? "Tenant" : "Host";
        Assert.IsFalse(fragment.Contains($"AuthorizationScope.{otherScope}", StringComparison.Ordinal));
        StringAssert.Contains(fragment, "ProductPermissions.Read");
    }

    [TestMethod]
    public void Previously_integrated_host_block_is_not_silently_rewritten_as_tenant_permissions()
    {
        var schema = CreateSchema(FullNetCrudDataScope.TenantRequired, legacy: true);
        var expected = CrudAuthorizationContributorFragmentGenerator.Generate(schema);
        var previous = expected.Replace("AuthorizationScope.Tenant", "AuthorizationScope.Host", StringComparison.Ordinal);
        var first = AuthorizationContributorIntegrationEditor.Edit(
            AuthorizationContributorIntegrationEditorTests.Source, "CatalogAuthorizationContributor.cs", previous);
        Assert.IsTrue(first.Succeeded);
        var updated = AuthorizationContributorIntegrationEditor.Edit(
            first.DesiredContent, "CatalogAuthorizationContributor.cs", expected);
        Assert.IsFalse(updated.Succeeded);
        Assert.IsFalse(updated.Changed);
        Assert.AreEqual(first.DesiredContent, updated.DesiredContent);
    }

    private static FullNetCrudSchema CreateSchema(FullNetCrudDataScope scope, bool legacy)
    {
        var columns = FullNetCrudSchemaTests.CreateProductSchema().Columns
            .Where(column => scope == FullNetCrudDataScope.TenantRequired || column.DatabaseName != "TenantId")
            .Where(column => legacy || column.DatabaseName != "CreatedAtUtc").ToArray();
        return legacy
            ? FullNetCrudSchema.CreateProject("acme", "catalog", "product", "acme_catalog_product",
                "Acme.Modules.Catalog", "Product", "products", "products", scope, hasVersion: true, columns)
            : FullNetCrudSchema.CreateProject("acme", "catalog", "product", "acme_catalog_product",
                "Acme.Modules.Catalog", "Product", "products", "products", scope,
                FullNetCrudEntityCapabilities.FromLegacy(hasVersion: true), columns);
    }
}
