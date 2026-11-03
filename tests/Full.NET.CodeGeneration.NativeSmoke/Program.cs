using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Remediation.Generated;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.Dapper;
using Full.NET.Tests.CodeGeneration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

// 仅接受外层验收创建的临时数据库，不在输出中记录连接串。
var provider = Enum.Parse<DatabaseProvider>(Environment.GetEnvironmentVariable("FULLNET_SMOKE_PROVIDER")!, false);
var connectionString = Environment.GetEnvironmentVariable("FULLNET_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException("缺少临时数据库连接配置。");
var migrationConnection = provider == DatabaseProvider.MySql
    ? new MySqlConnectionStringBuilder(connectionString) { AllowUserVariables = true }.ConnectionString
    : connectionString;
await using (DbConnection connection = provider == DatabaseProvider.SqlServer
    ? new SqlConnection(migrationConnection) : new MySqlConnection(migrationConnection))
{
    await connection.OpenAsync();
    foreach (var schema in new[] { CodeGenerationRemediationSchemas.Product(), CodeGenerationRemediationSchemas.ProductItem(), CodeGenerationRemediationSchemas.Workbench(), CodeGenerationRemediationSchemas.TreeNode(), CodeGenerationRemediationSchemas.ProductTag().Relationships[1].PrincipalSchema!, CodeGenerationRemediationSchemas.ProductTag() })
    {
        await using var command = connection.CreateCommand();
        command.CommandText = CrudArtifactGenerator.Generate(schema).Single(artifact =>
            artifact.RelativePath == $"templates/migrations/{provider}/Create{schema.ClrTypeName}.sql.template").Content;
        await command.ExecuteNonQueryAsync();
    }
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
collection.AddScoped<WorkbenchQueryService>(); collection.AddScoped<WorkbenchManagementService>();
collection.AddScoped<TreeNodeQueryService>(); collection.AddScoped<TreeNodeManagementService>();
collection.AddScoped<ProductDetailQueryService>(); collection.AddScoped<ProductDetailManagementService>(); collection.AddScoped<ProductAggregateService>();
collection.AddScoped<ProductTagQueryService>(); collection.AddScoped<ProductTagManagementService>(); collection.AddScoped<ProductTagRelationLookupService>();
collection.AddScoped<ProductTagSourceQueryService>(); collection.AddScoped<ProductTagTargetQueryService>(); collection.AddScoped<ProductTagTargetManagementService>();
#if FULLNET_AOT_COMPILE
ProductGeneratedMaterializer.Register();
ProductItemGeneratedMaterializer.Register();
WorkbenchGeneratedMaterializer.Register();
TreeNodeGeneratedMaterializer.Register();
ProductDetailGeneratedMaterializer.Register();
ProductTagGeneratedMaterializer.Register(); ProductTagSourceGeneratedMaterializer.Register(); ProductTagTargetGeneratedMaterializer.Register();
#endif
await using var services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
await using var scope = services.CreateAsyncScope();
((ICurrentTenantContextWriter)scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>())
    .SetTenant(new TenantContext(Guid.NewGuid(), "native", "Native"));
var management = scope.ServiceProvider.GetRequiredService<ProductManagementService>();
var queries = scope.ServiceProvider.GetRequiredService<ProductQueryService>();
var actor = Guid.NewGuid();
var created = await management.CreateAsync(new CreateProductRequest("native", 1, 1.25m, null), actor);
Check(created.IsSuccess, created.Error?.Code);
var id = created.Value!.Id;
var fetched = await queries.GetByIdAsync(id);
Check(fetched.IsSuccess && fetched.Value!.Price == 1.25m && fetched.Value.OptionalCode is null, "read");
var list = await queries.ListAsync(1, 10);
Check(list.IsSuccess && list.Value!.Total == 1, "list");
var updated = await management.UpdateAsync(id, new UpdateProductRequest("updated", 2, 3.50m, "code", created.Value.Version), actor);
Check(updated.IsSuccess && updated.Value!.Version == 2, updated.Error?.Code);
var stale = await management.DeleteAsync(id, new DeleteProductRequest(1), actor);
Check(!stale.IsSuccess && stale.Error!.Code == "catalog.products.version_conflict", "stale");
var deleted = await management.DeleteAsync(id, new DeleteProductRequest(2), actor);
Check(deleted.IsSuccess, deleted.Error?.Code);
Check(!(await queries.GetByIdAsync(id)).IsSuccess, "soft_delete");
var occupied = await management.CreateAsync(new CreateProductRequest("updated", 3, 1m, null), actor);
Check(!occupied.IsSuccess && occupied.Error!.Code == "catalog.products.unique_conflict", "unique");
const string workbenchJson = """{"name":"native workbench","quantity":"9223372036854775807","price":"12345678901234.5678","isReady":null,"referenceId":null,"occurredAtUtc":"2026-10-26T12:34:56.123456Z"}""";
var workbenchInput = JsonSerializer.Deserialize(workbenchJson, ProbeJsonContext.Default.CreateWorkbenchRequest)!;
var workbench = scope.ServiceProvider.GetRequiredService<WorkbenchManagementService>();
var createdWorkbench = await workbench.CreateAsync(workbenchInput, actor);
Check(createdWorkbench.IsSuccess, createdWorkbench.Error?.Code);
var filtered = await scope.ServiceProvider.GetRequiredService<WorkbenchQueryService>().ListAsync(1, 10,
    query: new() { FilterQuantity = long.MaxValue, FilterPriceFrom = workbenchInput.Price, FilterPriceTo = workbenchInput.Price, SortBy = "price", Descending = true });
Check(filtered.IsSuccess && filtered.Value!.Total == 1 && filtered.Value.Items[0].OccurredAtUtc == workbenchInput.OccurredAtUtc, "exact_filtered_workbench");
var workbenchOutput = JsonSerializer.Serialize(filtered.Value!.Items[0], ProbeJsonContext.Default.WorkbenchResponse);
Check(workbenchOutput.Contains("\"quantity\":\"9223372036854775807\"", StringComparison.Ordinal), "int64_json_wire");
Check(workbenchOutput.Contains("\"isReady\":null", StringComparison.Ordinal), "nullable_json_wire");
try
{
    _ = JsonSerializer.Deserialize(workbenchJson.Replace("12345678901234.5678", "1.000000000000000000000000000001", StringComparison.Ordinal), ProbeJsonContext.Default.CreateWorkbenchRequest);
    throw new InvalidOperationException("超精度 decimal 不得在反序列化时静默舍入。");
}
catch (JsonException) { /* 只接受转换器明确拒绝；其他失败不能伪装为验收成功。 */ }
var tree = scope.ServiceProvider.GetRequiredService<TreeNodeManagementService>();
var parentNode = (await tree.CreateAsync(new("parent", null), actor)).Value!;
var childNode = (await tree.CreateAsync(new("child", parentNode.Id), actor)).Value!;
Check(!(await tree.UpdateAsync(parentNode.Id, new("cycle", childNode.Id, parentNode.Version), actor)).IsSuccess, "tree_cycle");
Check(!(await tree.DeleteAsync(parentNode.Id, new(parentNode.Version), actor)).IsSuccess, "tree_parent_delete");
Check((await tree.DeleteAsync(childNode.Id, new(childNode.Version), actor)).IsSuccess, "tree_leaf_delete");
var aggregate = scope.ServiceProvider.GetRequiredService<ProductAggregateService>();
var combined = await aggregate.CreateAsync(new(new("aggregate", 10, 1m, null), [new("native detail")]), actor);
Check(combined.IsSuccess && combined.Value!.Details.Count == 1, combined.Error?.Code);
var rejected = await aggregate.CreateAsync(new(new("rejected aggregate", 11, 1m, null), [new(new string('x', 65))]), actor);
Check(!rejected.IsSuccess, "aggregate_invalid_detail");
Check((await queries.ListAsync(1, 10, query: new() { FilterName = "rejected aggregate" })).Value!.Total == 0, "aggregate_rollback");
var tag = await scope.ServiceProvider.GetRequiredService<ProductTagTargetManagementService>().CreateAsync(new("native tag"), actor);
Check(tag.IsSuccess, tag.Error?.Code);
var link = scope.ServiceProvider.GetRequiredService<ProductTagManagementService>();
var linked = await link.CreateAsync(new(combined.Value!.Master.Id, tag.Value!.Id), actor);
Check(linked.IsSuccess, linked.Error?.Code);
var choices = await scope.ServiceProvider.GetRequiredService<ProductTagRelationLookupService>().ListTargetAsync(1, 20, "native");
Check(choices.IsSuccess && choices.Value!.Items.Single().Value == tag.Value.Id, "minimal_relation_lookup");
Check((await link.DeleteAsync(linked.Value!.Id, new(linked.Value.Version), actor)).IsSuccess, "relation_delete");
var restored = await link.CreateAsync(new(combined.Value.Master.Id, tag.Value.Id), actor);
Check(restored.IsSuccess && restored.Value!.Id == linked.Value.Id && restored.Value.Version == 3, "relation_restore");
Console.WriteLine($"Generated Native CRUD passed: {provider}; CRUD/unique/exact JSON/filtered workbench/tree/aggregate rollback/relation lookup and restore.");

static void Check(bool success, string? stage)
{
    if (!success) throw new InvalidOperationException("生成代码原生验收失败：" + stage);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CreateWorkbenchRequest))]
[JsonSerializable(typeof(WorkbenchResponse))]
internal partial class ProbeJsonContext : JsonSerializerContext;
