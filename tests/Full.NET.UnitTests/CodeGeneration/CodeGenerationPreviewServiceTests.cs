using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Modules.CodeGeneration;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Features.PreviewCrudGeneration;
using Full.NET.Modules.CodeGeneration.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationPreviewServiceTests
{
    [TestMethod]
    public void Authorization_catalog_exposes_host_only_preview_navigation()
    {
        var contributor = new CodeGenerationAuthorizationContributor();

        CollectionAssert.AreEquivalent(
            new[]
            {
                CodeGenerationPreviewPermissions.Read,
                CodeGenerationTemplatePermissions.Read,
                CodeGenerationTemplatePermissions.Create,
                CodeGenerationTemplatePermissions.Update,
                CodeGenerationTemplatePermissions.Delete,
                CodeGenerationRunPermissions.Read,
                CodeGenerationRunPermissions.Execute,
                CodeGenerationRunPermissions.Apply,
                CodeGenerationRunPermissions.Rollback,
                CodeGenerationRunPermissions.Download,
                CodeGenerationCatalogPermissions.Read,
            },
            contributor.Permissions
                .Select(permission => permission.Code)
                .ToArray());
        Assert.IsTrue(contributor.Permissions.All(
            permission => permission.Scope == AuthorizationScope.Host));

        var templatesNavigation = contributor.Navigation.Single(
            item => item.Id == "code-generation-templates");
        Assert.AreEqual(
            "/code-generation/templates",
            templatesNavigation.Path);
        Assert.AreEqual(
            CodeGenerationTemplatePermissions.Read,
            templatesNavigation.RequiredPermission);

        var previewsNavigation = contributor.Navigation.Single(
            item => item.Id == "code-generation-previews");
        Assert.AreEqual(
            "/code-generation/previews",
            previewsNavigation.Path);
        Assert.AreEqual(
            CodeGenerationPreviewPermissions.Read,
            previewsNavigation.RequiredPermission);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "codegen.templates.create",
                "codegen.templates.update",
                "codegen.templates.delete",
                "codegen.catalog.read",
                "codegen.runs.execute",
                "codegen.runs.apply",
                "codegen.runs.rollback",
                "codegen.runs.download",
            },
            contributor.Actions.Select(action => action.Id).ToArray());
    }

    [TestMethod]
    public void Module_registers_run_orchestration_apply_and_validated_options()
    {
        var services = new ServiceCollection();

        new CodeGenerationModule().AddServices(
            services,
            new ConfigurationBuilder().Build());

        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(CodeGenerationRunService)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(CodeGenerationRunQueryService)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(CodeGenerationApplyService)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(CodeGenerationRollbackService)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType
                == typeof(IValidateOptions<CodeGenerationApplyOptions>)
            && descriptor.ImplementationType
                == typeof(CodeGenerationApplyOptionsValidator)
            && descriptor.Lifetime == ServiceLifetime.Singleton));
    }

    [TestMethod]
    public void Preview_returns_real_generator_artifacts_in_stable_order()
    {
        var service = new CodeGenerationPreviewService();

        var result = service.Preview(CreateRequest());

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("acme_catalog_product", result.Value!.DatabaseTableName);
        Assert.AreEqual("catalog.products.read", result.Value.ReadPermission);
        Assert.AreEqual("catalog.products.write", result.Value.WritePermission);
        Assert.IsTrue(result.Value.Artifacts.Count >= 7);
        CollectionAssert.AreEqual(
            result.Value.Artifacts
                .Select(artifact => artifact.Path)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray(),
            result.Value.Artifacts
                .Select(artifact => artifact.Path)
                .ToArray());
        Assert.IsTrue(result.Value.Artifacts.All(
            artifact => artifact.Sha256.Length == 64));
        StringAssert.Contains(
            result.Value.Artifacts.Single(
                artifact => artifact.Path == "clients/vue/products.generated.ts")
                .Content,
            "/api/v1/catalog/products");
    }

    [TestMethod]
    public void Preview_rejects_unknown_scalar_type_without_leaking_input()
    {
        var request = CreateRequest() with
        {
            Columns =
            [
                .. CreateRequest().Columns,
                new CodeGenerationPreviewColumnRequest(
                    "Secret",
                    "Secret",
                    "secret",
                    "Executable",
                    false,
                    null,
                    null,
                    null),
            ],
        };

        var result = new CodeGenerationPreviewService().Preview(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationErrorCodes.InvalidPreviewSchema,
            result.Error!.Code);
        Assert.IsFalse(
            result.Error.Message.Contains(
                "Executable",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void Preview_rejects_numeric_scalar_type_machine_code()
    {
        var validRequest = CreateRequest();
        var request = validRequest with
        {
            Columns = validRequest.Columns
                .Select((column, index) => index == 0
                    ? column with { ScalarType = "1" }
                    : column)
                .ToArray(),
        };

        var result = new CodeGenerationPreviewService().Preview(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationErrorCodes.InvalidPreviewSchema,
            result.Error!.Code);
    }

    [TestMethod]
    public void Preview_rejects_numeric_data_scope_machine_code()
    {
        var request = CreateRequest() with
        {
            DataScope = "1",
        };

        var result = new CodeGenerationPreviewService().Preview(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationErrorCodes.InvalidPreviewSchema,
            result.Error!.Code);
    }

    [TestMethod]
    public void Preview_rejects_more_than_128_columns()
    {
        var request = CreateRequest() with
        {
            Columns = Enumerable
                .Range(0, 129)
                .Select(index => new CodeGenerationPreviewColumnRequest(
                    $"Field{index}",
                    $"Field{index}",
                    $"field{index}",
                    "String",
                    false,
                    64,
                    null,
                    null))
                .ToArray(),
        };

        var result = new CodeGenerationPreviewService().Preview(request);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationErrorCodes.InvalidPreviewSchema,
            result.Error!.Code);
    }

    [TestMethod]
    public void Schema_normalizer_accepts_explicit_capabilities_and_emits_canonical_json()
    {
        var result = new CodeGenerationSchemaNormalizer().Normalize(
            CreateExplicitRequest());

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value!.Schema.UsesLegacyEntityCapabilities);
        Assert.AreEqual(
            "hard.delete",
            result.Value.CanonicalRequest.EntityCapabilities!.DeleteMode);
        Assert.AreEqual("single", result.Value.CanonicalRequest.Scene);
        StringAssert.Contains(
            result.Value.CanonicalJson,
            "\"dataScope\":\"tenant.required\"");
        StringAssert.Contains(
            result.Value.CanonicalJson,
            "\"scalarType\":\"uuid\"");
        StringAssert.Contains(
            result.Value.CanonicalJson,
            "\"deleteMode\":\"hard.delete\"");
        Assert.IsFalse(
            result.Value.CanonicalJson.Contains(
                "\"hasVersion\":null",
                StringComparison.Ordinal));
        Assert.AreEqual(64, result.Value.SchemaSha256.Length);
    }

    [TestMethod]
    public void Schema_normalizer_requires_exactly_one_capability_shape()
    {
        var mixed = CreateExplicitRequest() with { HasVersion = true };
        var missing = CreateRequest() with { HasVersion = null };
        var nullRelationships = CreateExplicitRequest() with
        {
            Relationships = null,
        };

        foreach (var request in new[] { mixed, missing, nullRelationships })
        {
            var result = new CodeGenerationSchemaNormalizer().Normalize(request);

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(
                CodeGenerationErrorCodes.InvalidPreviewSchema,
                result.Error!.Code);
        }
    }

    [TestMethod]
    public void Preview_accepts_explicit_capabilities_through_shared_normalizer()
    {
        var service = new CodeGenerationPreviewService(
            new CodeGenerationSchemaNormalizer());

        var result = service.Preview(CreateExplicitRequest());

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value!.Artifacts.Count >= 7);
    }

    [TestMethod]
    public void Preview_organization_owned_emits_write_authorizer_and_scope_fragments()
    {
        var service = new CodeGenerationPreviewService(
            new CodeGenerationSchemaNormalizer());
        var result = service.Preview(CreateOrganizationOwnedRequest());

        Assert.IsTrue(result.IsSuccess);
        var feature = result.Value!.Artifacts
            .Single(artifact => artifact.Path == "backend/ProductFeature.g.cs")
            .Content;
        var contracts = result.Value.Artifacts
            .Single(artifact => artifact.Path == "backend/ProductContracts.g.cs")
            .Content;
        StringAssert.Contains(feature, "IOrganizationOwnedEntityWriteAuthorizer");
        StringAssert.Contains(feature, "BuildOrganizationUnitFilter");
        StringAssert.Contains(feature, "OrganizationUnitId = organizationUnitId");
        Assert.IsFalse(contracts.Contains(
            "CreateProductRequest(\n    string Name,\n    Guid OrganizationUnitId",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void Preview_rejects_organization_unit_ownership_on_host_or_global_scope()
    {
        var service = new CodeGenerationPreviewService(
            new CodeGenerationSchemaNormalizer());
        foreach (var dataScope in new[] { "host.only", "global" })
        {
            var organizationOwned = CreateOrganizationOwnedRequest();
            var request = organizationOwned with
            {
                DataScope = dataScope,
                Columns = organizationOwned.Columns
                    .Where(column =>
                        !string.Equals(
                            column.ClrPropertyName,
                            "TenantId",
                            StringComparison.Ordinal))
                    .ToArray(),
            };

            var result = service.Preview(request);

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(
                CodeGenerationErrorCodes.InvalidPreviewSchema,
                result.Error!.Code);
        }
    }

    [TestMethod]
    public void Schema_contract_rejects_unknown_fields_at_every_nested_level()
    {
        var canonical = JsonSerializer.Serialize(
            CreateExplicitRequest(),
            CodeGenerationJsonSerializerContext.Default
                .CodeGenerationPreviewRequest);
        var mutations = new Action<JsonObject>[]
        {
            root => root["unexpected"] = true,
            root => root["entityCapabilities"]!
                .AsObject()["unexpected"] = true,
            root => root["columns"]!
                .AsArray()[0]!
                .AsObject()["unexpected"] = true,
            root => root["relationships"]!
                .AsArray()
                .Add(new JsonObject
                {
                    ["principalEntityKey"] = "catalog",
                    ["principalColumnName"] = "Id",
                    ["principalDataScope"] = "tenant.required",
                    ["dependentEntityKey"] = "product",
                    ["dependentColumnName"] = "CatalogId",
                    ["dependentDataScope"] = "tenant.required",
                    ["unexpected"] = true,
                }),
        };

        foreach (var mutate in mutations)
        {
            var root = JsonNode.Parse(canonical)!.AsObject();
            mutate(root);

            Assert.ThrowsExactly<JsonException>(() =>
                JsonSerializer.Deserialize(
                    root.ToJsonString(),
                    CodeGenerationJsonSerializerContext.Default
                        .CodeGenerationPreviewRequest));
        }
    }

    [TestMethod]
    public void Preview_propagates_cancellation_before_generation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            new CodeGenerationPreviewService().Preview(
                CreateRequest(),
                cancellation.Token));
    }

    private static CodeGenerationPreviewRequest CreateRequest() =>
        new(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "TenantRequired",
            true,
            [
                new(
                    "Id",
                    "Id",
                    "id",
                    "Uuid",
                    false,
                    null,
                    null,
                    null),
                new(
                    "TenantId",
                    "TenantId",
                    "tenantId",
                    "Uuid",
                    false,
                    null,
                    null,
                    null),
                new(
                    "Name",
                    "Name",
                    "displayName",
                    "String",
                    false,
                    200,
                    null,
                    null),
                new(
                    "IsActive",
                    "IsActive",
                    "isActive",
                    "Boolean",
                    false,
                    null,
                    null,
                    null),
                new(
                    "Version",
                    "Version",
                    "version",
                    "Int64",
                    false,
                    null,
                    null,
                    null),
            ]);

    private static CodeGenerationPreviewRequest CreateExplicitRequest()
    {
        var legacy = CreateRequest();
        return legacy with
        {
            DataScope = "tenant.required",
            HasVersion = null,
            EntityCapabilities = new CodeGenerationEntityCapabilitiesRequest(
                "hard.delete",
                HasCreatedAudit: false,
                HasUpdatedAudit: false,
                HasDeletedAudit: false,
                HasVersion: true,
                "none"),
            Scene = "single",
            Relationships = [],
            Columns = legacy.Columns
                .Select(column => column with
                {
                    ScalarType = column.ScalarType switch
                    {
                        "Uuid" => "uuid",
                        "String" => "string",
                        "Boolean" => "boolean",
                        "Int64" => "int64",
                        _ => column.ScalarType,
                    },
                })
                .ToArray(),
        };
    }

    private static CodeGenerationPreviewRequest CreateOrganizationOwnedRequest()
    {
        return new CodeGenerationPreviewRequest(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "tenant.required",
            null,
            [
                new("Id", "Id", "id", "uuid", false, null, null, null),
                new("TenantId", "TenantId", "tenantId", "uuid", false, null, null, null),
                new(
                    "OrganizationUnitId",
                    "OrganizationUnitId",
                    "organizationUnitId",
                    "uuid",
                    false,
                    null,
                    null,
                    null),
                new(
                    "Name",
                    "Name",
                    "displayName",
                    "string",
                    false,
                    200,
                    null,
                    null),
                new("Version", "Version", "version", "int64", false, null, null, null),
                new(
                    "CreatedAtUtc",
                    "CreatedAtUtc",
                    "createdAtUtc",
                    "date.time.utc",
                    false,
                    null,
                    null,
                    null),
                new(
                    "CreatedById",
                    "CreatedById",
                    "createdById",
                    "uuid",
                    false,
                    null,
                    null,
                    null),
                new(
                    "UpdatedAtUtc",
                    "UpdatedAtUtc",
                    "updatedAtUtc",
                    "date.time.utc",
                    true,
                    null,
                    null,
                    null),
                new(
                    "UpdatedById",
                    "UpdatedById",
                    "updatedById",
                    "uuid",
                    true,
                    null,
                    null,
                    null),
                new(
                    "IsDeleted",
                    "IsDeleted",
                    "isDeleted",
                    "boolean",
                    false,
                    null,
                    null,
                    null),
                new(
                    "DeletedAtUtc",
                    "DeletedAtUtc",
                    "deletedAtUtc",
                    "date.time.utc",
                    true,
                    null,
                    null,
                    null),
                new(
                    "DeletedById",
                    "DeletedById",
                    "deletedById",
                    "uuid",
                    true,
                    null,
                    null,
                    null),
            ],
            new CodeGenerationEntityCapabilitiesRequest(
                "soft.delete",
                HasCreatedAudit: true,
                HasUpdatedAudit: true,
                HasDeletedAudit: true,
                HasVersion: true,
                "organization.unit"),
            "single",
            []);
    }
}
