using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>显式业务策略必须进入构造闭包和三个命令分支，默认 Schema 不引入空策略依赖。</summary>
[TestClass]
public sealed class CrudMutationPolicyTests
{
    [TestMethod]
    public void Mutation_policy_requires_explicit_scope_and_version()
    {
        var schema = FullNetCrudSchemaTests.CreateProductSchema();
        FullNetCrudSchema Create(FullNetCrudDataScope scope, bool version) => FullNetCrudSchema.CreateProject(schema.OwnerKey, schema.ModuleKey,
            schema.EntityKey, schema.DatabaseTableName, schema.RootNamespace, schema.ClrTypeName, schema.ApiResourceName, schema.PermissionResourceName,
            scope, new FullNetCrudEntityCapabilities(FullNetCrudDeleteMode.HardDelete, false, false, false, version, FullNetCrudOwnershipMode.None, true),
            schema.Columns.Where(column => column.DatabaseName != "CreatedAtUtc").ToArray());
        Assert.AreEqual("entityCapabilities", Assert.ThrowsExactly<ArgumentException>(() => Create(FullNetCrudDataScope.TenantRequired, false)).ParamName);
        Assert.AreEqual("dataScope", Assert.ThrowsExactly<ArgumentException>(() => Create(FullNetCrudDataScope.Unspecified, true)).ParamName);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Explicit_policy_is_required_only_when_enabled_and_precedes_SQL(bool enabled)
    {
        var original = FullNetCrudSchemaTests.CreateProductSchema();
        var schema = FullNetCrudSchema.CreateProject(original.OwnerKey, original.ModuleKey, original.EntityKey,
            original.DatabaseTableName, original.RootNamespace, original.ClrTypeName, original.ApiResourceName,
            original.PermissionResourceName, FullNetCrudDataScope.TenantRequired,
            new FullNetCrudEntityCapabilities(FullNetCrudDeleteMode.HardDelete, false, false, false, true, FullNetCrudOwnershipMode.None, enabled),
            original.Columns.Where(column => column.DatabaseName != "CreatedAtUtc").ToArray());
        var artifacts = CrudArtifactGenerator.Generate(schema);
        var feature = artifacts.Single(item => item.RelativePath == "backend/ProductFeature.g.cs").Content;
        Assert.AreEqual(enabled, feature.Contains("IProductMutationPolicy mutationPolicy", StringComparison.Ordinal));
        foreach (var operation in new[] { "Create", "Update", "Delete" })
        {
            var call = feature.IndexOf($"mutationPolicy.Validate{operation}Async", StringComparison.Ordinal);
            if (!enabled) { Assert.AreEqual(-1, call); continue; }
            Assert.IsTrue(call > 0);
            var write = feature.IndexOf("await commandExecutor.ExecuteAsync(", call, StringComparison.Ordinal);
            Assert.IsTrue(write > call);
        }
        var report = artifacts.Single(item => item.RelativePath.EndsWith(".generation.json", StringComparison.Ordinal)).Content;
        Assert.AreEqual(enabled, report.Contains("\"hasMutationPolicy\": true", StringComparison.Ordinal));
    }
}
