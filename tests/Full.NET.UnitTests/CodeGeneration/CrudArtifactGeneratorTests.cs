using System.Globalization;
using System.Text.Json;
using Acme.Modules.Catalog.Generated;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CrudArtifactGeneratorTests
{
    [TestMethod]
    public void Generate_emits_sorted_unique_cross_stack_artifacts()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema());

        CollectionAssert.AreEqual(
            new[]
            {
                "backend/ProductContracts.g.cs",
                "backend/ProductEndpoint.g.cs",
                "backend/ProductFeature.g.cs",
                "backend/ProductRecord.g.cs",
                "backend/ProductSql.g.cs",
                "clients/layui/products-page.generated.js",
                "clients/layui/products.generated.js",
                "clients/vue/products-page.generated.ts",
                "clients/vue/products.generated.ts",
                "reports/products.generation.json",
                "templates/migrations/MySql/CreateProduct.sql.template",
                "templates/migrations/SqlServer/CreateProduct.sql.template",
                "templates/tests/ProductMigrationIntegrationTests.cs.template",
            },
            artifacts.Select(artifact => artifact.RelativePath).ToArray());
        Assert.AreEqual(
            artifacts.Count,
            artifacts.Select(artifact => artifact.RelativePath)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.IsTrue(artifacts.All(artifact =>
            artifact.Content.EndsWith('\n')
            && !artifact.Content.Contains('\r', StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Generate_explicit_scope_emits_paired_non_executable_migration_templates()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema());
        var sqlServer = Artifact(
            artifacts,
            "templates/migrations/SqlServer/CreateProduct.sql.template");
        var mySql = Artifact(
            artifacts,
            "templates/migrations/MySql/CreateProduct.sql.template");

        StringAssert.Contains(
            sqlServer,
            "CREATE TABLE dbo.acme_catalog_product");
        StringAssert.Contains(sqlServer, "Id uniqueidentifier NOT NULL");
        StringAssert.Contains(
            sqlServer,
            "CONSTRAINT PK_acme_catalog_product PRIMARY KEY NONCLUSTERED (Id)");
        StringAssert.Contains(
            sqlServer,
            "CREATE CLUSTERED INDEX IX_acme_catalog_product_TenantId_Id");
        StringAssert.Contains(
            sqlServer,
            "ON dbo.acme_catalog_product(TenantId, Id)");

        StringAssert.Contains(
            mySql,
            "CREATE TABLE IF NOT EXISTS acme_catalog_product");
        StringAssert.Contains(mySql, "Id BINARY(16) NOT NULL");
        StringAssert.Contains(
            mySql,
            "CONSTRAINT PK_acme_catalog_product PRIMARY KEY (Id)");
        StringAssert.Contains(
            mySql,
            "KEY IX_acme_catalog_product_TenantId_Id (TenantId, Id)");
        Assert.IsFalse(
            mySql.Contains("char(36)", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Generate_integration_template_targets_both_providers_and_exact_shape()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema());
        var template = Artifact(
            artifacts,
            "templates/tests/ProductMigrationIntegrationTests.cs.template");

        StringAssert.Contains(template, "CreateSqlServerDatabaseAsync()");
        StringAssert.Contains(template, "CreateMySqlDatabaseAsync()");
        StringAssert.Contains(
            template,
            "TABLE_NAME = 'acme_catalog_product'");
        StringAssert.Contains(template, "Assert.AreEqual(7, columnCount);");
        StringAssert.Contains(template, "移除 .template 后缀");
    }

    [TestMethod]
    public void Generate_backend_contracts_and_sql_keep_crud_boundaries()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema());
        var contracts = Artifact(artifacts, "backend/ProductContracts.g.cs");
        var sql = Artifact(artifacts, "backend/ProductSql.g.cs");

        StringAssert.Contains(contracts, "public sealed record ProductResponse(");
        StringAssert.Contains(contracts, "public sealed record CreateProductRequest(");
        StringAssert.Contains(contracts, "public sealed record UpdateProductRequest(");
        StringAssert.Contains(contracts, "public sealed record DisableProductRequest(");
        StringAssert.Contains(
            contracts,
            """[property: JsonPropertyName("displayName")] string Name""");
        StringAssert.Contains(
            contracts,
            "JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString");
        StringAssert.Contains(contracts, "public const string Read = \"catalog.products.read\";");
        StringAssert.Contains(contracts, "public const string Write = \"catalog.products.write\";");

        StringAssert.Contains(sql, "FROM acme_catalog_product");
        StringAssert.Contains(sql, "OFFSET @Offset ROWS");
        StringAssert.Contains(sql, "FETCH NEXT @PageSize ROWS ONLY");
        StringAssert.Contains(sql, "LIMIT @PageSize OFFSET @Offset");
        StringAssert.Contains(sql, "INSERT INTO acme_catalog_product");
        StringAssert.Contains(sql, "WHERE Id = @Id");
        StringAssert.Contains(sql, "AND TenantId = @TenantId");
        StringAssert.Contains(sql, "AND Version = @Version");
        StringAssert.Contains(
            sql,
            "SET IsActive = 0,\n            Version = Version + 1");
        Assert.IsFalse(sql.Contains("SELECT *", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generate_tenant_backend_feature_uses_fullnet_runtime_boundaries()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema());
        var endpoint = Artifact(artifacts, "backend/ProductEndpoint.g.cs");
        var feature = Artifact(artifacts, "backend/ProductFeature.g.cs");
        var record = Artifact(artifacts, "backend/ProductRecord.g.cs");
        var sql = Artifact(artifacts, "backend/ProductSql.g.cs");

        StringAssert.Contains(
            record,
            "internal sealed record ProductRecord(");
        StringAssert.Contains(sql, "using Full.NET.Data.Abstractions;");
        StringAssert.Contains(
            sql,
            "SqlDataScope.TenantRequired");
        StringAssert.Contains(
            sql,
            "SqlTenantBinding.CurrentTenantId");
        StringAssert.Contains(
            sql,
            "\"catalog.list_products.sql_server\"");
        StringAssert.Contains(
            sql,
            "\"catalog.list_products.my_sql\"");

        StringAssert.Contains(feature, "IMultiResultQueryExecutor");
        StringAssert.Contains(
            feature,
            "ReadSingleOrDefaultAsync<long>()");
        StringAssert.Contains(feature, "ICommandTransaction");
        StringAssert.Contains(feature, "idGenerator.NewId()");
        StringAssert.Contains(feature, "EnsureTenantContext()");
        StringAssert.Contains(feature, "ProductErrorCodes.VersionConflict");

        StringAssert.Contains(
            endpoint,
            "MapGroup(\"/api/v1/catalog/products\")");
        StringAssert.Contains(endpoint, "ProductPermissions.Read");
        StringAssert.Contains(endpoint, "ProductPermissions.Write");
        StringAssert.Contains(endpoint, "Results.Created(");
        StringAssert.Contains(
            endpoint,
            "AddGeneratedProductFeature");
        StringAssert.Contains(
            endpoint,
            "MapGeneratedProductFeature");
        StringAssert.Contains(
            endpoint,
            "[JsonSerializable(typeof(PagedResult<ProductResponse>))]");
    }

    [TestMethod]
    public void Generate_non_tenant_schema_omits_runtime_feature_without_explicit_scope()
    {
        var source = FullNetCrudSchemaTests.CreateProductSchema();
        var schema = FullNetCrudSchema.CreateProject(
            source.OwnerKey,
            source.ModuleKey,
            source.EntityKey,
            source.DatabaseTableName,
            source.RootNamespace,
            source.ClrTypeName,
            source.ApiResourceName,
            source.PermissionResourceName,
            isTenantScoped: false,
            hasVersion: source.HasVersion,
            source.Columns
                .Where(column => column.DatabaseName != "TenantId")
                .ToArray());

        var paths = CrudArtifactGenerator.Generate(schema)
            .Select(artifact => artifact.RelativePath)
            .ToArray();

        Assert.IsFalse(paths.Contains(
            "backend/ProductEndpoint.g.cs",
            StringComparer.Ordinal));
        Assert.IsFalse(paths.Contains(
            "backend/ProductFeature.g.cs",
            StringComparer.Ordinal));
        Assert.IsFalse(paths.Contains(
            "backend/ProductRecord.g.cs",
            StringComparer.Ordinal));
        Assert.IsFalse(paths.Any(path =>
            path.StartsWith("templates/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Generate_host_scope_emits_host_only_runtime_without_tenant_binding()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            CreateExplicitScopeSchema(FullNetCrudDataScope.HostOnly));
        var paths = artifacts
            .Select(artifact => artifact.RelativePath)
            .ToArray();
        var sql = Artifact(artifacts, "backend/ProductSql.g.cs");
        var feature = Artifact(artifacts, "backend/ProductFeature.g.cs");
        var migration = Artifact(
            artifacts,
            "templates/migrations/SqlServer/CreateProduct.sql.template");

        CollectionAssert.Contains(paths, "backend/ProductEndpoint.g.cs");
        CollectionAssert.Contains(paths, "backend/ProductFeature.g.cs");
        CollectionAssert.Contains(paths, "backend/ProductRecord.g.cs");
        StringAssert.Contains(sql, "SqlDataScope.HostOnly");
        StringAssert.Contains(sql, "SqlTenantBinding.None");
        Assert.IsFalse(sql.Contains("@TenantId", StringComparison.Ordinal));
        StringAssert.Contains(feature, "EnsureHostContext()");
        StringAssert.Contains(feature, "HostContextRequiredException");
        Assert.IsFalse(feature.Contains(
            "TenantContextMissingException",
            StringComparison.Ordinal));
        StringAssert.Contains(migration, "PRIMARY KEY CLUSTERED (Id)");
        Assert.IsFalse(
            migration.Contains("TenantId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generate_global_scope_emits_global_runtime_without_context_dependency()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            CreateExplicitScopeSchema(FullNetCrudDataScope.Global));
        var sql = Artifact(artifacts, "backend/ProductSql.g.cs");
        var feature = Artifact(artifacts, "backend/ProductFeature.g.cs");
        var migration = Artifact(
            artifacts,
            "templates/migrations/MySql/CreateProduct.sql.template");

        StringAssert.Contains(sql, "SqlDataScope.Global");
        StringAssert.Contains(sql, "SqlTenantBinding.None");
        Assert.IsFalse(sql.Contains("@TenantId", StringComparison.Ordinal));
        Assert.IsFalse(feature.Contains(
            "ICurrentTenant",
            StringComparison.Ordinal));
        Assert.IsFalse(feature.Contains(
            "EnsureTenantContext",
            StringComparison.Ordinal));
        Assert.IsFalse(feature.Contains(
            "EnsureHostContext",
            StringComparison.Ordinal));
        Assert.IsFalse(
            migration.Contains("TenantId", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Compiled_tenant_list_uses_one_provider_specific_round_trip()
    {
        var productId = Guid.Parse("0198abcd-1234-7000-8000-000000000001");
        var tenantId = Guid.Parse("0198abcd-1234-7000-8000-000000000002");
        var executor = new RecordingMultiResultQueryExecutor(
            1,
            new ProductRecord(
                productId,
                tenantId,
                "Product",
                null,
                true,
                7,
                DateTimeOffset.UnixEpoch));
        var service = new ProductQueryService(
            RejectingQueryExecutor.Instance,
            executor,
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.MySql,
            }));

        var result = await service.ListAsync(3, 25);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, executor.CallCount);
        Assert.AreEqual(
            "catalog.list_products.my_sql",
            executor.Statement?.Name);
        Assert.AreEqual(SqlDataScope.TenantRequired, executor.Statement?.Scope);
        Assert.AreEqual(
            SqlTenantBinding.CurrentTenantId,
            executor.Statement?.TenantBinding);
        Assert.AreEqual(50L, ReadParameter<long>(executor.Parameters!, "Offset"));
        Assert.AreEqual(25, ReadParameter<int>(executor.Parameters!, "PageSize"));
        Assert.AreEqual(productId, result.Value!.Items.Single().Id);
    }

    [TestMethod]
    public void Generate_clients_share_api_and_permission_contracts()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema());
        var vue = Artifact(artifacts, "clients/vue/products.generated.ts");
        var layui = Artifact(artifacts, "clients/layui/products.generated.js");

        foreach (var content in new[] { vue, layui })
        {
            StringAssert.Contains(content, "/api/v1/catalog/products");
            StringAssert.Contains(content, "catalog.products.read");
            StringAssert.Contains(content, "catalog.products.write");
        }

        StringAssert.Contains(vue, "export interface ProductResponse");
        StringAssert.Contains(vue, "export function createProductsApi");
        StringAssert.Contains(vue, "description: string | null;");
        StringAssert.Contains(vue, "version: string;");
        StringAssert.Contains(
            vue,
            "disable: (id: string, input: DisableProductRequest)");
        StringAssert.Contains(layui, "export function createProductsApi");
        StringAssert.Contains(layui, "disable(id, input)");
        StringAssert.Contains(layui, "jsonRequest('POST', input)");
    }

    [TestMethod]
    public void Generate_page_models_reuse_clients_and_guard_write_actions()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema());
        var vue = Artifact(
            artifacts,
            "clients/vue/products-page.generated.ts");
        var layui = Artifact(
            artifacts,
            "clients/layui/products-page.generated.js");

        StringAssert.Contains(vue, "from './products.generated';");
        StringAssert.Contains(vue, "export function useProductPage");
        StringAssert.Contains(
            vue,
            "Omit<UpdateProductRequest, 'version'>");
        StringAssert.Contains(vue, "productPermissions.write");
        StringAssert.Contains(vue, "version: item.version");
        StringAssert.Contains(
            vue,
            "'client.catalog_products_load_failed'");
        StringAssert.Contains(
            vue,
            "'client.catalog_products_operation_failed'");

        StringAssert.Contains(layui, "from './products.generated.js';");
        StringAssert.Contains(
            layui,
            "export function createProductPageModel");
        StringAssert.Contains(layui, "productPermissions.write");
        StringAssert.Contains(layui, "version: item.version");
        StringAssert.Contains(
            layui,
            "'client.catalog_products_load_failed'");
        StringAssert.Contains(
            layui,
            "'client.catalog_products_operation_failed'");

        foreach (var content in new[] { vue, layui })
        {
            Assert.IsFalse(
                content.Contains("router", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(
                content.Contains("menu", StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void Generate_page_models_use_explicit_id_and_version_json_names()
    {
        var columns = FullNetCrudSchemaTests.CreateProductSchema().Columns
            .Select(column => column.DatabaseName switch
            {
                "Id" => column with { JsonPropertyName = "productId" },
                "Version" => column with { JsonPropertyName = "rowVersion" },
                _ => column,
            })
            .ToArray();
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema(columns: columns));
        var vue = Artifact(
            artifacts,
            "clients/vue/products-page.generated.ts");
        var layui = Artifact(
            artifacts,
            "clients/layui/products-page.generated.js");

        StringAssert.Contains(
            vue,
            "Omit<UpdateProductRequest, 'rowVersion'>");
        StringAssert.Contains(vue, "item.productId");
        StringAssert.Contains(vue, "rowVersion: item.rowVersion");
        StringAssert.Contains(layui, "item.productId");
        StringAssert.Contains(layui, "rowVersion: item.rowVersion");
    }

    [TestMethod]
    public void Generate_report_preserves_explicit_cross_stack_names()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema());
        using var report = JsonDocument.Parse(Artifact(
            artifacts,
            "reports/products.generation.json"));

        var root = report.RootElement;
        Assert.AreEqual("acme_catalog_product", root.GetProperty("databaseTableName").GetString());
        Assert.AreEqual("Product", root.GetProperty("clrTypeName").GetString());
        Assert.AreEqual("products", root.GetProperty("apiResourceName").GetString());
        var nameColumn = root.GetProperty("columns").EnumerateArray().Single(column =>
            column.GetProperty("databaseName").GetString() == "Name");
        Assert.AreEqual("Name", nameColumn.GetProperty("clrPropertyName").GetString());
        Assert.AreEqual("displayName", nameColumn.GetProperty("jsonPropertyName").GetString());
    }

    [TestMethod]
    public void Generate_report_marks_legacy_entity_capability_shape()
    {
        var legacyArtifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema());

        using var legacyReport = JsonDocument.Parse(Artifact(
            legacyArtifacts,
            "reports/products.generation.json"));

        Assert.IsTrue(legacyReport.RootElement
            .GetProperty("usesLegacyEntityCapabilities")
            .GetBoolean());
        Assert.AreEqual(
            "disable",
            legacyReport.RootElement
                .GetProperty("legacyLifecycle")
                .GetString());
        Assert.AreEqual(
            JsonValueKind.Null,
            legacyReport.RootElement
                .GetProperty("entityCapabilities")
                .ValueKind);
        Assert.AreEqual(
            "single",
            legacyReport.RootElement.GetProperty("scene").GetString());
        Assert.AreEqual(
            "tenant.required",
            legacyReport.RootElement.GetProperty("dataScope").GetString());
    }

    [TestMethod]
    public void Generate_explicit_soft_delete_controls_audit_and_lifecycle_fields_on_the_server()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            CreateExplicitLifecycleSchema());
        var contracts = Artifact(artifacts, "backend/ProductContracts.g.cs");
        var feature = Artifact(artifacts, "backend/ProductFeature.g.cs");
        var endpoint = Artifact(artifacts, "backend/ProductEndpoint.g.cs");
        var sql = Artifact(artifacts, "backend/ProductSql.g.cs");
        var vue = Artifact(artifacts, "clients/vue/products.generated.ts");
        var vuePage = Artifact(
            artifacts,
            "clients/vue/products-page.generated.ts");
        var layui = Artifact(
            artifacts,
            "clients/layui/products.generated.js");
        var layuiPage = Artifact(
            artifacts,
            "clients/layui/products-page.generated.js");
        using var report = JsonDocument.Parse(Artifact(
            artifacts,
            "reports/products.generation.json"));

        StringAssert.Contains(
            sql,
            "WHERE Id = @Id\n"
            + "            AND TenantId = @TenantId\n"
            + "            AND Version = @Version\n"
            + "            AND IsDeleted = 0;");
        StringAssert.Contains(
            sql,
            "SET IsDeleted = 1,\n"
            + "            DeletedAtUtc = @DeletedAtUtc,\n"
            + "            DeletedById = @DeletedById,\n"
            + "            Version = Version + 1");
        StringAssert.Contains(sql, "AND IsDeleted = 0");
        StringAssert.Contains(feature, "CreatedById = actorUserId");
        StringAssert.Contains(feature, "UpdatedById = actorUserId");
        StringAssert.Contains(feature, "DeletedById = actorUserId");
        StringAssert.Contains(feature, "DeletedAtUtc = clock.UtcNow");
        StringAssert.Contains(endpoint, "ClaimsPrincipal principal");
        StringAssert.Contains(endpoint, "FullNetIdentityClaimTypes.Subject");
        StringAssert.Contains(endpoint, "MapPost(\"/{productId:guid}/delete\"");
        StringAssert.Contains(
            vue,
            "update: (id: string, input: UpdateProductRequest)");
        StringAssert.Contains(
            vue,
            "delete: (id: string, input: DeleteProductRequest)");
        StringAssert.Contains(vuePage, "version: item.version");
        StringAssert.Contains(layui, "update(id, input)");
        StringAssert.Contains(layui, "delete(id, input)");
        StringAssert.Contains(layuiPage, "version: item.version");
        Assert.AreEqual(
            "soft.delete",
            report.RootElement
                .GetProperty("entityCapabilities")
                .GetProperty("deleteMode")
                .GetString());
        Assert.AreEqual(
            "none",
            report.RootElement
                .GetProperty("entityCapabilities")
                .GetProperty("ownershipMode")
                .GetString());
        var createContractStart = contracts.IndexOf(
            "public sealed record CreateProductRequest(",
            StringComparison.Ordinal);
        var createContractEnd = contracts.IndexOf(
            "public sealed record UpdateProductRequest(",
            createContractStart,
            StringComparison.Ordinal);
        var createContract = contracts[
            createContractStart..createContractEnd];
        foreach (var serverControlledField in new[]
        {
            "CreatedAtUtc",
            "CreatedById",
            "UpdatedAtUtc",
            "UpdatedById",
            "IsDeleted",
            "DeletedAtUtc",
            "DeletedById",
            "Version",
            "TenantId",
        })
        {
            Assert.IsFalse(createContract.Contains(
                serverControlledField,
                StringComparison.Ordinal));
        }
        Assert.IsFalse(sql.Contains("IsActive", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generate_immutable_entity_omits_update_and_delete_entry_points()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            CreateImmutableSchema());
        var contracts = Artifact(artifacts, "backend/ProductContracts.g.cs");
        var endpoint = Artifact(artifacts, "backend/ProductEndpoint.g.cs");
        var vue = Artifact(artifacts, "clients/vue/products.generated.ts");
        var vuePage = Artifact(
            artifacts,
            "clients/vue/products-page.generated.ts");
        var layui = Artifact(
            artifacts,
            "clients/layui/products.generated.js");
        var layuiPage = Artifact(
            artifacts,
            "clients/layui/products-page.generated.js");

        StringAssert.Contains(contracts, "CreateProductRequest");
        Assert.IsFalse(contracts.Contains(
            "UpdateProductRequest",
            StringComparison.Ordinal));
        Assert.IsFalse(endpoint.Contains("MapPut(", StringComparison.Ordinal));
        Assert.IsFalse(endpoint.Contains("/delete", StringComparison.Ordinal));
        Assert.IsFalse(endpoint.Contains("/disable", StringComparison.Ordinal));
        Assert.IsFalse(vue.Contains("update:", StringComparison.Ordinal));
        Assert.IsFalse(vue.Contains("delete:", StringComparison.Ordinal));
        Assert.IsFalse(vue.Contains("disable:", StringComparison.Ordinal));
        Assert.IsFalse(vuePage.Contains(
            "async function update",
            StringComparison.Ordinal));
        Assert.IsFalse(vuePage.Contains(
            "async function remove",
            StringComparison.Ordinal));
        Assert.IsFalse(layui.Contains("update(", StringComparison.Ordinal));
        Assert.IsFalse(layui.Contains("delete(", StringComparison.Ordinal));
        Assert.IsFalse(layuiPage.Contains(
            "async function update",
            StringComparison.Ordinal));
        Assert.IsFalse(layuiPage.Contains(
            "async function remove",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generate_rejects_tree_until_parent_scope_and_cycle_guards_exist()
    {
        var error = Assert.ThrowsExactly<NotSupportedException>(() =>
            CrudArtifactGenerator.Generate(CreateTreeSchema()));

        StringAssert.Contains(error.Message, "同租户父节点");
        StringAssert.Contains(error.Message, "环");
    }

    [TestMethod]
    public void Generate_explicit_hard_delete_uses_physical_delete_without_soft_delete_fields()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            CreateHardDeleteSchema());
        var sql = Artifact(artifacts, "backend/ProductSql.g.cs");

        StringAssert.Contains(sql, "DELETE FROM acme_catalog_product");
        StringAssert.Contains(sql, "AND Version = @Version");
        Assert.IsFalse(sql.Contains("IsDeleted", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("DeletedAtUtc", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("DeletedById", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generate_explicit_dual_provider_templates_emit_only_declared_lifecycle_columns()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            CreateExplicitLifecycleSchema());
        var sqlServer = Artifact(
            artifacts,
            "templates/migrations/SqlServer/CreateProduct.sql.template");
        var mySql = Artifact(
            artifacts,
            "templates/migrations/MySql/CreateProduct.sql.template");

        foreach (var template in new[] { sqlServer, mySql })
        {
            StringAssert.Contains(template, "IsDeleted");
            StringAssert.Contains(template, "DeletedAtUtc");
            StringAssert.Contains(template, "DeletedById");
            StringAssert.Contains(template, "CreatedById");
            StringAssert.Contains(template, "UpdatedById");
            Assert.IsFalse(template.Contains(
                "IsActive",
                StringComparison.Ordinal));
            Assert.IsFalse(template.Contains(
                "OrganizationUnitId",
                StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void Generate_rejects_relational_and_organization_ownership_until_safe_ports_exist()
    {
        var relationship = new FullNetCrudRelationship(
            PrincipalEntityKey: "product",
            PrincipalColumnName: "Id",
            PrincipalDataScope: FullNetCrudDataScope.TenantRequired,
            DependentEntityKey: "product_item",
            DependentColumnName: "ProductId",
            DependentDataScope: FullNetCrudDataScope.TenantRequired);
        var relational = CreateExplicitLifecycleSchema(
            scene: FullNetCrudScene.MasterDetail,
            relationships: [relationship]);
        var organizationOwned = CreateExplicitLifecycleSchema(
            ownershipMode: FullNetCrudOwnershipMode.OrganizationUnit);

        var relationalError = Assert.ThrowsExactly<NotSupportedException>(() =>
            CrudArtifactGenerator.Generate(relational));
        var ownershipError = Assert.ThrowsExactly<NotSupportedException>(() =>
            CrudArtifactGenerator.Generate(organizationOwned));

        StringAssert.Contains(relationalError.Message, "聚合事务");
        StringAssert.Contains(ownershipError.Message, "可信组织");
    }

    [TestMethod]
    public void Generate_builds_valid_client_identifier_for_kebab_case_resource()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema(
                apiResourceName: "product-items"));

        StringAssert.Contains(
            Artifact(artifacts, "clients/vue/product-items.generated.ts"),
            "export function createProductItemsApi");
        StringAssert.Contains(
            Artifact(artifacts, "clients/layui/product-items.generated.js"),
            "export function createProductItemsApi");
        StringAssert.Contains(
            Artifact(
                artifacts,
                "clients/vue/product-items-page.generated.ts"),
            "from './product-items.generated';");
        StringAssert.Contains(
            Artifact(
                artifacts,
                "clients/layui/product-items-page.generated.js"),
            "from './product-items.generated.js';");
    }

    [TestMethod]
    public void Generate_uses_string_wire_format_for_int64_and_decimal()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema(
                columns:
                [
                    new("Id", "Id", "id", FullNetScalarType.Uuid),
                    new(
                        "TenantId",
                        "TenantId",
                        "tenantId",
                        FullNetScalarType.Uuid),
                    new(
                        "Name",
                        "Name",
                        "displayName",
                        FullNetScalarType.String,
                        MaxLength: 200),
                    new(
                        "Price",
                        "Price",
                        "price",
                        FullNetScalarType.Decimal,
                        NumericPrecision: 18,
                        NumericScale: 2),
                    new(
                        "IsActive",
                        "IsActive",
                        "isActive",
                        FullNetScalarType.Boolean),
                    new(
                        "Version",
                        "Version",
                        "version",
                        FullNetScalarType.Int64),
                    new(
                        "CreatedAtUtc",
                        "CreatedAtUtc",
                        "createdAtUtc",
                        FullNetScalarType.DateTimeUtc),
                ]));
        var vue = Artifact(artifacts, "clients/vue/products.generated.ts");
        var contracts = Artifact(artifacts, "backend/ProductContracts.g.cs");
        var sqlServerMigration = Artifact(
            artifacts,
            "templates/migrations/SqlServer/CreateProduct.sql.template");
        var mySqlMigration = Artifact(
            artifacts,
            "templates/migrations/MySql/CreateProduct.sql.template");
        using var report = JsonDocument.Parse(Artifact(
            artifacts,
            "reports/products.generation.json"));
        var priceReport = report.RootElement
            .GetProperty("columns")
            .EnumerateArray()
            .Single(column =>
                column.GetProperty("databaseName").GetString() == "Price");

        StringAssert.Contains(vue, "price: string;");
        StringAssert.Contains(vue, "version: string;");
        StringAssert.Contains(
            contracts,
            """JsonNumberHandling.WriteAsString)] decimal Price""");
        StringAssert.Contains(
            sqlServerMigration,
            "Price decimal(18, 2) NOT NULL");
        StringAssert.Contains(
            mySqlMigration,
            "Price decimal(18, 2) NOT NULL");
        Assert.IsFalse(sqlServerMigration.Contains(
            "precision required",
            StringComparison.Ordinal));
        Assert.AreEqual(
            18,
            priceReport.GetProperty("numericPrecision").GetInt32());
        Assert.AreEqual(
            2,
            priceReport.GetProperty("numericScale").GetInt32());
    }

    [TestMethod]
    public void Compiled_contract_honors_explicit_json_names_and_safe_int64_wire_format()
    {
        const long version = 9_007_199_254_740_993;
        var response = new ProductResponse(
            Guid.Parse("0198abcd-1234-7000-8000-000000000001"),
            Guid.Parse("0198abcd-1234-7000-8000-000000000002"),
            "精确名称",
            null,
            true,
            version,
            DateTimeOffset.Parse(
                "2026-07-29T00:00:00+00:00",
                CultureInfo.InvariantCulture));

        var json = JsonSerializer.Serialize(response);

        using var serialized = JsonDocument.Parse(json);
        Assert.AreEqual(
            "精确名称",
            serialized.RootElement.GetProperty("displayName").GetString());
        Assert.AreEqual(
            "9007199254740993",
            serialized.RootElement.GetProperty("version").GetString());
        Assert.AreEqual(
            JsonValueKind.Null,
            serialized.RootElement.GetProperty("description").ValueKind);
        var update = JsonSerializer.Deserialize<UpdateProductRequest>(
            """
            {
              "displayName": "更新名称",
              "isActive": true,
              "version": "9007199254740993"
            }
            """);
        Assert.IsNotNull(update);
        Assert.AreEqual(version, update.Version);
        Assert.AreEqual("更新名称", update.Name);
    }

    [TestMethod]
    public void Generate_is_byte_stable_across_repetition_and_current_culture()
    {
        var schema = FullNetCrudSchemaTests.CreateProductSchema();
        var first = CrudArtifactGenerator.Generate(schema);
        IReadOnlyList<GeneratedArtifact> second;
        using (new CultureScope("tr-TR"))
        {
            second = CrudArtifactGenerator.Generate(schema);
        }

        CollectionAssert.AreEqual(
            first.Select(SerializeArtifact).ToArray(),
            second.Select(SerializeArtifact).ToArray());
    }

    [TestMethod]
    public void Generate_matches_compiled_catalog_product_fixtures()
    {
        var artifacts = CrudArtifactGenerator.Generate(
            FullNetCrudSchemaTests.CreateProductSchema());
        var fixtureRoot = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "Full.NET.UnitTests",
            "CodeGeneration",
            "Fixtures",
            "CatalogProduct");

        foreach (var artifact in artifacts)
        {
            var fixturePath = Path.Combine(
                fixtureRoot,
                artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(
                File.Exists(fixturePath),
                $"缺少代码生成固定夹具：{artifact.RelativePath}");
            Assert.AreEqual(
                artifact.Content,
                File.ReadAllText(fixturePath),
                $"代码生成固定夹具漂移：{artifact.RelativePath}");
        }
    }

    private static string Artifact(
        IEnumerable<GeneratedArtifact> artifacts,
        string relativePath) =>
        artifacts.Single(artifact => artifact.RelativePath == relativePath).Content;

    private static FullNetCrudSchema CreateExplicitScopeSchema(
        FullNetCrudDataScope dataScope)
    {
        var source = FullNetCrudSchemaTests.CreateProductSchema();
        return FullNetCrudSchema.CreateProject(
            source.OwnerKey,
            source.ModuleKey,
            source.EntityKey,
            source.DatabaseTableName,
            source.RootNamespace,
            source.ClrTypeName,
            source.ApiResourceName,
            source.PermissionResourceName,
            dataScope,
            source.HasVersion,
            source.Columns
                .Where(column => column.DatabaseName != "TenantId")
                .ToArray());
    }

    private static IReadOnlyList<FullNetColumn> CapabilityColumns() =>
    [
        .. FullNetCrudSchemaTests.CreateProductSchema().Columns,
        new(
            "CreatedById",
            "CreatedById",
            "createdById",
            FullNetScalarType.Uuid),
        new(
            "UpdatedAtUtc",
            "UpdatedAtUtc",
            "updatedAtUtc",
            FullNetScalarType.DateTimeUtc,
            IsNullable: true),
        new(
            "UpdatedById",
            "UpdatedById",
            "updatedById",
            FullNetScalarType.Uuid,
            IsNullable: true),
        new(
            "IsDeleted",
            "IsDeleted",
            "isDeleted",
            FullNetScalarType.Boolean),
        new(
            "DeletedAtUtc",
            "DeletedAtUtc",
            "deletedAtUtc",
            FullNetScalarType.DateTimeUtc,
            IsNullable: true),
        new(
            "DeletedById",
            "DeletedById",
            "deletedById",
            FullNetScalarType.Uuid,
            IsNullable: true),
        new(
            "OrganizationUnitId",
            "OrganizationUnitId",
            "organizationUnitId",
            FullNetScalarType.Uuid),
    ];

    private static FullNetCrudSchema CreateExplicitLifecycleSchema(
        FullNetCrudScene scene = FullNetCrudScene.Single,
        IReadOnlyList<FullNetCrudRelationship>? relationships = null,
        FullNetCrudOwnershipMode ownershipMode = FullNetCrudOwnershipMode.None)
    {
        var columns = new List<FullNetColumn>
        {
            new("Id", "Id", "id", FullNetScalarType.Uuid),
            new("TenantId", "TenantId", "tenantId", FullNetScalarType.Uuid),
            new(
                "Name",
                "Name",
                "displayName",
                FullNetScalarType.String,
                MaxLength: 200),
            new("Version", "Version", "version", FullNetScalarType.Int64),
            new(
                "CreatedAtUtc",
                "CreatedAtUtc",
                "createdAtUtc",
                FullNetScalarType.DateTimeUtc),
            new(
                "CreatedById",
                "CreatedById",
                "createdById",
                FullNetScalarType.Uuid),
            new(
                "UpdatedAtUtc",
                "UpdatedAtUtc",
                "updatedAtUtc",
                FullNetScalarType.DateTimeUtc,
                IsNullable: true),
            new(
                "UpdatedById",
                "UpdatedById",
                "updatedById",
                FullNetScalarType.Uuid,
                IsNullable: true),
            new(
                "IsDeleted",
                "IsDeleted",
                "isDeleted",
                FullNetScalarType.Boolean),
            new(
                "DeletedAtUtc",
                "DeletedAtUtc",
                "deletedAtUtc",
                FullNetScalarType.DateTimeUtc,
                IsNullable: true),
            new(
                "DeletedById",
                "DeletedById",
                "deletedById",
                FullNetScalarType.Uuid,
                IsNullable: true),
        };
        if (ownershipMode == FullNetCrudOwnershipMode.OrganizationUnit)
        {
            columns.Add(new(
                "OrganizationUnitId",
                "OrganizationUnitId",
                "organizationUnitId",
                FullNetScalarType.Uuid));
        }

        return FullNetCrudSchema.CreateProject(
            ownerKey: "acme",
            moduleKey: "catalog",
            entityKey: "product",
            databaseTableName: "acme_catalog_product",
            rootNamespace: "Acme.Modules.Catalog",
            clrTypeName: "Product",
            apiResourceName: "products",
            permissionResourceName: "products",
            FullNetCrudDataScope.TenantRequired,
            new FullNetCrudEntityCapabilities(
                FullNetCrudDeleteMode.SoftDelete,
                HasCreatedAudit: true,
                HasUpdatedAudit: true,
                HasDeletedAudit: true,
                HasVersion: true,
                ownershipMode),
            scene,
            relationships ?? [],
            columns);
    }

    private static FullNetCrudSchema CreateImmutableSchema() =>
        FullNetCrudSchema.CreateProject(
            ownerKey: "acme",
            moduleKey: "catalog",
            entityKey: "product",
            databaseTableName: "acme_catalog_product",
            rootNamespace: "Acme.Modules.Catalog",
            clrTypeName: "Product",
            apiResourceName: "products",
            permissionResourceName: "products",
            FullNetCrudDataScope.TenantRequired,
            new FullNetCrudEntityCapabilities(
                FullNetCrudDeleteMode.Immutable,
                HasCreatedAudit: true,
                HasUpdatedAudit: false,
                HasDeletedAudit: false,
                HasVersion: false,
                FullNetCrudOwnershipMode.None),
            FullNetCrudScene.Single,
            [],
            [
                new("Id", "Id", "id", FullNetScalarType.Uuid),
                new(
                    "TenantId",
                    "TenantId",
                    "tenantId",
                    FullNetScalarType.Uuid),
                new(
                    "Name",
                    "Name",
                    "displayName",
                    FullNetScalarType.String,
                    MaxLength: 200),
                new(
                    "CreatedAtUtc",
                    "CreatedAtUtc",
                    "createdAtUtc",
                    FullNetScalarType.DateTimeUtc),
                new(
                    "CreatedById",
                    "CreatedById",
                    "createdById",
                    FullNetScalarType.Uuid),
            ]);

    private static FullNetCrudSchema CreateTreeSchema() =>
        CreateHardDeleteSchema(FullNetCrudScene.Tree);

    private static FullNetCrudSchema CreateHardDeleteSchema(
        FullNetCrudScene scene = FullNetCrudScene.Single) =>
        FullNetCrudSchema.CreateProject(
            ownerKey: "acme",
            moduleKey: "catalog",
            entityKey: "product",
            databaseTableName: "acme_catalog_product",
            rootNamespace: "Acme.Modules.Catalog",
            clrTypeName: "Product",
            apiResourceName: "products",
            permissionResourceName: "products",
            FullNetCrudDataScope.TenantRequired,
            new FullNetCrudEntityCapabilities(
                FullNetCrudDeleteMode.HardDelete,
                HasCreatedAudit: false,
                HasUpdatedAudit: false,
                HasDeletedAudit: false,
                HasVersion: true,
                FullNetCrudOwnershipMode.None),
            scene,
            [],
            new FullNetColumn[]
            {
                new("Id", "Id", "id", FullNetScalarType.Uuid),
                new(
                    "TenantId",
                    "TenantId",
                    "tenantId",
                    FullNetScalarType.Uuid),
            }
            .Concat(scene == FullNetCrudScene.Tree
                ?
                [
                    new FullNetColumn(
                        "ParentId",
                        "ParentId",
                        "parentId",
                        FullNetScalarType.Uuid,
                        IsNullable: true),
                ]
                : [])
            .Concat(
            [
                new(
                    "Name",
                    "Name",
                    "displayName",
                    FullNetScalarType.String,
                    MaxLength: 200),
                new("Version", "Version", "version", FullNetScalarType.Int64),
            ])
            .ToArray());

    private static string SerializeArtifact(GeneratedArtifact artifact) =>
        $"{artifact.Kind}\n{artifact.RelativePath}\n{artifact.Content}";

    private static T ReadParameter<T>(object parameters, string name) =>
        (T)parameters.GetType().GetProperty(name)!.GetValue(parameters)!;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 Full.NET 仓库根目录。");
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string culture)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }

    private sealed class RecordingMultiResultQueryExecutor(
        long total,
        ProductRecord row) : IMultiResultQueryExecutor
    {
        public int CallCount { get; private set; }

        public SqlStatement? Statement { get; private set; }

        public object? Parameters { get; private set; }

        public Task<TResult> QueryMultipleAsync<TResult>(
            SqlStatement statement,
            object? parameters,
            Func<IMultiResultReader, CancellationToken, Task<TResult>> projector,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Statement = statement;
            Parameters = parameters;
            return projector(
                new ScriptedMultiResultReader(total, row),
                cancellationToken);
        }
    }

    private sealed class ScriptedMultiResultReader(
        long total,
        ProductRecord row) : IMultiResultReader
    {
        public Task<T?> ReadSingleOrDefaultAsync<T>() =>
            Task.FromResult((T?)(object)total);

        public Task<IReadOnlyList<T>> ReadAsync<T>() =>
            Task.FromResult<IReadOnlyList<T>>([(T)(object)row]);
    }

    private sealed class RejectingQueryExecutor : IQueryExecutor
    {
        public static RejectingQueryExecutor Instance { get; } = new();

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "分页列表不得发起额外的普通单结果查询。");

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "分页列表不得发起额外的普通列表查询。");
    }
}
