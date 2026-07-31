using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CrudGenerationWorkspaceTests
{
    [TestMethod]
    public async Task Plan_empty_workspace_returns_creates_without_writing()
    {
        using var workspace = TemporaryWorkspace.Create();

        var plan = await CrudGenerationWorkspace.PlanAsync(
            workspace.RootPath,
            FullNetCrudSchemaTests.CreateProductSchema());

        Assert.IsTrue(plan.CanApply);
        Assert.AreEqual(13, plan.Actions.Count);
        Assert.IsTrue(plan.Actions.All(
            action => action.Kind == GenerationWriteActionKind.Create));
        Assert.IsFalse(Directory.Exists(
            Path.Combine(workspace.RootPath, ".fullnet")));
        Assert.AreEqual(0, Directory.GetFiles(
            workspace.RootPath,
            "*",
            SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Plan_multiple_schemas_builds_one_preview_without_writing()
    {
        using var workspace = TemporaryWorkspace.Create();
        var product = FullNetCrudSchemaTests.CreateProductSchema();
        var order = CreateOrderSchema(product);

        var plan = await CrudGenerationWorkspace.PlanAsync(
            workspace.RootPath,
            [product, order]);

        Assert.IsTrue(plan.CanApply);
        Assert.AreEqual(26, plan.Actions.Count);
        Assert.IsTrue(plan.Actions.All(
            action => action.Kind == GenerationWriteActionKind.Create));
        Assert.IsTrue(plan.Actions.Any(
            action => action.RelativePath == "backend/ProductContracts.g.cs"));
        Assert.IsTrue(plan.Actions.Any(
            action => action.RelativePath == "backend/OrderContracts.g.cs"));
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                workspace.RootPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Plan_multiple_schemas_rejects_duplicate_artifact_paths()
    {
        using var workspace = TemporaryWorkspace.Create();
        var product = FullNetCrudSchemaTests.CreateProductSchema();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            CrudGenerationWorkspace.PlanAsync(
                workspace.RootPath,
                [product, product]));
    }

    [TestMethod]
    public async Task Apply_multiple_schemas_writes_one_manifest_and_is_idempotent()
    {
        using var workspace = TemporaryWorkspace.Create();
        var product = FullNetCrudSchemaTests.CreateProductSchema();
        var order = CreateOrderSchema(product);

        var first = await CrudGenerationWorkspace.ApplyAsync(
            workspace.RootPath,
            [product, order]);
        var second = await CrudGenerationWorkspace.ApplyAsync(
            workspace.RootPath,
            [product, order]);

        Assert.IsTrue(first.CanApply);
        Assert.HasCount(26, first.Actions);
        Assert.IsTrue(first.Actions.All(
            action => action.Kind == GenerationWriteActionKind.Create));
        Assert.IsTrue(second.CanApply);
        Assert.HasCount(26, second.Actions);
        Assert.IsTrue(second.Actions.All(
            action => action.Kind == GenerationWriteActionKind.Unchanged));
        var manifest = GenerationManifest.Parse(workspace.Read(
            GenerationWorkspaceStore.ManifestRelativePath));
        Assert.HasCount(26, manifest.Artifacts);
        Assert.AreEqual(
            27,
            Directory.GetFiles(
                workspace.RootPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Apply_multiple_schemas_with_conflict_writes_nothing_else()
    {
        using var workspace = TemporaryWorkspace.Create();
        const string handwrittenPath = "backend/ProductContracts.g.cs";
        workspace.Write(handwrittenPath, "handwritten");
        var product = FullNetCrudSchemaTests.CreateProductSchema();
        var order = CreateOrderSchema(product);

        var plan = await CrudGenerationWorkspace.ApplyAsync(
            workspace.RootPath,
            [product, order]);

        Assert.IsFalse(plan.CanApply);
        Assert.AreEqual("handwritten", workspace.Read(handwrittenPath));
        Assert.IsFalse(File.Exists(Path.Combine(
            workspace.RootPath,
            "backend",
            "OrderContracts.g.cs")));
        Assert.IsFalse(File.Exists(Path.Combine(
            workspace.RootPath,
            GenerationWorkspaceStore.ManifestRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar))));
        Assert.AreEqual(
            1,
            Directory.GetFiles(
                workspace.RootPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Apply_writes_all_artifacts_and_repeats_as_unchanged()
    {
        using var workspace = TemporaryWorkspace.Create();
        var schema = FullNetCrudSchemaTests.CreateProductSchema();
        var artifacts = CrudArtifactGenerator.Generate(schema);

        var first = await CrudGenerationWorkspace.ApplyAsync(
            workspace.RootPath,
            schema);
        var second = await CrudGenerationWorkspace.ApplyAsync(
            workspace.RootPath,
            schema);

        Assert.IsTrue(first.CanApply);
        Assert.IsTrue(first.Actions.All(
            action => action.Kind == GenerationWriteActionKind.Create));
        Assert.IsTrue(second.CanApply);
        Assert.IsTrue(second.Actions.All(
            action => action.Kind == GenerationWriteActionKind.Unchanged));
        foreach (var artifact in artifacts)
        {
            Assert.AreEqual(
                artifact.Content,
                workspace.Read(artifact.RelativePath));
        }

        Assert.AreEqual(
            second.NextManifest!.ToJson(),
            workspace.Read(
                GenerationWorkspaceStore.ManifestRelativePath));
        Assert.AreEqual(0, workspace.FindFiles("*.tmp").Length);
        Assert.AreEqual(0, workspace.FindFiles("*.recovery").Length);
    }

    [TestMethod]
    public async Task Apply_handwritten_conflict_returns_plan_without_writes()
    {
        using var workspace = TemporaryWorkspace.Create();
        const string handwrittenPath = "backend/ProductContracts.g.cs";
        workspace.Write(handwrittenPath, "handwritten");

        var plan = await CrudGenerationWorkspace.ApplyAsync(
            workspace.RootPath,
            FullNetCrudSchemaTests.CreateProductSchema());

        Assert.IsFalse(plan.CanApply);
        Assert.AreEqual(
            GenerationWriteActionKind.Conflict,
            plan.Actions.Single(
                action => action.RelativePath == handwrittenPath).Kind);
        Assert.AreEqual("handwritten", workspace.Read(handwrittenPath));
        Assert.IsFalse(Directory.Exists(
            Path.Combine(workspace.RootPath, ".fullnet")));
        Assert.AreEqual(
            1,
            Directory.GetFiles(
                workspace.RootPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    private static FullNetCrudSchema CreateOrderSchema(
        FullNetCrudSchema source) =>
        FullNetCrudSchema.CreateProject(
            ownerKey: "acme",
            moduleKey: "sales",
            entityKey: "order",
            databaseTableName: "acme_sales_order",
            rootNamespace: "Acme.Modules.Sales",
            clrTypeName: "Order",
            apiResourceName: "orders",
            permissionResourceName: "orders",
            dataScope: source.DataScope,
            hasVersion: source.HasVersion,
            columns: source.Columns);

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TemporaryWorkspace Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                $"fullnet-codegen-workflow-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new TemporaryWorkspace(rootPath);
        }

        public string Read(string relativePath)
        {
            return File.ReadAllText(
                PathOf(relativePath),
                new UTF8Encoding(false, true));
        }

        public void Write(string relativePath, string content)
        {
            var path = PathOf(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                content,
                new UTF8Encoding(false, true));
        }

        public string[] FindFiles(string searchPattern)
        {
            return Directory.GetFiles(
                RootPath,
                searchPattern,
                SearchOption.AllDirectories);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private string PathOf(string relativePath)
        {
            return Path.Combine(
                RootPath,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        }
    }
}
