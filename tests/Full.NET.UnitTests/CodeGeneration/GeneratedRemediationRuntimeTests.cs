using Acme.Remediation.Generated;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;
using Full.NET.Data.Dapper;
using Full.NET.Tests.CodeGeneration;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>调用实际生成且随测试工程编译的代码，验证写入失败、约束与类型边界。</summary>
[TestClass]
public sealed class GeneratedRemediationRuntimeTests
{
    [TestMethod]
    public void Generated_errors_use_explicit_culture_and_preserve_machine_codes()
    {
        var source = new Full.NET.Hosting.Api.GeneratedCrudErrorResourceSource("catalog.products.");
        foreach (var suffix in new[] { "not_found", "version_conflict", "unique_conflict", "invalid_parent", "parent_cycle", "parent_has_children", "principal_not_found", "invalid_query", "invalid_organization", "invalid_aggregate", "invalid_field" })
        {
            var code = "catalog.products." + suffix;
            Assert.IsTrue(source.TryGetTemplate(code, System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), out var chinese));
            Assert.IsTrue(source.TryGetTemplate(code, System.Globalization.CultureInfo.GetCultureInfo("en-US"), out var english));
            Assert.AreNotEqual(chinese, english);
        }
        Assert.IsFalse(source.TryGetTemplate("catalog.other.not_found", System.Globalization.CultureInfo.InvariantCulture, out _));
        var invalid = new CreateProductRequest("", 1, 1m, null);
        var result = Create().Service.CreateAsync(invalid, Guid.NewGuid()).GetAwaiter().GetResult();
        Assert.AreEqual("validation.failed", result.Error!.Code);
        Assert.AreEqual("catalog.products.invalid_field", result.Error.ValidationViolations![0].Code);
    }
    [TestMethod]
    public async Task Generated_cascade_then_version_conflict_rolls_back_the_transaction()
    {
        var fixture = Create();
        var result = await fixture.Service.DeleteAsync(fixture.Id, new DeleteProductRequest(4), Guid.NewGuid());
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("catalog.products.version_conflict", result.Error!.Code);
        await fixture.Coordinator.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await fixture.Coordinator.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            Arg.Is<SqlStatement>(statement => statement != null && statement.Text.StartsWith("UPDATE acme_catalog_product_item SET IsDeleted = 1", StringComparison.Ordinal)
                && statement.Text.Contains("DeletedById = @DeletedById", StringComparison.Ordinal)
                && statement.Text.Contains("Version = Version + 1", StringComparison.Ordinal)),
            Arg.Is<object>(parameters => parameters is IReadOnlyDictionary<string, object?>), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Generated_failure_inside_an_existing_transaction_marks_it_rollback_only()
    {
        var fixture = Create();
        fixture.Coordinator.HasTransaction.Returns(true);
        var result = await fixture.Service.DeleteAsync(fixture.Id, new DeleteProductRequest(4), Guid.NewGuid());
        Assert.IsFalse(result.IsSuccess);
        fixture.Coordinator.Received(1).MarkRollbackOnly();
        await fixture.Coordinator.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("", "1.20")]
    [DataRow("   ", "1.20")]
    [DataRow("valid", "1.234")]
    [DataRow("valid", "1000000")]
    [DataRow("valid", "-1000000")]
    public async Task Generated_required_and_decimal_constraints_reject_before_writing(string name, string price)
    {
        var fixture = Create();
        var result = await fixture.Service.CreateAsync(new CreateProductRequest(name, 9,
            decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), null), Guid.NewGuid());
        Assert.IsFalse(result.IsSuccess);
        await fixture.Command.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task Generated_unique_prechecks_use_each_column_and_skip_null()
    {
        var fixture = Create();
        fixture.Command.ExecuteAsync(ProductSql.InsertStatement, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var result = await fixture.Service.CreateAsync(new CreateProductRequest("valid", 9, 1.20m, null), Guid.NewGuid());
        Assert.IsTrue(result.IsSuccess);
        await fixture.Query.Received(1).QuerySingleOrDefaultAsync<Guid?>(
            Arg.Is<SqlStatement>(statement => statement != null && statement.Text.Contains("WHERE Name = @Value", StringComparison.Ordinal)),
            Arg.Is<object>(parameters => HasValue(parameters, "valid")), Arg.Any<CancellationToken>());
        await fixture.Query.Received(1).QuerySingleOrDefaultAsync<Guid?>(
            Arg.Is<SqlStatement>(statement => statement != null && statement.Text.Contains("WHERE Number = @Value", StringComparison.Ordinal)),
            Arg.Is<object>(parameters => HasValue(parameters, 9)), Arg.Any<CancellationToken>());
        await fixture.Query.DidNotReceive().QuerySingleOrDefaultAsync<Guid?>(
            Arg.Is<SqlStatement>(statement => statement != null && statement.Text.Contains("OptionalCode = @Value", StringComparison.Ordinal)),
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Generated_database_unique_conflict_is_mapped_after_rollback()
    {
        var fixture = Create();
        fixture.Command.ExecuteAsync(ProductSql.InsertStatement, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new DataCommandException(DataCommandFailureKind.UniqueConstraint, new Exception("provider")));
        var result = await fixture.Service.CreateAsync(new CreateProductRequest("valid", 9, 1.20m, null), Guid.NewGuid());
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("catalog.products.unique_conflict", result.Error!.Code);
        await fixture.Coordinator.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Generated_runtime_fixtures_match_current_generator()
    {
        var directory = new DirectoryInfo(CodeGenerationRemediationSchemas.RepositoryRoot());
        foreach (var schema in new[] { CodeGenerationRemediationSchemas.Product(), CodeGenerationRemediationSchemas.ProductItem(), CodeGenerationRemediationSchemas.ProductTag() })
        foreach (var artifact in CrudArtifactGenerator.Generate(schema).Where(item => item.Kind == GeneratedArtifactKind.Backend
            && item.RelativePath.EndsWith(".g.cs", StringComparison.Ordinal) && !item.RelativePath.EndsWith("Endpoint.g.cs", StringComparison.Ordinal)))
        {
            var path = Path.Combine(directory.FullName, "tests", "Shared", "CodeGeneration", "Remediation", Path.GetFileName(artifact.RelativePath));
            Assert.AreEqual(artifact.Content, File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), path);
        }
    }

    [TestMethod]
    public void Related_entity_snapshot_is_required_and_cannot_cross_module()
    {
        var schema = CodeGenerationRemediationSchemas.Product();
        var relation = schema.Relationships[0] with { DependentSchema = null };
        Assert.ThrowsExactly<ArgumentException>(() => CrudArtifactGenerator.Generate(Clone(schema, [relation])));
        var list = new List<string> { "ProductId", "Id" };
        var frozen = Clone(schema, [schema.Relationships[0] with { CompositeKeyColumnNames = list }]);
        list.Clear();
        Assert.HasCount(2, frozen.Relationships[0].CompositeKeyColumnNames!);
        var child = schema.Relationships[0].DependentSchema!;
        var crossModule = FullNetCrudSchema.CreateProject("acme", "other", child.EntityKey, "acme_other_" + child.EntityKey,
            child.RootNamespace, child.ClrTypeName, child.ApiResourceName, child.PermissionResourceName,
            child.DataScope, child.EntityCapabilities, child.Columns);
        Assert.ThrowsExactly<ArgumentException>(() => CrudArtifactGenerator.Generate(Clone(schema,
            [schema.Relationships[0] with { DependentSchema = crossModule }])));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Generated_materializer_reads_provider_value_types_without_reflection(bool sqlServerTime)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var time = DateTimeOffset.UtcNow;
        using var table = new System.Data.DataTable();
        foreach (var item in new (string Name, Type Type)[]
        {
            ("Id", typeof(Guid)), ("TenantId", typeof(Guid)), ("Name", typeof(string)), ("Number", typeof(int)),
            ("Price", typeof(decimal)), ("OptionalCode", typeof(string)), ("Version", typeof(decimal)),
            ("IsDeleted", typeof(int)), ("DeletedAtUtc", sqlServerTime ? typeof(DateTimeOffset) : typeof(DateTime)), ("DeletedById", typeof(Guid)),
        }) table.Columns.Add(item.Name, item.Type);
        table.Rows.Add(id, tenantId, "materialized", 7, 1.25m, DBNull.Value, 5m, 1,
            sqlServerTime ? (object)time : time.UtcDateTime, actor);
        using var reader = table.CreateDataReader();
        Assert.IsTrue(reader.Read());
        var row = ProductGeneratedMaterializer.Read(reader);
        Assert.AreEqual(id, row.Id);
        Assert.AreEqual(tenantId, row.TenantId);
        Assert.AreEqual(1.25m, row.Price);
        Assert.IsNull(row.OptionalCode);
        Assert.AreEqual(5L, row.Version);
        Assert.IsTrue(row.IsDeleted);
        Assert.AreEqual(time, row.DeletedAtUtc);
        Assert.AreEqual(actor, row.DeletedById);
    }

    private static bool HasValue(object? parameters, object expected) =>
        parameters is IReadOnlyDictionary<string, object?> values && Equals(values["Value"], expected);

    private static FullNetCrudSchema Clone(FullNetCrudSchema schema, IReadOnlyList<FullNetCrudRelationship> relations) =>
        FullNetCrudSchema.CreateProject(schema.OwnerKey, schema.ModuleKey, schema.EntityKey, schema.DatabaseTableName,
            schema.RootNamespace, schema.ClrTypeName, schema.ApiResourceName, schema.PermissionResourceName,
            schema.DataScope, schema.EntityCapabilities, schema.Scene, relations, schema.Columns);

    private static (ProductManagementService Service, IQueryExecutor Query, ICommandExecutor Command,
        IDbTransactionCoordinator Coordinator, Guid Id) Create()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var coordinator = Substitute.For<IDbTransactionCoordinator>();
        var tenant = Substitute.For<ICurrentTenant>();
        var clock = Substitute.For<IClock>();
        var generator = Substitute.For<IIdGenerator>();
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        tenant.IsAvailable.Returns(true);
        tenant.IsHost.Returns(false);
        tenant.Id.Returns(tenantId);
        generator.NewId().Returns(id);
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        query.QuerySingleOrDefaultAsync<ProductRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new ProductRecord(id, tenantId, "existing", 8, 1.20m, null, 5, false, null, null));
        var queries = new ProductQueryService(query, Substitute.For<IMultiResultQueryExecutor>(), Options.Create(new DatabaseOptions()));
        return (new ProductManagementService(query, command, new DapperCommandTransaction(coordinator), queries, tenant, clock, generator),
            query, command, coordinator, id);
    }
}
