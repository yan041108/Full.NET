using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.Data.CodeGeneration.Schema;
using Full.NET.Tests.CodeGeneration;
using Full.NET.CodeGeneration.Cli;
using System.Text.Json;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>整链候选必须先完整规划与编译，首次纳管的入口必须能恢复原文。</summary>
[TestClass]
public sealed class ModuleIntegrationFullPlanTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Hard_delete_without_audit_or_relationship_compiles_for_both_schema_models(bool legacy)
    {
        using var fixture = new Fixture();
        FullNetColumn[] columns = [new("Id", "Id", "id", FullNetScalarType.Uuid), new("Name", "Name", "name", FullNetScalarType.String, MaxLength: 64),
            new("IsActive", "IsActive", "isActive", FullNetScalarType.Boolean), new("Version", "Version", "version", FullNetScalarType.Int64)];
        var schema = legacy ? FullNetCrudSchema.CreateProject("acme", "catalog", "http_product", "acme_catalog_http_product", "Acme.Modules.Catalog",
            "HttpProduct", "http-products", "http_products", FullNetCrudDataScope.HostOnly, true, columns)
            : FullNetCrudSchema.CreateProject("acme", "catalog", "http_product", "acme_catalog_http_product", "Acme.Modules.Catalog",
            "HttpProduct", "http-products", "http_products", FullNetCrudDataScope.HostOnly,
            new FullNetCrudEntityCapabilities(FullNetCrudDeleteMode.HardDelete, false, false, false, true, FullNetCrudOwnershipMode.None),
            columns);
        var plan = await ModuleIntegrationFullPlanner.PlanAsync(fixture.Root, schema, fixture.Target, await CrudGenerationWorkspace.PlanAsync(fixture.Root, schema));
        var compilation = await ModuleIntegrationFullCompilation.ValidateAsync(fixture.Root, schema, fixture.Target, plan);
        Assert.IsTrue(compilation.Succeeded, string.Join("\n", compilation.Diagnostics));
    }
    [TestMethod]
    public async Task Tutorial_schema_freezes_its_detail_and_matches_the_checked_in_backend()
    {
        var root = CodeGenerationRemediationSchemas.RepositoryRoot();
        var schema = await CrudSchemaDocument.LoadAsync(Path.Combine(root, "samples/enterprise-request/schema.json"), CancellationToken.None);
        Assert.IsNotNull(schema.Relationships[0].DependentSchema);
        foreach (var artifact in ModuleIntegrationBackendWorkspace.CreateArtifacts(schema))
            Assert.AreEqual(artifact.Content, await File.ReadAllTextAsync(Path.Combine(root,
                "samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest", artifact.RelativePath)), artifact.RelativePath);
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Relation_lookup_endpoints_materializers_and_minimal_json_contract_compile(bool organizationOwned)
    {
        using var fixture = new Fixture();
        var source = organizationOwned ? CodeGenerationRemediationSchemas.OrganizationLink() : CodeGenerationRemediationSchemas.ProductTag();
        var schema = Full.NET.Data.CodeGeneration.Schema.FullNetCrudSchema.CreateProject(source.OwnerKey, source.ModuleKey, source.EntityKey,
            source.DatabaseTableName, "Acme.Modules.Catalog", source.ClrTypeName, source.ApiResourceName, source.PermissionResourceName,
            source.DataScope, source.EntityCapabilities, source.Scene, source.Relationships, source.Columns);
        var plan = await ModuleIntegrationFullPlanner.PlanAsync(fixture.Root, schema, fixture.Target, await CrudGenerationWorkspace.PlanAsync(fixture.Root, schema));
        if (organizationOwned)
        {
            var project = plan.Actions.Single(action => action.RelativePath == fixture.Target.ModuleProjectPath);
            Assert.Contains("Full.NET.Modules.Organization.Contracts.csproj", project.Content!);
            Assert.DoesNotContain("Full.NET.Modules.Organization.Contracts.csproj", File.ReadAllText(Path.Combine(fixture.Root, fixture.Target.ModuleProjectPath)));
        }
        var result = await ModuleIntegrationFullCompilation.ValidateAsync(fixture.Root, schema, fixture.Target, plan);
        Assert.IsTrue(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "module/Generated")));
    }

    [TestMethod]
    public async Task Aggregate_endpoint_detail_services_and_json_context_compile_as_one_candidate_without_writes()
    {
        using var fixture = new Fixture();
        var source = CodeGenerationRemediationSchemas.Product();
        var schema = Full.NET.Data.CodeGeneration.Schema.FullNetCrudSchema.CreateProject(source.OwnerKey, source.ModuleKey, source.EntityKey,
            source.DatabaseTableName, "Acme.Modules.Catalog", source.ClrTypeName, source.ApiResourceName, source.PermissionResourceName,
            source.DataScope, source.EntityCapabilities, source.Scene, source.Relationships, source.Columns);
        var generated = await CrudGenerationWorkspace.PlanAsync(fixture.Root, schema);
        var plan = await ModuleIntegrationFullPlanner.PlanAsync(fixture.Root, schema, fixture.Target, generated);
        var result = await ModuleIntegrationFullCompilation.ValidateAsync(fixture.Root, schema, fixture.Target, plan);
        Assert.IsTrue(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "module/Generated")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, GenerationWorkspaceStore.ManifestRelativePath)));
    }

    [TestMethod]
    public async Task Moving_a_shared_composition_target_requires_rollback_instead_of_leaving_the_old_hook_active()
    {
        using var fixture = new Fixture();
        var original = await fixture.PlanAsync();
        await GenerationWorkspaceStore.ApplyAsync(fixture.Root, original);
        const string nextCatalog = "composition/AlternateCatalog.cs";
        File.Copy(Path.Combine(fixture.Root, Fixture.CatalogPath), Path.Combine(fixture.Root, nextCatalog));
        var target = ModuleIntegrationTarget.Create("Catalog", "module/Catalog.csproj", Fixture.EntryPath,
            "composition/Composition.csproj", nextCatalog, "ui/router.ts", null);
        var before = fixture.Files();
        var schema = FullNetCrudSchemaTests.CreateProductSchema();
        var artifacts = await CrudGenerationWorkspace.PlanAsync(fixture.Root, schema);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => ModuleIntegrationFullPlanner.PlanAsync(fixture.Root, schema,
            target, artifacts));
        CollectionAssert.AreEquivalent(before.ToArray(), fixture.Files().ToArray());
    }

    [TestMethod]
    public async Task Real_host_CLI_compiles_and_commits_one_complete_plan_then_rolls_back_all_original_files()
    {
        using var fixture = new Fixture();
        var repository = CodeGenerationRemediationSchemas.RepositoryRoot();
        const string contributor = "module/CatalogAuthorizationContributor.cs";
        File.WriteAllText(Path.Combine(fixture.Root, "schema.json"), CodeGenerationCliTests.ValidSchemaJson);
        File.WriteAllText(Path.Combine(fixture.Root, "target.json"), JsonSerializer.Serialize(new
        {
            moduleName = "Catalog", moduleProjectPath = "module/Catalog.csproj", moduleEntryPointPath = Fixture.EntryPath,
            compositionProjectPath = "composition/Composition.csproj", compositionCatalogPath = Fixture.CatalogPath,
            vueRouterPath = "ui/router.ts", authorizationContributorPath = contributor,
        }));
        File.WriteAllText(Path.Combine(fixture.Root, contributor), File.ReadAllText(Path.Combine(repository,
            "tests/templates/support/fixtures/application-catalog-authorization-contributor.cs.fixture"))
            .Replace("Demo.Modules.Catalog", "Acme.Modules.Catalog", StringComparison.Ordinal));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "module/Generated"));
        File.WriteAllText(Path.Combine(fixture.Root, "module/Generated/ManualMarker.cs"),
            "namespace Acme.Modules.Catalog.Generated; internal static class ManualMarker { }");
        var entry = Path.Combine(fixture.Root, Fixture.EntryPath);
        File.WriteAllText(entry, File.ReadAllText(entry).Replace("services.AddOptions();", "services.AddOptions();\n"
            + "        services.AddSingleton<Full.NET.Modules.Identity.Contracts.IAuthorizationCatalogContributor, CatalogAuthorizationContributor>();", StringComparison.Ordinal));
        var originals = fixture.Files();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(["apply-host-integration", "--schema", Path.Combine(fixture.Root, "schema.json"),
            "--repository", fixture.Root, "--target", Path.Combine(fixture.Root, "target.json")], output, error);
        Assert.AreEqual(0, result, error.ToString());
        StringAssert.Contains(output.ToString(), "Applied Operation ");
        StringAssert.Contains(output.ToString(), "Applied HostIntegration module/Catalog.csproj");
        StringAssert.Contains(File.ReadAllText(entry), "services.AddFullNetGeneratedModuleFeatures();");
        Guid id;
        await using (var lease = await GenerationWorkspaceLease.TryAcquireAsync(fixture.Root) ?? throw new InvalidOperationException())
        {
            id = (await GenerationWorkspaceJournal.ReadHeadAsync(lease)).TipOperationId!.Value;
            Assert.AreEqual("completed", (await GenerationWorkspaceJournal.ReadOperationAsync(lease, id))!.Phase);
        }
        var manifest = await GenerationWorkspaceStore.ReadManifestOrEmptyAsync(fixture.Root);
        Assert.IsTrue(manifest.Artifacts.Any(artifact => artifact.RelativePath == contributor));
        Assert.IsTrue(manifest.Artifacts.Any(artifact => artifact.RelativePath == "module/Generated/Product/ProductFeature.g.cs"
            && artifact.Owner == "acme/catalog/product"));
        Assert.AreEqual(0, await CodeGenerationCli.RunAsync(["rollback", "--workspace", fixture.Root, "--operation", id.ToString("D")], output, error), error.ToString());
        foreach (var original in originals) Assert.AreEqual(original.Value, File.ReadAllText(Path.Combine(fixture.Root, original.Key)), original.Key);
        Assert.IsEmpty((await GenerationWorkspaceStore.ReadManifestOrEmptyAsync(fixture.Root)).Artifacts);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "module/Generated/Product/ProductFeature.g.cs")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Full_plan_contains_Vue_page_both_client_artifacts_and_route_and_second_plan_is_unchanged(bool applicationClient)
    {
        using var fixture = new Fixture();
        // 先完成后端整链接入，再增加 Vue；新增目标不能被误判为移动既有接线。
        await GenerationWorkspaceStore.ApplyAsync(fixture.Root, await fixture.PlanAsync());
        Directory.CreateDirectory(Path.Combine(fixture.Root, "ui"));
        const string router = """
            export function createAppRouter() {
              return createRouter({
                routes: [
                  { path: '/403', component: loadStatusView }
                ]
              });
            }
            """;
        File.WriteAllText(Path.Combine(fixture.Root, "ui/router.ts"), router);
        var target = ModuleIntegrationTarget.Create("Catalog", "module/Catalog.csproj", Fixture.EntryPath,
            "composition/Composition.csproj", Fixture.CatalogPath, "ui/router.ts", null,
            ModuleClientRouteTarget.Create("/catalog/products", "catalog-products", "ui/views/CatalogProductsView.vue",
                clientModuleSpecifier: applicationClient ? "./business/index.generated.js" : null));
        var schema = FullNetCrudSchemaTests.CreateProductSchema();
        var generated = await CrudGenerationWorkspace.PlanAsync(fixture.Root, schema);
        var plan = await ModuleIntegrationFullPlanner.PlanAsync(fixture.Root, schema, target, generated);
        Assert.IsTrue(plan.CanApply);
        Assert.AreEqual(3, plan.Actions.Count(action => action.RelativePath.StartsWith("ui/views/", StringComparison.Ordinal)));
        var adapter = plan.Actions.Single(action => action.RelativePath == "ui/views/products.generated.ts").Content!;
        StringAssert.Contains(adapter, applicationClient ? "from './business/index.generated.js';" : "from '@fullnet/client-contracts';");
        Assert.IsTrue(plan.Actions.Any(action => action.RelativePath == "ui/router.ts" && action.Content!.Contains("catalog-products", StringComparison.Ordinal)));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "ui/views")));
        await GenerationWorkspaceStore.ApplyAsync(fixture.Root, plan);
        var second = await ModuleIntegrationFullPlanner.PlanAsync(fixture.Root, schema, target,
            await CrudGenerationWorkspace.PlanAsync(fixture.Root, schema));
        Assert.IsTrue(second.CanApply);
        Assert.IsTrue(second.Actions.All(action => action.Kind == GenerationWriteActionKind.Unchanged));
    }

    [TestMethod]
    public async Task Previously_owned_handwritten_entry_cannot_be_reowned_after_human_drift()
    {
        using var fixture = new Fixture();
        await GenerationWorkspaceStore.ApplyAsync(fixture.Root, await fixture.PlanAsync());
        var entryPath = Path.Combine(fixture.Root, Fixture.EntryPath);
        File.AppendAllText(entryPath, "\n// 人工增加的行为必须先重新审查，不可自动认领。\n");
        var before = File.ReadAllText(entryPath);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(fixture.PlanAsync);
        Assert.AreEqual(before, File.ReadAllText(entryPath));
    }

    [TestMethod]
    [DataRow(".fullnet/Module.csproj")]
    [DataRow(".git/Module.csproj")]
    public void Integration_targets_cannot_take_over_internal_metadata(string project)
    {
        Assert.ThrowsExactly<ArgumentException>(() => ModuleIntegrationTarget.Create("Catalog", project,
            "module/CatalogModule.cs", "composition/Composition.csproj", "composition/Catalog.cs", "ui/router.ts", null));
    }

    [TestMethod]
    public async Task Full_plan_writes_once_and_rollback_restores_previously_unmanaged_entry_and_composition()
    {
        using var fixture = new Fixture();
        var originals = fixture.Files();
        var plan = await fixture.PlanAsync();
        Assert.IsTrue(plan.CanApply);
        CollectionAssert.AreEquivalent(originals.Keys.ToArray(), fixture.Files().Keys.ToArray());
        foreach (var file in originals) Assert.AreEqual(file.Value, File.ReadAllText(Path.Combine(fixture.Root, file.Key)));
        Assert.IsTrue(plan.Actions.Any(action => action.RelativePath == Fixture.EntryPath && action.Kind == GenerationWriteActionKind.Update));
        var runId = Guid.NewGuid();
        await GenerationRollbackCheckpointStore.CreateAsync(fixture.Root, runId, plan);
        var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(fixture.Root, runId);
        Assert.AreEqual(originals[Fixture.EntryPath], checkpoint.PreviousContents[Fixture.EntryPath]);
        await GenerationWorkspaceStore.ApplyAsync(fixture.Root, plan);
        var rollback = await GenerationRollbackWorkspace.RestoreAsync(fixture.Root, checkpoint);
        Assert.IsTrue(rollback.CanApply);
        foreach (var file in originals) Assert.AreEqual(file.Value, File.ReadAllText(Path.Combine(fixture.Root, file.Key)));
        Assert.IsEmpty(rollback.NextManifest!.Artifacts);
        Assert.IsFalse(rollback.NextManifest.Artifacts.Any(entry => entry.RelativePath == Fixture.EntryPath));
    }

    [TestMethod]
    public async Task Invalid_late_composition_target_leaves_all_business_files_unchanged()
    {
        using var fixture = new Fixture();
        File.Delete(Path.Combine(fixture.Root, Fixture.CatalogPath));
        var originals = fixture.Files();
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(fixture.PlanAsync);
        CollectionAssert.AreEquivalent(originals.Keys.ToArray(), fixture.Files().Keys.ToArray());
        foreach (var file in originals) Assert.AreEqual(file.Value, File.ReadAllText(Path.Combine(fixture.Root, file.Key)));
    }

    [TestMethod]
    public async Task Full_candidate_compiles_before_any_business_commit_and_rejects_invalid_entry()
    {
        using var fixture = new Fixture();
        var plan = await fixture.PlanAsync();
        var compiled = await ModuleIntegrationFullCompilation.ValidateAsync(fixture.Root,
            FullNetCrudSchemaTests.CreateProductSchema(), fixture.Target, plan);
        Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, GenerationWorkspaceStore.ManifestRelativePath)));
        var entry = File.ReadAllText(Path.Combine(fixture.Root, Fixture.EntryPath));
        File.WriteAllText(Path.Combine(fixture.Root, Fixture.EntryPath), entry.Replace("services.AddOptions();", "services.MissingMethod();", StringComparison.Ordinal));
        var invalid = await fixture.PlanAsync();
        var rejected = await ModuleIntegrationFullCompilation.ValidateAsync(fixture.Root,
            FullNetCrudSchemaTests.CreateProductSchema(), fixture.Target, invalid);
        Assert.IsFalse(rejected.Succeeded);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, GenerationWorkspaceStore.ManifestRelativePath)));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "module/Generated")));
    }

    private sealed class Fixture : IDisposable
    {
        internal const string EntryPath = "module/CatalogModule.cs";
        internal const string CatalogPath = "composition/Catalog.cs";
        internal string Root { get; } = Directory.CreateTempSubdirectory("fullnet-codegen-full-plan-").FullName;
        internal ModuleIntegrationTarget Target { get; } = ModuleIntegrationTarget.Create("Catalog", "module/Catalog.csproj",
            EntryPath, "composition/Composition.csproj", CatalogPath, "ui/router.ts", null);

        internal Fixture()
        {
            var repository = CodeGenerationRemediationSchemas.RepositoryRoot().Replace('\\', '/');
            Write("Directory.Build.props", $"<Project><Import Project=\"{repository}/Directory.Build.props\" /></Project>");
            Write("Directory.Packages.props", $"<Project><Import Project=\"{repository}/Directory.Packages.props\" /></Project>");
            Write("module/Catalog.csproj", $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
                    <ProjectReference Include="{{repository}}/src/BuildingBlocks/Full.NET.Data.Abstractions/Full.NET.Data.Abstractions.csproj" />
                    <ProjectReference Include="{{repository}}/src/BuildingBlocks/Full.NET.Hosting/Full.NET.Hosting.csproj" />
                    <ProjectReference Include="{{repository}}/src/BuildingBlocks/Full.NET.Modularity/Full.NET.Modularity.csproj" />
                    <ProjectReference Include="{{repository}}/src/Modules/Full.NET.Modules.Identity.Contracts/Full.NET.Modules.Identity.Contracts.csproj" />
                  </ItemGroup>
                </Project>
                """);
            Write("composition/Composition.csproj", $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="{{repository}}/src/BuildingBlocks/Full.NET.Modularity/Full.NET.Modularity.csproj" />
                  </ItemGroup>
                </Project>
                """);
            Write(EntryPath, """
                using Full.NET.Modularity.Modules;
                using Microsoft.AspNetCore.Routing;
                using Microsoft.Extensions.Configuration;
                using Microsoft.Extensions.DependencyInjection;
                namespace Acme.Modules.Catalog;
                public sealed class CatalogModule : IFullNetModule
                {
                    public string Name => "Catalog";
                    public IReadOnlyCollection<string> Dependencies => [];
                    public void AddServices(IServiceCollection services, IConfiguration configuration)
                    {
                        services.AddOptions();
                    }
                    public void MapEndpoints(IEndpointRouteBuilder endpoints)
                    {
                    }
                }
                """);
            Write(CatalogPath, """
                using Full.NET.Modularity.Modules;
                namespace Acme.Composition;
                public static class Catalog
                {
                    private static IReadOnlyList<IFullNetModule> CreateModules() =>
                    [
                    ];
                }
                """);
        }

        internal async Task<GenerationWritePlan> PlanAsync()
        {
            var schema = FullNetCrudSchemaTests.CreateProductSchema();
            var artifacts = await CrudGenerationWorkspace.PlanAsync(Root, schema);
            return await ModuleIntegrationFullPlanner.PlanAsync(Root, schema, Target, artifacts);
        }

        internal Dictionary<string, string> Files() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Root, path).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);

        private void Write(string path, string content)
        {
            var full = Path.Combine(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
