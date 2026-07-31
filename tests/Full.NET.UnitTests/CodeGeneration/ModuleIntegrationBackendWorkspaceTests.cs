using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class ModuleIntegrationBackendWorkspaceTests
{
    [TestMethod]
    public async Task Plan_maps_only_backend_artifacts_without_writing()
    {
        using var workspace = TemporaryModuleWorkspace.Create();
        var schema = FullNetCrudSchemaTests.CreateProductSchema();

        var artifacts =
            ModuleIntegrationBackendWorkspace.CreateArtifacts(schema);
        var plan = await ModuleIntegrationBackendWorkspace.PlanAsync(
            workspace.RootPath,
            schema);

        Assert.HasCount(5, artifacts);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "Generated/Product/ProductContracts.g.cs",
                "Generated/Product/ProductEndpoint.g.cs",
                "Generated/Product/ProductFeature.g.cs",
                "Generated/Product/ProductRecord.g.cs",
                "Generated/Product/ProductSql.g.cs",
            },
            artifacts.Select(artifact => artifact.RelativePath).ToArray());
        Assert.IsTrue(artifacts.All(artifact =>
            artifact.Kind == GeneratedArtifactKind.Backend));
        Assert.IsTrue(plan.CanApply);
        Assert.HasCount(6, plan.Actions);
        Assert.IsTrue(plan.Actions.All(action =>
            action.Kind == GenerationWriteActionKind.Create));
        var registry = plan.Actions.Single(action =>
            action.RelativePath
                == ModuleIntegrationBackendWorkspace.RegistryRelativePath);
        StringAssert.Contains(
            registry.Content!,
            "services.AddGeneratedProductFeature();");
        StringAssert.Contains(
            registry.Content!,
            "endpoints.MapGeneratedProductFeature();");
        Assert.AreEqual(
            0,
            Directory.GetFileSystemEntries(
                workspace.RootPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Plan_preserves_other_owned_entities_and_rejects_their_edits()
    {
        using var workspace = TemporaryModuleWorkspace.Create();
        var product = FullNetCrudSchemaTests.CreateProductSchema();
        var productPlan =
            await ModuleIntegrationBackendWorkspace.PlanAsync(
                workspace.RootPath,
                product);
        await GenerationWorkspaceStore.ApplyAsync(
            workspace.RootPath,
            productPlan);
        var order = CreateOrderSchema(product);

        var orderPlan =
            await ModuleIntegrationBackendWorkspace.PlanAsync(
                workspace.RootPath,
                order);

        Assert.IsTrue(orderPlan.CanApply);
        Assert.HasCount(11, orderPlan.Actions);
        Assert.AreEqual(
            5,
            orderPlan.Actions.Count(action =>
                action.RelativePath.StartsWith(
                    "Generated/Product/",
                    StringComparison.Ordinal)
                && action.Kind == GenerationWriteActionKind.Unchanged));
        Assert.AreEqual(
            5,
            orderPlan.Actions.Count(action =>
                action.RelativePath.StartsWith(
                    "Generated/Order/",
                    StringComparison.Ordinal)
                && action.Kind == GenerationWriteActionKind.Create));
        var registry = orderPlan.Actions.Single(action =>
            action.RelativePath
                == ModuleIntegrationBackendWorkspace.RegistryRelativePath);
        Assert.AreEqual(
            GenerationWriteActionKind.Update,
            registry.Kind);
        Assert.IsTrue(
            registry.Content!.IndexOf(
                "services.AddGeneratedOrderFeature();",
                StringComparison.Ordinal)
            < registry.Content.IndexOf(
                "services.AddGeneratedProductFeature();",
                StringComparison.Ordinal));
        await GenerationWorkspaceStore.ApplyAsync(
            workspace.RootPath,
            orderPlan);
        Assert.HasCount(
            11,
            GenerationManifest.Parse(workspace.Read(
                GenerationWorkspaceStore.ManifestRelativePath)).Artifacts);

        var productContracts =
            "Generated/Product/ProductContracts.g.cs";
        workspace.Write(productContracts, "// 人工修改\n");
        var before = workspace.Capture();

        var exception = await Assert.ThrowsExactlyAsync<
            GenerationWorkspaceConflictException>(() =>
            ModuleIntegrationBackendWorkspace.PlanAsync(
                workspace.RootPath,
                order));

        Assert.AreEqual(productContracts, exception.RelativePath);
        CollectionAssert.AreEquivalent(
            before.ToArray(),
            workspace.Capture().ToArray());
    }

    [TestMethod]
    public async Task Plan_rejects_modified_module_registry()
    {
        using var workspace = TemporaryModuleWorkspace.Create();
        var schema = FullNetCrudSchemaTests.CreateProductSchema();
        var plan = await ModuleIntegrationBackendWorkspace.PlanAsync(
            workspace.RootPath,
            schema);
        await GenerationWorkspaceStore.ApplyAsync(
            workspace.RootPath,
            plan);
        workspace.Write(
            ModuleIntegrationBackendWorkspace.RegistryRelativePath,
            "// 人工修改必须保留\n");
        var before = workspace.Capture();

        var exception = await Assert.ThrowsExactlyAsync<
            GenerationWorkspaceConflictException>(() =>
            ModuleIntegrationBackendWorkspace.PlanAsync(
                workspace.RootPath,
                schema));

        Assert.AreEqual(
            ModuleIntegrationBackendWorkspace.RegistryRelativePath,
            exception.RelativePath);
        CollectionAssert.AreEquivalent(
            before.ToArray(),
            workspace.Capture().ToArray());
    }

    [TestMethod]
    public async Task Plan_rejects_previous_non_backend_ownership()
    {
        using var workspace = TemporaryModuleWorkspace.Create();
        const string relativePath = "reports/catalog.json";
        const string content = "{}\n";
        workspace.Write(relativePath, content);
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
                [
                    new(
                        relativePath,
                        GenerationContentHash.Compute(content)),
                ]).ToJson());
        var before = workspace.Capture();

        var exception = await Assert.ThrowsExactlyAsync<
            GenerationWorkspaceConflictException>(() =>
            ModuleIntegrationBackendWorkspace.PlanAsync(
                workspace.RootPath,
                FullNetCrudSchemaTests.CreateProductSchema()));

        Assert.AreEqual(relativePath, exception.RelativePath);
        CollectionAssert.AreEquivalent(
            before.ToArray(),
            workspace.Capture().ToArray());
    }

    private static FullNetCrudSchema CreateOrderSchema(
        FullNetCrudSchema product) =>
        FullNetCrudSchema.CreateProject(
            ownerKey: product.OwnerKey,
            moduleKey: product.ModuleKey,
            entityKey: "order",
            databaseTableName: "acme_catalog_order",
            rootNamespace: product.RootNamespace,
            clrTypeName: "Order",
            apiResourceName: "orders",
            permissionResourceName: "orders",
            dataScope: product.DataScope,
            hasVersion: product.HasVersion,
            columns: product.Columns);

    private sealed class TemporaryModuleWorkspace : IDisposable
    {
        private static readonly UTF8Encoding StrictUtf8 = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        private TemporaryModuleWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TemporaryModuleWorkspace Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                $"fullnet-module-backend-workspace-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new TemporaryModuleWorkspace(rootPath);
        }

        public string Read(string relativePath) =>
            File.ReadAllText(PathOf(relativePath), StrictUtf8);

        public void Write(string relativePath, string content)
        {
            var path = PathOf(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, StrictUtf8);
        }

        public Dictionary<string, string> Capture() =>
            Directory
                .GetFiles(
                    RootPath,
                    "*",
                    SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(RootPath, path),
                    path => Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(
                            File.ReadAllBytes(path))),
                    StringComparer.Ordinal);

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private string PathOf(string relativePath) =>
            Path.Combine(
                RootPath,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
    }
}
