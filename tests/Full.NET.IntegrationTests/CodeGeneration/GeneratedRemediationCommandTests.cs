using System.Data.Common;
using Acme.Remediation.Generated;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.Dapper;
using Full.NET.Tests.CodeGeneration;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.CodeGeneration;

/// <summary>双库调用实际生成的管理服务及 Full.NET 执行器，验证约束与失败事务。</summary>
[TestClass]
public sealed class GeneratedRemediationCommandTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Tutorial_master_detail_migration_is_repeatable_and_has_explicit_child_organization(DatabaseProvider provider)
    {
        await using var runtime = await Runtime.CreateAsync(provider);
        var script = await File.ReadAllTextAsync(Path.Combine(CodeGenerationRemediationSchemas.RepositoryRoot(),
            "samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/Migrations", provider.ToString(), "001_EnterpriseRequest.sql"));
        await runtime.Connection.ExecuteAsync(script);
        await runtime.Connection.ExecuteAsync(script);
        Assert.AreEqual(1, await runtime.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'demo_enterprise_request_enterprise_request_line' AND COLUMN_NAME = 'OrganizationUnitId' AND IS_NULLABLE = 'NO'"));
    }
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Relation_options_use_real_scoped_queries_and_return_only_keys_and_labels(DatabaseProvider provider)
    {
        await using var runtime = await Runtime.CreateAsync(provider);
        var actor = Guid.CreateVersion7();
        var product = runtime.Scope.ServiceProvider.GetRequiredService<ProductManagementService>();
        var first = (await product.CreateAsync(new("literal%_[~ source", 11, 1m, null), actor)).Value!;
        Assert.IsTrue((await product.CreateAsync(new("literalAB source", 12, 2m, null), actor)).IsSuccess);
        var options = runtime.Scope.ServiceProvider.GetRequiredService<ProductTagRelationLookupService>();
        var selected = (await options.ListSourceAsync(1, 20, selected: first.Id)).Value!;
        Assert.AreEqual(first.Id, selected.Items.Single().Value);
        Assert.AreEqual(first.Name, selected.Items.Single().Label);
        Assert.AreEqual(1L, (await options.ListSourceAsync(1, 20, "%_[~")).Value!.Total);
        var tenant = (ICurrentTenantContextWriter)runtime.Scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "other", "Other"));
        Assert.AreEqual(0L, (await options.ListSourceAsync(1, 20, selected: first.Id)).Value!.Total);
        tenant.SetTenant(new TenantContext(runtime.TenantId, "test", "Test"));
        Assert.IsTrue((await product.DeleteAsync(first.Id, new(first.Version), actor)).IsSuccess);
        Assert.AreEqual(0L, (await options.ListSourceAsync(1, 20, selected: first.Id)).Value!.Total);
        var source = runtime.Scope.ServiceProvider.GetRequiredService<OrganizationSourceManagementService>();
        var unitA = Guid.CreateVersion7(); var unitB = Guid.CreateVersion7();
        var a = (await source.CreateAsync(new("A"), actor, unitA)).Value!;
        var b = (await source.CreateAsync(new("B"), actor, unitB)).Value!;
        var owned = runtime.Scope.ServiceProvider.GetRequiredService<OrganizationLinkRelationLookupService>();
        var inB = (await owned.ListSourceAsync(1, 20, null, null, actor, false, unitB)).Value!;
        Assert.AreEqual(b.Id, inB.Items.Single().Value);
        Assert.AreEqual(0L, (await owned.ListSourceAsync(1, 20, null, a.Id, actor, false, unitB)).Value!.Total);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Aggregate_create_and_edit_are_atomic_and_foreign_or_stale_details_cannot_change_the_master(DatabaseProvider provider)
    {
        await using var runtime = await Runtime.CreateAsync(provider);
        var service = runtime.Scope.ServiceProvider.GetRequiredService<ProductAggregateService>();
        var actor = Guid.CreateVersion7();
        var failed = await service.CreateAsync(new(new("must rollback", 10, 1.25m, null), [new("valid"), new(new string('x', 65))]), actor);
        Assert.IsFalse(failed.IsSuccess);
        Assert.AreEqual(0, await runtime.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM acme_catalog_product"));
        Assert.AreEqual(0, await runtime.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM acme_catalog_product_item"));
        var oversized = await service.CreateAsync(new(new("too many", 10, 1m, null), Enumerable.Range(0, 129).Select(index => new ProductDetailInput("row" + index)).ToArray()), actor);
        Assert.AreEqual("catalog.products.invalid_aggregate", oversized.Error!.Code);
        var first = (await service.CreateAsync(new(new("aggregate", 1, 1.25m, null), [new("A"), new("B")]), actor)).Value!;
        var other = (await service.CreateAsync(new(new("other", 2, 2m, null), [new("other child")]), actor)).Value!;
        var a = first.Details[0]; var b = first.Details[1];
        var foreign = await service.UpdateAsync(first.Master.Id, new(new("foreign must rollback", 3, 3m, null, 1), [],
            [new(other.Details[0].Id, 1, new("moved"))], []), actor);
        Assert.AreEqual("catalog.products.invalid_aggregate", foreign.Error!.Code);
        var before = (await service.GetAsync(first.Master.Id)).Value!;
        Assert.AreEqual("aggregate", before.Master.Name); Assert.AreEqual(1L, before.Master.Version);
        var updated = await service.UpdateAsync(first.Master.Id, new(new("updated aggregate", 3, 3m, null, 1), [new("C")],
            [new(a.Id, a.Version, new("A2"))], [new(b.Id, b.Version)]), actor);
        Assert.IsTrue(updated.IsSuccess, updated.Error?.Code);
        Assert.AreEqual(2L, updated.Value!.Master.Version);
        CollectionAssert.AreEquivalent(new[] { "A2", "C" }, updated.Value.Details.Select(item => item.Description).ToArray());
        Assert.AreEqual(1, await runtime.Connection.ExecuteScalarAsync<int>("SELECT IsDeleted FROM acme_catalog_product_item WHERE Id = @Id", new { b.Id }));
        var stale = await service.UpdateAsync(first.Master.Id, new(new("stale must rollback", 4, 4m, null, 2), [],
            [new(a.Id, 1, new("stale"))], []), actor);
        Assert.AreEqual("catalog.product_items.version_conflict", stale.Error!.Code);
        var final = (await service.GetAsync(first.Master.Id)).Value!;
        Assert.AreEqual("updated aggregate", final.Master.Name); Assert.AreEqual(2L, final.Master.Version);
        Assert.AreEqual("A2", final.Details.Single(item => item.Id == a.Id).Description);
        Assert.AreEqual(other.Master.Id, (await service.GetAsync(other.Master.Id)).Value!.Details.Single().ProductId);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Tree_writes_serialize_opposite_reparents_and_parent_deletion_on_real_connections(DatabaseProvider provider)
    {
        await using var runtime = await Runtime.CreateAsync(provider);
        var actor = Guid.CreateVersion7();
        var service = runtime.Scope.ServiceProvider.GetRequiredService<TreeNodeManagementService>();
        var a = (await service.CreateAsync(new("A", null), actor)).Value!;
        var b = (await service.CreateAsync(new("B", null), actor)).Value!;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Result<TreeNodeResponse>> Move(Guid id, string name, Guid parent)
        {
            await using var scope = runtime.Services.CreateAsyncScope();
            ((ICurrentTenantContextWriter)scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>())
                .SetTenant(new TenantContext(runtime.TenantId, "test", "Test"));
            await start.Task;
            return await scope.ServiceProvider.GetRequiredService<TreeNodeManagementService>().UpdateAsync(id, new(name, parent, 1), actor);
        }
        var first = Move(a.Id, "A", b.Id);
        var second = Move(b.Id, "B", a.Id);
        start.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.AreEqual(1, results.Count(result => result.IsSuccess));
        Assert.AreEqual("catalog.tree_nodes.parent_cycle", results.Single(result => !result.IsSuccess).Error!.Code);
        var child = results.Single(result => result.IsSuccess).Value!;
        var parent = child.Id == a.Id ? b : a;
        var denied = await service.DeleteAsync(parent.Id, new(parent.Version), actor);
        Assert.AreEqual("catalog.tree_nodes.parent_has_children", denied.Error!.Code);
        Assert.IsTrue((await service.DeleteAsync(child.Id, new(child.Version), actor)).IsSuccess);
        Assert.IsTrue((await service.DeleteAsync(parent.Id, new(parent.Version), actor)).IsSuccess);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Organization_relationships_tree_and_cascade_remain_inside_the_authorized_unit(DatabaseProvider provider)
    {
        await using var runtime = await Runtime.CreateAsync(provider);
        var actor = Guid.CreateVersion7(); var unitA = Guid.CreateVersion7(); var unitB = Guid.CreateVersion7();
        var tree = runtime.Scope.ServiceProvider.GetRequiredService<OrganizationTreeNodeManagementService>();
        var parent = (await tree.CreateAsync(new("unit B", null), actor, unitB)).Value!;
        var crossParent = await tree.CreateAsync(new("unit A", parent.Id), actor, unitA);
        Assert.AreEqual("catalog.organization_tree_nodes.invalid_parent", crossParent.Error!.Code);
        var source = (await runtime.Scope.ServiceProvider.GetRequiredService<OrganizationSourceManagementService>().CreateAsync(new("source"), actor, unitA)).Value!;
        var target = (await runtime.Scope.ServiceProvider.GetRequiredService<OrganizationTargetManagementService>().CreateAsync(new("target"), actor, unitB)).Value!;
        var link = runtime.Scope.ServiceProvider.GetRequiredService<OrganizationLinkManagementService>();
        Assert.AreEqual("catalog.organization_links.principal_not_found", (await link.CreateAsync(new(source.Id, target.Id), actor, unitA)).Error!.Code);
        var management = runtime.Scope.ServiceProvider.GetRequiredService<OrganizationParentManagementService>();
        var aggregate = (await management.CreateAsync(new("aggregate"), actor, unitA)).Value!;
        // 模拟已有坏数据：级联只能触及主端归属，不可因外键相同而跨机构更新。
        var own = Guid.CreateVersion7(); var foreign = Guid.CreateVersion7();
        foreach (var (id, unit) in new[] { (own, unitA), (foreign, unitB) })
            await runtime.Connection.ExecuteAsync("""
                INSERT INTO acme_catalog_organization_child (Id, TenantId, OrganizationUnitId, Name, ParentId, Version, IsDeleted)
                VALUES (@Id, @TenantId, @OrganizationUnitId, 'child', @ParentId, 1, 0)
                """, new { Id = id, runtime.TenantId, OrganizationUnitId = unit, ParentId = aggregate.Id });
        Assert.IsTrue((await management.DeleteAsync(aggregate.Id, new(aggregate.Version), actor)).IsSuccess);
        Assert.AreEqual(1, await runtime.Connection.ExecuteScalarAsync<int>("SELECT IsDeleted FROM acme_catalog_organization_child WHERE Id = @Id", new { Id = own }));
        Assert.AreEqual(0, await runtime.Connection.ExecuteScalarAsync<int>("SELECT IsDeleted FROM acme_catalog_organization_child WHERE Id = @Id", new { Id = foreign }));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Generated_workbench_filters_sort_precision_nulls_and_scope_execute_on_the_real_provider(DatabaseProvider provider)
    {
        await using var runtime = await Runtime.CreateAsync(provider);
        var service = runtime.Scope.ServiceProvider.GetRequiredService<WorkbenchManagementService>();
        var query = runtime.Scope.ServiceProvider.GetRequiredService<WorkbenchQueryService>();
        var actor = Guid.CreateVersion7();
        var reference = Guid.CreateVersion7();
        var instant = DateTimeOffset.Parse("2026-10-26T12:34:56.123456Z", System.Globalization.CultureInfo.InvariantCulture);
        const decimal price = 12345678901234.5678m;
        var first = await service.CreateAsync(new("literal%_[~tail", long.MaxValue, price, null, reference, instant), actor);
        var second = await service.CreateAsync(new("literal%_[~tail", long.MaxValue, price, false, null, instant), actor);
        Assert.IsTrue(first.IsSuccess, first.Error?.Code);
        Assert.IsTrue(second.IsSuccess, second.Error?.Code);
        Assert.IsTrue((await service.CreateAsync(new("literalABtail", 2, 0.1m, true, null, null), actor)).IsSuccess);
        Assert.IsTrue((await service.CreateAsync(new("LITERAL%_[~tail", 3, 1m, true, null, null), actor)).IsSuccess);
        var tenant = (ICurrentTenantContextWriter)runtime.Scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "other", "Other"));
        Assert.IsTrue((await service.CreateAsync(new("literal%_[~tail", long.MaxValue, price, false, null, instant), actor)).IsSuccess);
        tenant.SetTenant(new TenantContext(runtime.TenantId, "test", "Test"));
        var filter = new WorkbenchListQuery { FilterName = "%_[~", FilterPriceFrom = price, FilterPriceTo = price,
            FilterOccurredAtUtcFrom = instant, FilterOccurredAtUtcTo = instant, SortBy = "name" };
        var page1 = (await query.ListAsync(1, 1, query: filter)).Value!;
        var page2 = (await query.ListAsync(2, 1, query: filter)).Value!;
        Assert.AreEqual(2L, page1.Total);
        Assert.AreEqual(2L, page2.Total);
        Assert.AreNotEqual(page1.Items.Single().Id, page2.Items.Single().Id);
        Assert.AreEqual(page1.Items.Single().Id, (await query.ListAsync(1, 1, query: filter)).Value!.Items.Single().Id);
        Assert.AreEqual(long.MaxValue, page1.Items.Single().Quantity);
        Assert.AreEqual(price, page1.Items.Single().Price);
        Assert.AreEqual(instant, page1.Items.Single().OccurredAtUtc);
        var onlyFalse = (await query.ListAsync(1, 20, query: new() { FilterName = "%_[~", FilterIsReady = false })).Value!;
        Assert.AreEqual(second.Value!.Id, onlyFalse.Items.Single().Id);
        var byReference = (await query.ListAsync(1, 20, query: new() { FilterReferenceId = reference })).Value!;
        Assert.AreEqual(first.Value!.Id, byReference.Items.Single().Id);
        var deleted = await service.DeleteAsync(second.Value.Id, new(second.Value.Version), actor);
        Assert.IsTrue(deleted.IsSuccess, deleted.Error?.Code);
        Assert.AreEqual(1L, (await query.ListAsync(1, 20, query: filter)).Value!.Total);
        var tooPrecise = await service.CreateAsync(new("reject precision", 1, 1m, null, null, instant.AddTicks(1)), actor);
        Assert.IsFalse(tooPrecise.IsSuccess);
        var descending = (await query.ListAsync(1, 20, query: new() { SortBy = "quantity", Descending = true })).Value!;
        Assert.AreEqual(long.MaxValue, descending.Items[0].Quantity);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Generated_version_conflict_preserves_children_then_soft_delete_audits_them(DatabaseProvider provider)
    {
        await using var runtime = await Runtime.CreateAsync(provider);
        var service = runtime.Scope.ServiceProvider.GetRequiredService<ProductManagementService>();
        var actor = Guid.NewGuid();
        var created = await service.CreateAsync(new CreateProductRequest("parent", 1, 1.25m, null), actor);
        Assert.IsTrue(created.IsSuccess, created.Error?.Code);
        var parentId = created.Value!.Id;
        var childId = Guid.NewGuid();
        await runtime.Connection.ExecuteAsync("""
            INSERT INTO acme_catalog_product_item (Id, TenantId, ProductId, Description, Version, IsDeleted)
            VALUES (@Id, @TenantId, @ProductId, @Description, 1, 0)
            """, new { Id = childId, runtime.TenantId, ProductId = parentId, Description = "preserve" });
        var failed = await service.DeleteAsync(parentId, new DeleteProductRequest(999), actor);
        Assert.IsFalse(failed.IsSuccess);
        Assert.AreEqual("catalog.products.version_conflict", failed.Error!.Code);
        Assert.IsFalse(await runtime.Connection.QuerySingleAsync<bool>(
            "SELECT IsDeleted FROM acme_catalog_product_item WHERE Id = @Id", new { Id = childId }));
        Assert.AreEqual(1L, await runtime.Connection.QuerySingleAsync<long>(
            "SELECT Version FROM acme_catalog_product_item WHERE Id = @Id", new { Id = childId }));
        var succeeded = await service.DeleteAsync(parentId, new DeleteProductRequest(created.Value.Version), actor);
        Assert.IsTrue(succeeded.IsSuccess, succeeded.Error?.Code);
        Assert.IsTrue(await runtime.Connection.QuerySingleAsync<bool>(
            "SELECT IsDeleted FROM acme_catalog_product_item WHERE Id = @Id", new { Id = childId }));
        Assert.AreEqual(actor, await runtime.Connection.QuerySingleAsync<Guid>(
            "SELECT DeletedById FROM acme_catalog_product_item WHERE Id = @Id", new { Id = childId }));
        Assert.AreEqual(2L, await runtime.Connection.QuerySingleAsync<long>(
            "SELECT Version FROM acme_catalog_product_item WHERE Id = @Id", new { Id = childId }));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Generated_unique_indexes_recover_and_enforce_nullable_and_tenant_semantics(DatabaseProvider provider)
    {
        await using var runtime = await Runtime.CreateAsync(provider);
        var service = runtime.Scope.ServiceProvider.GetRequiredService<ProductManagementService>();
        var actor = Guid.NewGuid();
        var first = await service.CreateAsync(new CreateProductRequest("first", 1, 1m, null), actor);
        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue((await service.CreateAsync(new CreateProductRequest("second", 2, 1m, null), actor)).IsSuccess);
        var conflict = await service.CreateAsync(new CreateProductRequest("third", 2, 1m, null), actor);
        Assert.AreEqual("catalog.products.unique_conflict", conflict.Error!.Code);
        var insert = """
            INSERT INTO acme_catalog_product (Id, TenantId, Name, Number, Price, OptionalCode, Version, IsDeleted)
            VALUES (@Id, @TenantId, @Name, @Number, @Price, NULL, 1, 0)
            """;
        // 绕过预检查仍须由数据库约束阻断，不能把串行应用检查当并发唯一性保证。
        await Assert.ThrowsAsync<DbException>(() => runtime.Connection.ExecuteAsync(insert,
            new { Id = Guid.NewGuid(), runtime.TenantId, Name = "duplicate", Number = 2, Price = 1m }));
        await runtime.Connection.ExecuteAsync(insert,
            new { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "first", Number = 1, Price = 1m });
        Assert.AreEqual(3, await runtime.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM acme_catalog_product"));
        Assert.IsTrue((await service.CreateAsync(new CreateProductRequest("FIRST", 3, 1m, "code"), actor)).IsSuccess);
        Assert.IsTrue((await service.DeleteAsync(first.Value!.Id, new DeleteProductRequest(first.Value.Version), actor)).IsSuccess);
        var deletedKey = await service.CreateAsync(new CreateProductRequest("first", 4, 1m, null), actor);
        Assert.AreEqual("catalog.products.unique_conflict", deletedKey.Error!.Code);
        var nullableConflict = await service.CreateAsync(new CreateProductRequest("fourth", 4, 1m, "code"), actor);
        Assert.AreEqual("catalog.products.unique_conflict", nullableConflict.Error!.Code);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Generated_many_to_many_checks_both_principals_and_rejects_duplicate_pair(DatabaseProvider provider)
    {
        await using var runtime = await Runtime.CreateAsync(provider);
        var product = await runtime.Scope.ServiceProvider.GetRequiredService<ProductManagementService>()
            .CreateAsync(new CreateProductRequest("parent", 1, 1m, null), Guid.NewGuid());
        Assert.IsTrue(product.IsSuccess);
        var tagId = Guid.NewGuid();
        await runtime.Connection.ExecuteAsync("""
            INSERT INTO acme_catalog_tag (Id, TenantId, Name, Version, IsDeleted)
            VALUES (@Id, @TenantId, @Name, 1, 0)
            """, new { Id = tagId, runtime.TenantId, Name = "tag" });
        var service = runtime.Scope.ServiceProvider.GetRequiredService<ProductTagManagementService>();
        var missing = await service.CreateAsync(new CreateProductTagRequest(product.Value!.Id, Guid.NewGuid()), Guid.NewGuid());
        Assert.AreEqual("catalog.product_tags.principal_not_found", missing.Error!.Code);
        var request = new CreateProductTagRequest(product.Value.Id, tagId);
        var created = await service.CreateAsync(request, Guid.NewGuid());
        Assert.IsTrue(created.IsSuccess);
        // 物理列 ProductId 与 CLR/JSON 名称不同，写入参数和查询映射都必须保持正确。
        var fetched = await runtime.Scope.ServiceProvider.GetRequiredService<ProductTagQueryService>()
            .GetByIdAsync(created.Value!.Id);
        Assert.AreEqual(product.Value.Id, fetched.Value!.ProductReferenceId);
        var duplicate = await service.CreateAsync(request, Guid.NewGuid());
        Assert.AreEqual("catalog.product_tags.unique_conflict", duplicate.Error!.Code);
        Assert.AreEqual(1, await runtime.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM acme_catalog_product_tag"));
        Assert.IsTrue((await service.DeleteAsync(created.Value.Id, new DeleteProductTagRequest(created.Value.Version), Guid.NewGuid())).IsSuccess);
        var restored = await service.CreateAsync(request, Guid.NewGuid());
        Assert.IsTrue(restored.IsSuccess, restored.Error?.Code);
        Assert.AreEqual(created.Value.Id, restored.Value!.Id);
        Assert.AreEqual(3L, restored.Value.Version);
        Assert.IsFalse(restored.Value.IsDeleted);
        Assert.IsNull(restored.Value.DeletedAtUtc);
        Assert.IsNull(restored.Value.DeletedById);
        Assert.AreEqual(1, await runtime.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM acme_catalog_product_tag"));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Generated_concurrent_creates_converge_to_one_row_and_one_conflict(DatabaseProvider provider)
    {
        await using var runtime = await Runtime.CreateAsync(provider);
        await using var first = runtime.Services.CreateAsyncScope();
        await using var second = runtime.Services.CreateAsyncScope();
        foreach (var scope in new[] { first, second })
            ((ICurrentTenantContextWriter)scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>())
                .SetTenant(new TenantContext(runtime.TenantId, "test", "Test"));
        var results = await Task.WhenAll(first.ServiceProvider.GetRequiredService<ProductManagementService>()
                .CreateAsync(new CreateProductRequest("race", 41, 1.25m, null), Guid.NewGuid()),
            second.ServiceProvider.GetRequiredService<ProductManagementService>()
                .CreateAsync(new CreateProductRequest("race", 41, 1.25m, null), Guid.NewGuid()));
        Assert.AreEqual(1, results.Count(result => result.IsSuccess));
        Assert.AreEqual("catalog.products.unique_conflict", results.Single(result => !result.IsSuccess).Error!.Code);
        Assert.AreEqual(1, await runtime.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM acme_catalog_product"));
    }

    private sealed record Runtime(ServiceProvider Services, AsyncServiceScope Scope, DbConnection Connection, Guid TenantId) : IAsyncDisposable
    {
        internal static async Task<Runtime> CreateAsync(DatabaseProvider provider)
        {
            var connectionString = provider == DatabaseProvider.SqlServer
                ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
                : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
            if (provider == DatabaseProvider.MySql)
                connectionString = new MySqlConnectionStringBuilder(connectionString)
                {
                    GuidFormat = MySqlGuidFormat.Binary16,
                    // 与 DbUp 执行草案一致，允许迁移内用于幂等探测的会话变量。
                    AllowUserVariables = true,
                }.ConnectionString;
            DbConnection connection = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString) : new MySqlConnection(connectionString);
            await connection.OpenAsync();
            var schema = CodeGenerationRemediationSchemas.ProductTag();
            foreach (var entity in new[] { CodeGenerationRemediationSchemas.Product(), CodeGenerationRemediationSchemas.ProductItem(),
                schema.Relationships[1].PrincipalSchema!, schema, CodeGenerationRemediationSchemas.Workbench(), CodeGenerationRemediationSchemas.TreeNode(),
                CodeGenerationRemediationSchemas.OrganizationTreeNode(), CodeGenerationRemediationSchemas.OrganizationParent(),
                CodeGenerationRemediationSchemas.OrganizationParent().Relationships[0].DependentSchema!, CodeGenerationRemediationSchemas.OrganizationLink(),
                CodeGenerationRemediationSchemas.OrganizationLink().Relationships[0].PrincipalSchema!, CodeGenerationRemediationSchemas.OrganizationLink().Relationships[1].PrincipalSchema! })
            {
                var suffix = provider == DatabaseProvider.SqlServer ? "SqlServer" : "MySql";
                var script = CrudArtifactGenerator.Generate(entity).Single(artifact => artifact.RelativePath == $"templates/migrations/{suffix}/Create{entity.ClrTypeName}.sql.template").Content;
                // 独立索引可能在建表后失败；重跑整个草案须幂等收敛。
                await connection.ExecuteAsync(script);
                await connection.ExecuteAsync(script);
            }
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = provider.ToString(), ["Database:ConnectionString"] = connectionString,
                ["Database:MySqlGuidStorageMode"] = "Binary16",
            }).Build();
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddScoped<CurrentTenantAccessor>();
            collection.AddScoped<ICurrentTenant>(services => services.GetRequiredService<CurrentTenantAccessor>());
            collection.AddFullNetDapper(configuration, "Testing");
            collection.AddSingleton<IClock, SystemClock>();
            collection.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
            collection.AddScoped<ProductQueryService>();
            collection.AddScoped<ProductManagementService>();
            collection.AddScoped<ProductDetailQueryService>(); collection.AddScoped<ProductDetailManagementService>();
            collection.AddScoped<ProductAggregateService>();
            collection.AddScoped<ProductTagQueryService>();
            collection.AddScoped<ProductTagManagementService>();
            collection.AddScoped<ProductTagSourceQueryService>(); collection.AddScoped<ProductTagTargetQueryService>();
            collection.AddScoped<ProductTagRelationLookupService>();
            collection.AddScoped<WorkbenchQueryService>();
            collection.AddScoped<WorkbenchManagementService>();
            collection.AddScoped<TreeNodeQueryService>(); collection.AddScoped<TreeNodeManagementService>();
            collection.AddScoped<OrganizationTreeNodeQueryService>(); collection.AddScoped<OrganizationTreeNodeManagementService>();
            collection.AddScoped<OrganizationParentQueryService>(); collection.AddScoped<OrganizationParentManagementService>();
            collection.AddScoped<OrganizationSourceQueryService>(); collection.AddScoped<OrganizationSourceManagementService>();
            collection.AddScoped<OrganizationTargetQueryService>(); collection.AddScoped<OrganizationTargetManagementService>();
            collection.AddScoped<OrganizationLinkQueryService>(); collection.AddScoped<OrganizationLinkManagementService>();
            collection.AddScoped<OrganizationLinkSourceQueryService>(); collection.AddScoped<OrganizationLinkTargetQueryService>();
            collection.AddScoped<OrganizationLinkRelationLookupService>();
            collection.AddSingleton<IOrganizationOwnedEntityWriteAuthorizer, AllowedTestAuthorizer>();
            collection.AddSingleton<IUserDataScopeResolver, TestScopeResolver>();
            collection.AddSingleton<IDataScopeSqlFilterBuilder, TestScopeFilter>();
            var services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var scope = services.CreateAsyncScope();
            var tenantId = Guid.NewGuid();
            ((ICurrentTenantContextWriter)scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>()).SetTenant(new TenantContext(tenantId, "test", "Test"));
            return new Runtime(services, scope, connection, tenantId);
        }

        public async ValueTask DisposeAsync()
        {
            await Scope.DisposeAsync();
            await Services.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    // 测试只隔离生成服务自身边界；授权服务允许写两个组织，用于证明这种授权不会解除关系约束。
    private sealed class AllowedTestAuthorizer : IOrganizationOwnedEntityWriteAuthorizer
    {
        public Task<Result<bool>> EnsureCanWriteAsync(Guid tenantId, Guid organizationUnitId, Guid actorUserId, CancellationToken cancellationToken = default) => Task.FromResult(Result<bool>.Success(true));
    }
    private sealed class TestScopeResolver : IUserDataScopeResolver
    {
        public Task<EffectiveUserDataScope> ResolveAsync(Guid userId, bool isSuperAdministrator, CancellationToken cancellationToken = default) => Task.FromResult(new EffectiveUserDataScope(true, []));
    }
    private sealed class TestScopeFilter : IDataScopeSqlFilterBuilder
    {
        public DataScopeSqlFilter? BuildOrganizationUnitFilter(EffectiveUserDataScope scope, string unitIdColumn, Guid currentUserId) => null;
    }
}
