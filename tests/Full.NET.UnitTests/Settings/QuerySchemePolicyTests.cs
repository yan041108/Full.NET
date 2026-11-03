using System.Text.Json;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageQuerySchemes;
using Full.NET.Modules.Settings.Serialization;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Settings.Persistence;
using System.Reflection;

namespace Full.NET.UnitTests.Settings;

[TestClass]
public sealed class QuerySchemePolicyTests
{
    private static readonly QuerySchemeDefinition Definition = new("test.orders", 2, "test.orders.read", false, true,
        [new("title", 200), new("status", 32)], ["id", "title"], ["title", "status"]);
    private static QuerySchemeConfiguration Configuration() => new([new("title", "contains", "  literal_%[~  ")], "title", false,
        [new("status", 1, null, false, null), new("title", 0, 200, true, "left")]);

    [TestMethod]
    public void Current_schema_preserves_literal_filters_and_normalizes_column_order()
    {
        var result = QuerySchemePolicy.Normalize(Definition, 2, Configuration()); Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("literal_%[~", result.Value!.Filters.Single().Value);
        CollectionAssert.AreEqual(new[] { "title", "status" }, result.Value.Columns.Select(column => column.ColumnKey).ToArray());
        Assert.AreEqual(200, result.Value.Columns[0].Width); Assert.AreEqual("left", result.Value.Columns[0].Fixed);
    }

    [TestMethod]
    public void Old_schema_unknown_fields_operators_sort_and_duplicate_filters_fail_closed()
    {
        Assert.IsFalse(QuerySchemePolicy.Normalize(Definition, 1, Configuration()).IsSuccess);
        foreach (var value in new[]
        {
            Configuration() with { Filters = [new("secret", "contains", "value")] },
            Configuration() with { Filters = [new("title", "sql", "value")] },
            Configuration() with { Filters = [new("title", "contains", "a"),new("title", "contains", "b")] },
            Configuration() with { SortBy = "title;DROP TABLE t" },
            Configuration() with { Filters = [new("title", "contains", "\n")] },
            Configuration() with { Filters = [new("status", "contains", new string('s',33))] },
        }) Assert.IsFalse(QuerySchemePolicy.Normalize(Definition, 2, value).IsSuccess);
    }

    [TestMethod]
    public void Columns_cannot_escape_catalog_budget_or_hide_every_visible_field()
    {
        foreach (var columns in new IReadOnlyList<GridColumnPreference>[]
        {
            [], [new("secret",0,null,true,null)], [new("title",0,10,true,null)], [new("title",0,1201,true,null)],
            [new("title",0,null,true,"sticky")], [new("title",32,null,true,null)], [new("title",0,null,false,null)],
            [new("title",0,null,true,null),new("title",1,null,true,null)],
            [new("title",0,null,true,null),new("status",0,null,true,null)],
        }) Assert.IsFalse(QuerySchemePolicy.Normalize(Definition, 2, Configuration() with { Columns = columns }).IsSuccess);
    }

    [TestMethod]
    public void Request_required_properties_do_not_silently_default_mutation_intent()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize("{\"version\":1}", SettingsJsonSerializerContext.Default.ShareQuerySchemeRequest));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize("{\"version\":0}", SettingsJsonSerializerContext.Default.SetDefaultQuerySchemeRequest));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize("{\"filters\":[],\"sortBy\":\"id\",\"columns\":[]}", SettingsJsonSerializerContext.Default.QuerySchemeConfiguration));
    }

    [TestMethod]
    public void Catalog_accepts_only_safe_unique_installed_business_schemas()
    {
        var catalog = new QuerySchemeCatalog([new Contributor([Definition])]); Assert.IsTrue(catalog.TryGet("test.orders", out _));
        Assert.IsFalse(catalog.TryGet("test.unknown", out _));
        Assert.ThrowsExactly<InvalidOperationException>(() => new QuerySchemeCatalog([new Contributor([Definition, Definition])]));
        Assert.ThrowsExactly<InvalidOperationException>(() => new QuerySchemeCatalog([new Contributor([Definition with { SortKeys = ["id;sql"] }])]));
        Assert.ThrowsExactly<InvalidOperationException>(() => new QuerySchemeCatalog([new Contributor([Definition with { SupportsTenant = false }])]));
    }

    private sealed class Contributor(IReadOnlyCollection<QuerySchemeDefinition> definitions) : IQuerySchemeContributor
    {
        public IReadOnlyCollection<QuerySchemeDefinition> Definitions => definitions;
    }

    [TestMethod]
    public void Serialized_configuration_budget_is_checked_after_normalization()
    {
        var schema = Definition with { Filters = Enumerable.Range(0,8).Select(index => new QuerySchemeFilterDefinition("field" + index,512)).ToArray() };
        var configuration = Configuration() with { Filters = schema.Filters.Select(field => new QuerySchemeFilter(field.FieldKey,"contains",new string('字',512))).ToArray() };
        Assert.IsFalse(QuerySchemePolicy.Normalize(schema,2,configuration).IsSuccess);
    }

    [TestMethod]
    public void Every_declared_provider_statement_keeps_its_host_or_trusted_tenant_boundary()
    {
        var host = new CurrentTenantAccessor(); ((ICurrentTenantContextWriter)host).SetHost();
        var tenant = new CurrentTenantAccessor(); ((ICurrentTenantContextWriter)tenant).SetTenant(new(Guid.CreateVersion7(),"scope-test","Scope test"));
        var statements = typeof(QuerySchemeSql).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(SqlStatement)).Select(field => (SqlStatement)field.GetValue(null)!).ToArray();
        Assert.IsTrue(statements.Length > 0);
        foreach (var statement in statements)
        {
            var isHost = statement.Name.Contains(".host.", StringComparison.Ordinal);
            Assert.AreEqual(isHost ? SqlDataScope.HostOnly : SqlDataScope.TenantRequired, statement.Scope, statement.Name);
            Assert.AreEqual(isHost ? SqlTenantBinding.None : SqlTenantBinding.CurrentTenantId, statement.TenantBinding, statement.Name);
            SqlScopeGuard.Validate(statement, isHost ? host : tenant);
        }
    }
}
