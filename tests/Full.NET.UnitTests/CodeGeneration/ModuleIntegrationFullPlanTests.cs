using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>整链候选必须先检查全部文件；检查点须恢复原来不属于生成清单的手写入口。</summary>
[TestClass]
public sealed class ModuleIntegrationFullPlanTests
{
    [TestMethod]
    public async Task Invalid_late_target_writes_nothing_and_complete_plan_restores_handwritten_files()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-full-plan-").FullName;
        try
        {
            const string entry = """
                namespace Acme.Modules.Catalog;
                public sealed class CatalogModule : IFullNetModule
                {
                    public void AddServices(IServiceCollection services, IConfiguration configuration)
                    {
                        services.AddOptions();
                    }
                    public void MapEndpoints(IEndpointRouteBuilder endpoints)
                    {
                    }
                }
                """;
            const string catalog = """
                using Full.NET.Modularity.Modules;
                namespace Acme.Composition;
                public static class ModuleCatalog
                {
                    private static IReadOnlyList<IFullNetModule> CreateModules() =>
                    [
                    ];
                }
                """;
            var originals = new Dictionary<string, string>
            {
                ["src/Catalog/Catalog.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup /></Project>",
                ["src/Catalog/CatalogModule.cs"] = entry,
                ["src/Composition/Composition.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <ProjectReference Include=\"../Core/Core.csproj\" />\n  </ItemGroup>\n</Project>",
                ["src/Composition/ModuleCatalog.cs"] = catalog,
            };
            foreach (var (path, text) in originals)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
                File.WriteAllText(Path.Combine(root, path), text);
            }
            var target = ModuleIntegrationTarget.Create("Catalog", "src/Catalog/Catalog.csproj", "src/Catalog/CatalogModule.cs",
                "src/Composition/Composition.csproj", "src/Composition/ModuleCatalog.cs", "ui/routes.ts", null);
            var schema = FullNetCrudSchemaTests.CreateProductSchema();
            var draft = await CrudGenerationWorkspace.PlanAsync(root, schema);
            var plan = await ModuleIntegrationFullPlanner.PlanAsync(root, schema, target, draft);
            Assert.IsTrue(plan.CanApply);
            foreach (var (path, text) in originals) Assert.AreEqual(text, File.ReadAllText(Path.Combine(root, path)));
            var id = Guid.NewGuid();
            await GenerationRollbackCheckpointStore.CreateAsync(root, id, plan);
            await GenerationWorkspaceStore.ApplyAsync(root, plan);
            var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(root, id);
            var restored = await GenerationRollbackWorkspace.RestoreAsync(root, checkpoint);
            Assert.IsTrue(restored.CanApply);
            foreach (var (path, text) in originals) Assert.AreEqual(text, File.ReadAllText(Path.Combine(root, path)));
            Assert.AreEqual(0, (await GenerationWorkspaceStore.ReadManifestOrEmptyAsync(root)).Artifacts.Count);

            File.WriteAllText(Path.Combine(root, "src/Composition/ModuleCatalog.cs"), "unsupported shape");
            draft = await CrudGenerationWorkspace.PlanAsync(root, schema);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => ModuleIntegrationFullPlanner.PlanAsync(root, schema, target, draft));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "src/Catalog/Generated")) &&
                Directory.GetFiles(Path.Combine(root, "src/Catalog/Generated"), "*.cs", SearchOption.AllDirectories).Length > 0);
            Assert.AreEqual(entry, File.ReadAllText(Path.Combine(root, "src/Catalog/CatalogModule.cs")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
