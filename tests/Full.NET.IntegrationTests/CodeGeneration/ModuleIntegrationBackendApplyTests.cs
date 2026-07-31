extern alias codegencli;

using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using CodeGenerationCli =
    codegencli::Full.NET.CodeGeneration.Cli.CodeGenerationCli;

namespace Full.NET.IntegrationTests.CodeGeneration;

[TestClass]
[DoNotParallelize]
public sealed class ModuleIntegrationBackendApplyTests
{
    [TestMethod]
    public async Task Apply_compiles_writes_idempotently_and_rejects_handwritten_conflict()
    {
        using var fixture = ModuleApplyFixture.Create(compilable: true);
        var temporaryBuilds = CaptureTemporaryBuildDirectories();

        var first = await RunApplyAsync(fixture);

        Assert.AreEqual(0, first.ExitCode, first.Error);
        Assert.AreEqual(string.Empty, first.Error);
        Assert.AreEqual(
            6,
            Lines(first.Output).Count(line =>
                line.StartsWith("Create Generated/", StringComparison.Ordinal)));
        StringAssert.Contains(
            first.Output,
            "Validated ModuleCompilation "
            + "src/Modules/Acme.Modules.Catalog/"
            + "Acme.Modules.Catalog.csproj");
        Assert.AreEqual(
            6,
            GenerationManifest.Parse(fixture.ReadModuleFile(
                GenerationWorkspaceStore.ManifestRelativePath))
                .Artifacts.Count);
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.ModuleDirectory,
            "bin")));
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.ModuleDirectory,
            "obj")));

        var second = await RunApplyAsync(fixture);

        Assert.AreEqual(0, second.ExitCode, second.Error);
        Assert.AreEqual(string.Empty, second.Error);
        Assert.AreEqual(
            6,
            Lines(second.Output).Count(line =>
                line.StartsWith(
                    "Unchanged Generated/",
                    StringComparison.Ordinal)));

        fixture.UseOrderSchema();
        var order = await RunApplyAsync(fixture);

        Assert.AreEqual(0, order.ExitCode, order.Error);
        Assert.AreEqual(
            5,
            Lines(order.Output).Count(line =>
                line.StartsWith(
                    "Create Generated/Order/",
                    StringComparison.Ordinal)));
        StringAssert.Contains(
            order.Output,
            "Update Generated/FullNetGeneratedModuleFeatures.g.cs");
        Assert.AreEqual(
            11,
            GenerationManifest.Parse(fixture.ReadModuleFile(
                GenerationWorkspaceStore.ManifestRelativePath))
                .Artifacts.Count);
        var registry = fixture.ReadModuleFile(
            ModuleIntegrationBackendWorkspace.RegistryRelativePath);
        StringAssert.Contains(
            registry,
            "services.AddGeneratedOrderFeature();");
        StringAssert.Contains(
            registry,
            "services.AddGeneratedProductFeature();");

        fixture.UseProductSchema();
        var conflictPath =
            "Generated/Product/ProductContracts.g.cs";
        fixture.WriteModuleFile(
            conflictPath,
            "// 人工修改必须保留\n");
        var beforeConflict = fixture.CaptureRepository();

        var conflict = await RunApplyAsync(fixture);

        Assert.AreEqual(2, conflict.ExitCode);
        Assert.AreEqual(string.Empty, conflict.Error);
        StringAssert.Contains(
            conflict.Output,
            $"Conflict {conflictPath}");
        CollectionAssert.AreEquivalent(
            beforeConflict.ToArray(),
            fixture.CaptureRepository().ToArray());
        CollectionAssert.AreEquivalent(
            temporaryBuilds,
            CaptureTemporaryBuildDirectories());
    }

    [TestMethod]
    public async Task Compile_failure_returns_sanitized_diagnostics_without_repository_writes()
    {
        using var fixture = ModuleApplyFixture.Create(compilable: false);
        var before = fixture.CaptureRepository();
        var temporaryBuilds = CaptureTemporaryBuildDirectories();

        var result = await RunApplyAsync(fixture);

        Assert.AreEqual(2, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        StringAssert.Contains(result.Error, "error CS");
        Assert.IsFalse(result.Error.Contains(
            fixture.RepositoryRoot,
            StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.Error.Contains(
            fixture.RootPath,
            StringComparison.OrdinalIgnoreCase));
        CollectionAssert.AreEquivalent(
            before.ToArray(),
            fixture.CaptureRepository().ToArray());
        CollectionAssert.AreEquivalent(
            temporaryBuilds,
            CaptureTemporaryBuildDirectories());
    }

    [TestMethod]
    public async Task Apply_entry_compiles_then_updates_idempotently_and_rejects_registry_drift()
    {
        using var fixture = ModuleApplyFixture.Create(compilable: true);
        var backend = await RunApplyAsync(fixture);
        Assert.AreEqual(0, backend.ExitCode, backend.Error);
        var originalEntry = fixture.ReadModuleFile("CatalogModule.cs");

        fixture.WriteModuleFile(
            "Broken.cs",
            "namespace Acme.Modules.Catalog;\n"
            + "public sealed class Broken { MissingType Value { get; } }\n");
        var failed = await RunEntryApplyAsync(fixture);

        Assert.AreEqual(2, failed.ExitCode);
        StringAssert.Contains(failed.Error, "error CS");
        Assert.AreEqual(
            originalEntry,
            fixture.ReadModuleFile("CatalogModule.cs"));
        fixture.DeleteModuleFile("Broken.cs");

        var first = await RunEntryApplyAsync(fixture);

        Assert.AreEqual(0, first.ExitCode, first.Error);
        Assert.AreEqual(string.Empty, first.Error);
        StringAssert.Contains(
            first.Output,
            "Update src/Modules/Acme.Modules.Catalog/CatalogModule.cs");
        StringAssert.Contains(
            first.Output,
            "Validated ModuleCompilation "
            + "src/Modules/Acme.Modules.Catalog/"
            + "Acme.Modules.Catalog.csproj");
        var updatedEntry = fixture.ReadModuleFile("CatalogModule.cs");
        StringAssert.Contains(
            updatedEntry,
            "using Acme.Modules.Catalog.Generated;");
        StringAssert.Contains(
            updatedEntry,
            "services.AddFullNetGeneratedModuleFeatures();");
        StringAssert.Contains(
            updatedEntry,
            "endpoints.MapFullNetGeneratedModuleFeatures();");
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.ModuleDirectory,
            "bin")));
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.ModuleDirectory,
            "obj")));

        var second = await RunEntryApplyAsync(fixture);

        Assert.AreEqual(0, second.ExitCode, second.Error);
        StringAssert.Contains(
            second.Output,
            "Unchanged src/Modules/Acme.Modules.Catalog/CatalogModule.cs");
        Assert.IsFalse(second.Output.Contains(
            "Validated ModuleCompilation",
            StringComparison.Ordinal));

        fixture.WriteModuleFile(
            ModuleIntegrationBackendWorkspace.RegistryRelativePath,
            "// 漂移的聚合桥\n");
        var beforeConflict = fixture.ReadModuleFile("CatalogModule.cs");
        var conflict = await RunEntryApplyAsync(fixture);

        Assert.AreEqual(2, conflict.ExitCode);
        StringAssert.Contains(conflict.Error, "聚合桥");
        Assert.AreEqual(
            beforeConflict,
            fixture.ReadModuleFile("CatalogModule.cs"));
    }

    [TestMethod]
    public async Task Apply_composition_compiles_updates_both_files_and_is_idempotent()
    {
        using var fixture = ModuleApplyFixture.Create(compilable: true);
        Assert.AreEqual(0, (await RunApplyAsync(fixture)).ExitCode);
        Assert.AreEqual(0, (await RunEntryApplyAsync(fixture)).ExitCode);
        var originalProject = fixture.ReadCompositionFile(
            "Acme.Composition.csproj");
        var originalCatalog = fixture.ReadCompositionFile(
            "ModuleCatalog.cs");

        fixture.WriteCompositionFile(
            "Broken.cs",
            "namespace Acme.Composition;\n"
            + "public sealed class Broken { MissingType Value { get; } }\n");
        var failed = await RunCompositionApplyAsync(fixture);

        Assert.AreEqual(2, failed.ExitCode);
        StringAssert.Contains(failed.Error, "error CS");
        Assert.AreEqual(
            originalProject,
            fixture.ReadCompositionFile("Acme.Composition.csproj"));
        Assert.AreEqual(
            originalCatalog,
            fixture.ReadCompositionFile("ModuleCatalog.cs"));
        fixture.DeleteCompositionFile("Broken.cs");

        var first = await RunCompositionApplyAsync(fixture);

        Assert.AreEqual(0, first.ExitCode, first.Error);
        StringAssert.Contains(
            first.Output,
            "Update src/Composition/Acme.Composition/Acme.Composition.csproj");
        StringAssert.Contains(
            first.Output,
            "Update src/Composition/Acme.Composition/ModuleCatalog.cs");
        StringAssert.Contains(
            first.Output,
            "Validated CompositionCompilation "
            + "src/Composition/Acme.Composition/Acme.Composition.csproj");
        StringAssert.Contains(
            fixture.ReadCompositionFile("Acme.Composition.csproj"),
            @"..\..\Modules\Acme.Modules.Catalog\Acme.Modules.Catalog.csproj");
        var updatedCatalog = fixture.ReadCompositionFile(
            "ModuleCatalog.cs");
        StringAssert.Contains(
            updatedCatalog,
            "using Acme.Modules.Catalog;");
        StringAssert.Contains(
            updatedCatalog,
            "new CatalogModule(),");
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.CompositionDirectory,
            "bin")));
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.CompositionDirectory,
            "obj")));

        var second = await RunCompositionApplyAsync(fixture);

        Assert.AreEqual(0, second.ExitCode, second.Error);
        StringAssert.Contains(
            second.Output,
            "Unchanged src/Composition/Acme.Composition/Acme.Composition.csproj");
        StringAssert.Contains(
            second.Output,
            "Unchanged src/Composition/Acme.Composition/ModuleCatalog.cs");
        Assert.IsFalse(second.Output.Contains(
            "Validated CompositionCompilation",
            StringComparison.Ordinal));

        fixture.WriteModuleFile(
            "CatalogModule.cs",
            ModuleApplyFixture.ModuleEntry);
        var prerequisiteFailure = await RunCompositionApplyAsync(fixture);

        Assert.AreEqual(2, prerequisiteFailure.ExitCode);
        StringAssert.Contains(
            prerequisiteFailure.Error,
            "apply-module-entry-integration");
        Assert.AreEqual(
            updatedCatalog,
            fixture.ReadCompositionFile("ModuleCatalog.cs"));
    }

    [TestMethod]
    public async Task Apply_client_routes_updates_both_trusted_maps_and_is_idempotent()
    {
        using var fixture = ModuleApplyFixture.Create(compilable: true);
        fixture.UseClientRouteTarget();
        var originalVue = fixture.ReadRepositoryFile(
            "ui/admin/src/router/index.ts");
        var originalLayui = fixture.ReadRepositoryFile(
            "ui/admin-layui/js/core/route-controllers.js");

        var prerequisiteFailure =
            await RunClientRouteApplyAsync(fixture);

        Assert.AreEqual(2, prerequisiteFailure.ExitCode);
        StringAssert.Contains(
            prerequisiteFailure.Error,
            "apply-module-integration");
        Assert.AreEqual(
            originalVue,
            fixture.ReadRepositoryFile(
                "ui/admin/src/router/index.ts"));
        Assert.AreEqual(
            originalLayui,
            fixture.ReadRepositoryFile(
                "ui/admin-layui/js/core/route-controllers.js"));

        Assert.AreEqual(0, (await RunApplyAsync(fixture)).ExitCode);
        Assert.AreEqual(0, (await RunEntryApplyAsync(fixture)).ExitCode);
        Assert.AreEqual(0, (await RunCompositionApplyAsync(fixture)).ExitCode);

        fixture.WriteRepositoryFile(
            "ui/admin/src/router/index.ts",
            "export const routes = [];\n");
        var malformed = await RunClientRouteApplyAsync(fixture);

        Assert.AreEqual(2, malformed.ExitCode);
        StringAssert.Contains(malformed.Error, "Vue 路由");
        Assert.AreEqual(
            originalLayui,
            fixture.ReadRepositoryFile(
                "ui/admin-layui/js/core/route-controllers.js"));
        fixture.WriteRepositoryFile(
            "ui/admin/src/router/index.ts",
            originalVue);

        var first = await RunClientRouteApplyAsync(fixture);

        Assert.AreEqual(0, first.ExitCode, first.Error);
        StringAssert.Contains(
            first.Output,
            "Update ui/admin/src/router/index.ts");
        StringAssert.Contains(
            first.Output,
            "Update ui/admin-layui/js/core/route-controllers.js");
        StringAssert.Contains(
            first.Output,
            "Validated ClientRouteStructure /catalog/products");
        var updatedVue = fixture.ReadRepositoryFile(
            "ui/admin/src/router/index.ts");
        StringAssert.Contains(
            updatedVue,
            "name: 'catalog-products'");
        StringAssert.Contains(
            updatedVue,
            "import('../views/CatalogProductsView.vue')");
        var updatedLayui = fixture.ReadRepositoryFile(
            "ui/admin-layui/js/core/route-controllers.js");
        StringAssert.Contains(
            updatedLayui,
            "['/catalog/products', defineController(");
        StringAssert.Contains(
            updatedLayui,
            "'createCatalogProductsController'");

        var second = await RunClientRouteApplyAsync(fixture);

        Assert.AreEqual(0, second.ExitCode, second.Error);
        StringAssert.Contains(
            second.Output,
            "Unchanged ui/admin/src/router/index.ts");
        StringAssert.Contains(
            second.Output,
            "Unchanged ui/admin-layui/js/core/route-controllers.js");
        Assert.AreEqual(
            updatedVue,
            fixture.ReadRepositoryFile(
                "ui/admin/src/router/index.ts"));
        Assert.AreEqual(
            updatedLayui,
            fixture.ReadRepositoryFile(
                "ui/admin-layui/js/core/route-controllers.js"));
    }

    private static async Task<CommandResult> RunApplyAsync(
        ModuleApplyFixture fixture)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CodeGenerationCli.RunAsync(
            [
                "apply-module-integration",
                "--schema",
                fixture.SchemaPath,
                "--repository",
                fixture.RepositoryRoot,
                "--target",
                fixture.TargetPath,
            ],
            output,
            error);
        return new CommandResult(
            exitCode,
            output.ToString(),
            error.ToString());
    }

    private static async Task<CommandResult> RunEntryApplyAsync(
        ModuleApplyFixture fixture)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CodeGenerationCli.RunAsync(
            [
                "apply-module-entry-integration",
                "--schema",
                fixture.SchemaPath,
                "--repository",
                fixture.RepositoryRoot,
                "--target",
                fixture.TargetPath,
            ],
            output,
            error);
        return new CommandResult(
            exitCode,
            output.ToString(),
            error.ToString());
    }

    private static async Task<CommandResult> RunCompositionApplyAsync(
        ModuleApplyFixture fixture)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CodeGenerationCli.RunAsync(
            [
                "apply-composition-integration",
                "--schema",
                fixture.SchemaPath,
                "--repository",
                fixture.RepositoryRoot,
                "--target",
                fixture.TargetPath,
            ],
            output,
            error);
        return new CommandResult(
            exitCode,
            output.ToString(),
            error.ToString());
    }

    private static async Task<CommandResult> RunClientRouteApplyAsync(
        ModuleApplyFixture fixture)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CodeGenerationCli.RunAsync(
            [
                "apply-client-route-integration",
                "--schema",
                fixture.SchemaPath,
                "--repository",
                fixture.RepositoryRoot,
                "--target",
                fixture.TargetPath,
            ],
            output,
            error);
        return new CommandResult(
            exitCode,
            output.ToString(),
            error.ToString());
    }

    private static string[] Lines(string value) =>
        value.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries);

    private static string[] CaptureTemporaryBuildDirectories() =>
        Directory
            .GetDirectories(
                Path.GetTempPath(),
                "fullnet-codegen-module-build-*",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private sealed record CommandResult(
        int ExitCode,
        string Output,
        string Error);

    private sealed class ModuleApplyFixture : IDisposable
    {
        private static readonly UTF8Encoding StrictUtf8 = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        private ModuleApplyFixture(
            string rootPath,
            string repositoryRoot,
            string moduleDirectory,
            string compositionDirectory,
            string schemaPath,
            string targetPath)
        {
            RootPath = rootPath;
            RepositoryRoot = repositoryRoot;
            ModuleDirectory = moduleDirectory;
            CompositionDirectory = compositionDirectory;
            SchemaPath = schemaPath;
            TargetPath = targetPath;
        }

        public string RootPath { get; }

        public string RepositoryRoot { get; }

        public string ModuleDirectory { get; }

        public string CompositionDirectory { get; }

        public string SchemaPath { get; }

        public string TargetPath { get; }

        public void UseClientRouteTarget() =>
            File.WriteAllText(
                TargetPath,
                TargetJsonWithClientRoute,
                StrictUtf8);

        public void UseOrderSchema() =>
            File.WriteAllText(
                SchemaPath,
                SchemaJson
                    .Replace(
                        "\"entityKey\": \"product\"",
                        "\"entityKey\": \"order\"",
                        StringComparison.Ordinal)
                    .Replace(
                        "\"databaseTableName\": \"acme_catalog_product\"",
                        "\"databaseTableName\": \"acme_catalog_order\"",
                        StringComparison.Ordinal)
                    .Replace(
                        "\"clrTypeName\": \"Product\"",
                        "\"clrTypeName\": \"Order\"",
                        StringComparison.Ordinal)
                    .Replace(
                        "\"apiResourceName\": \"products\"",
                        "\"apiResourceName\": \"orders\"",
                        StringComparison.Ordinal)
                    .Replace(
                        "\"permissionResourceName\": \"products\"",
                        "\"permissionResourceName\": \"orders\"",
                        StringComparison.Ordinal),
                StrictUtf8);

        public void UseProductSchema() =>
            File.WriteAllText(
                SchemaPath,
                SchemaJson,
                StrictUtf8);

        public static ModuleApplyFixture Create(bool compilable)
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                $"fullnet-module-apply-test-{Guid.NewGuid():N}");
            var repositoryRoot = Path.Combine(rootPath, "repository");
            var moduleDirectory = Path.Combine(
                repositoryRoot,
                "src",
                "Modules",
                "Acme.Modules.Catalog");
            Directory.CreateDirectory(moduleDirectory);
            var compositionDirectory = Path.Combine(
                repositoryRoot,
                "src",
                "Composition",
                "Acme.Composition");
            Directory.CreateDirectory(compositionDirectory);
            var schemaPath = Path.Combine(rootPath, "schema.json");
            var targetPath = Path.Combine(rootPath, "target.json");
            File.WriteAllText(
                schemaPath,
                SchemaJson,
                StrictUtf8);
            File.WriteAllText(
                targetPath,
                TargetJson,
                StrictUtf8);
            File.WriteAllText(
                Path.Combine(
                    moduleDirectory,
                    "Acme.Modules.Catalog.csproj"),
                compilable
                    ? CompilableProject(FindRepositoryRoot())
                    : UnderReferencedProject,
                StrictUtf8);
            File.WriteAllText(
                Path.Combine(moduleDirectory, "CatalogModule.cs"),
                ModuleEntry,
                StrictUtf8);
            File.WriteAllText(
                Path.Combine(
                    compositionDirectory,
                    "Acme.Composition.csproj"),
                CompositionProject(FindRepositoryRoot()),
                StrictUtf8);
            File.WriteAllText(
                Path.Combine(
                    compositionDirectory,
                    "ModuleCatalog.cs"),
                CompositionCatalog,
                StrictUtf8);
            WriteRepositoryFile(
                repositoryRoot,
                "ui/admin/src/router/index.ts",
                VueRouter);
            WriteRepositoryFile(
                repositoryRoot,
                "ui/admin/src/views/CatalogProductsView.vue",
                "<template><main>Catalog products</main></template>\n");
            WriteRepositoryFile(
                repositoryRoot,
                "ui/admin-layui/js/core/route-controllers.js",
                LayuiRouter);
            WriteRepositoryFile(
                repositoryRoot,
                "ui/admin-layui/js/core/catalog-products.js",
                "export function createCatalogProductsController() {\n"
                + "  return { load() {} };\n"
                + "}\n");
            return new ModuleApplyFixture(
                rootPath,
                repositoryRoot,
                moduleDirectory,
                compositionDirectory,
                schemaPath,
                targetPath);
        }

        public string ReadModuleFile(string relativePath) =>
            File.ReadAllText(
                ModulePath(relativePath),
                StrictUtf8);

        public void WriteModuleFile(
            string relativePath,
            string content)
        {
            var path = ModulePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, StrictUtf8);
        }

        public void DeleteModuleFile(string relativePath) =>
            File.Delete(ModulePath(relativePath));

        public string ReadCompositionFile(string relativePath) =>
            File.ReadAllText(
                CompositionPath(relativePath),
                StrictUtf8);

        public void WriteCompositionFile(
            string relativePath,
            string content) =>
            File.WriteAllText(
                CompositionPath(relativePath),
                content,
                StrictUtf8);

        public void DeleteCompositionFile(string relativePath) =>
            File.Delete(CompositionPath(relativePath));

        public string ReadRepositoryFile(string relativePath) =>
            File.ReadAllText(
                RepositoryPath(relativePath),
                StrictUtf8);

        public void WriteRepositoryFile(
            string relativePath,
            string content) =>
            WriteRepositoryFile(
                RepositoryRoot,
                relativePath,
                content);

        public Dictionary<string, string> CaptureRepository() =>
            Directory
                .GetFiles(
                    RepositoryRoot,
                    "*",
                    SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(RepositoryRoot, path),
                    path => Convert.ToHexString(
                        SHA256.HashData(File.ReadAllBytes(path))),
                    StringComparer.Ordinal);

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private string ModulePath(string relativePath) =>
            Path.Combine(
                ModuleDirectory,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        private string CompositionPath(string relativePath) =>
            Path.Combine(
                CompositionDirectory,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        private string RepositoryPath(string relativePath) =>
            Path.Combine(
                RepositoryRoot,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        private static void WriteRepositoryFile(
            string repositoryRoot,
            string relativePath,
            string content)
        {
            var path = Path.Combine(
                repositoryRoot,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, StrictUtf8);
        }

        private static string CompilableProject(string repositoryRoot)
        {
            string Project(string relativePath) =>
                Path.Combine(
                    repositoryRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));

            return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <ProjectReference Include="{{Project("src/BuildingBlocks/Full.NET.Abstractions/Full.NET.Abstractions.csproj")}}" />
                <ProjectReference Include="{{Project("src/BuildingBlocks/Full.NET.Data.Abstractions/Full.NET.Data.Abstractions.csproj")}}" />
                <ProjectReference Include="{{Project("src/BuildingBlocks/Full.NET.Hosting/Full.NET.Hosting.csproj")}}" />
                <ProjectReference Include="{{Project("src/BuildingBlocks/Full.NET.Modularity/Full.NET.Modularity.csproj")}}" />
                <ProjectReference Include="{{Project("src/Modules/Full.NET.Modules.Identity.Contracts/Full.NET.Modules.Identity.Contracts.csproj")}}" />
              </ItemGroup>
            </Project>
            """;
        }

        private static string CompositionProject(string repositoryRoot)
        {
            var modularityProject = Path.Combine(
                repositoryRoot,
                "src",
                "BuildingBlocks",
                "Full.NET.Modularity",
                "Full.NET.Modularity.csproj");
            return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{modularityProject}}" />
              </ItemGroup>
            </Project>
            """;
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "Full.NET.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "无法定位仓库根目录。");
        }

        private const string UnderReferencedProject =
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
              </ItemGroup>
            </Project>
            """;

        internal const string ModuleEntry =
            """
            using System.Collections.Generic;
            using Full.NET.Modularity.Modules;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Modules.Catalog;

            public sealed class CatalogModule : IFullNetModule
            {
                public string Name => "Catalog";

                public IReadOnlyCollection<string> Dependencies => [];

                public void AddServices(
                    IServiceCollection services,
                    IConfiguration configuration)
                {
                    services.AddOptions();
                }

                public void MapEndpoints(IEndpointRouteBuilder endpoints)
                {
                    endpoints.MapGet("/catalog", () => "catalog");
                }
            }
            """;

        private const string CompositionCatalog =
            """
            using System.Collections.Generic;
            using Full.NET.Modularity.Modules;

            namespace Acme.Composition;

            public static class ModuleCatalog
            {
                private static IReadOnlyList<IFullNetModule> CreateModules() =>
                [
                ];
            }
            """;

        private const string VueRouter =
            """
            export function createAppRouter() {
              return createRouter({
                routes: [
                  { name: 'overview', path: '/', component: OverviewView },
                  { path: '/403', component: loadStatusView },
                  { path: '/:pathMatch(.*)*', redirect: '/404' }
                ]
              });
            }

            """;

        private const string LayuiRouter =
            """
            function defineController(importController, exportName, root, options) {
              return { importController, create: module => module[exportName](root, options) };
            }

            export function createLayuiRouteControllerDefinitions(root, options) {
              const sharedOptions = {};
              return new Map([
                ['/', defineController(
                  () => import('./overview-dashboard.js'),
                  'createOverviewDashboardController',
                  root,
                  sharedOptions
                )]
              ]);
            }

            """;

        private const string SchemaJson =
            """
            {
              "ownerKey": "acme",
              "moduleKey": "catalog",
              "entityKey": "product",
              "databaseTableName": "acme_catalog_product",
              "rootNamespace": "Acme.Modules.Catalog",
              "clrTypeName": "Product",
              "apiResourceName": "products",
              "permissionResourceName": "products",
              "dataScope": "TenantRequired",
              "hasVersion": true,
              "columns": [
                {
                  "databaseName": "Id",
                  "clrPropertyName": "Id",
                  "jsonPropertyName": "id",
                  "scalarType": "Uuid"
                },
                {
                  "databaseName": "TenantId",
                  "clrPropertyName": "TenantId",
                  "jsonPropertyName": "tenantId",
                  "scalarType": "Uuid"
                },
                {
                  "databaseName": "Name",
                  "clrPropertyName": "Name",
                  "jsonPropertyName": "name",
                  "scalarType": "String",
                  "maxLength": 200
                },
                {
                  "databaseName": "IsActive",
                  "clrPropertyName": "IsActive",
                  "jsonPropertyName": "isActive",
                  "scalarType": "Boolean"
                },
                {
                  "databaseName": "Version",
                  "clrPropertyName": "Version",
                  "jsonPropertyName": "version",
                  "scalarType": "Int64"
                },
                {
                  "databaseName": "CreatedAtUtc",
                  "clrPropertyName": "CreatedAtUtc",
                  "jsonPropertyName": "createdAtUtc",
                  "scalarType": "DateTimeUtc"
                }
              ]
            }
            """;

        private const string TargetJson =
            """
            {
              "moduleName": "Catalog",
              "moduleProjectPath": "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
              "moduleEntryPointPath": "src/Modules/Acme.Modules.Catalog/CatalogModule.cs",
              "compositionProjectPath": "src/Composition/Acme.Composition/Acme.Composition.csproj",
              "compositionCatalogPath": "src/Composition/Acme.Composition/ModuleCatalog.cs",
              "vueRouterPath": "ui/admin/src/router/index.ts",
              "layuiRouterPath": "ui/admin-layui/js/core/route-controllers.js"
            }
            """;

        private const string TargetJsonWithClientRoute =
            """
            {
              "moduleName": "Catalog",
              "moduleProjectPath": "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
              "moduleEntryPointPath": "src/Modules/Acme.Modules.Catalog/CatalogModule.cs",
              "compositionProjectPath": "src/Composition/Acme.Composition/Acme.Composition.csproj",
              "compositionCatalogPath": "src/Composition/Acme.Composition/ModuleCatalog.cs",
              "vueRouterPath": "ui/admin/src/router/index.ts",
              "layuiRouterPath": "ui/admin-layui/js/core/route-controllers.js",
              "clientRoute": {
                "routePath": "/catalog/products",
                "vueRouteName": "catalog-products",
                "vueComponentPath": "ui/admin/src/views/CatalogProductsView.vue",
                "layuiControllerPath": "ui/admin-layui/js/core/catalog-products.js",
                "layuiControllerExport": "createCatalogProductsController"
              }
            }
            """;
    }
}
