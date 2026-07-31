using System.Xml.Linq;
using Full.NET.CodeGeneration.Cli;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class ModuleIntegrationBuildProjectionTests
{
    [TestMethod]
    public void Create_projects_backend_and_probe_without_writing()
    {
        var projectionRoot = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-codegen-projection-test-{Guid.NewGuid():N}");
        var moduleProject = Path.GetFullPath(Path.Combine(
            "src",
            "Modules",
            "Acme.Modules.Catalog",
            "Acme.Modules.Catalog.csproj"));
        var moduleDirectory = Path.GetDirectoryName(moduleProject)!;
        var sourcePathsToRemove = new[]
        {
            Path.Combine(
                moduleDirectory,
                "Generated",
                "Product",
                "ProductContracts.g.cs"),
            Path.Combine(
                moduleDirectory,
                "Generated",
                "Product",
                "ProductSql.g.cs"),
        };

        var projection = ModuleIntegrationBuildProjection.Create(
            FullNetCrudSchemaTests.CreateProductSchema(),
            moduleProject,
            projectionRoot,
            sourcePathsToRemove);

        Assert.AreEqual(moduleProject, projection.ModuleProjectFullPath);
        Assert.HasCount(7, projection.SourceFiles);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "FullNetGeneratedModuleFeatures.g.cs",
                "ProductContracts.g.cs",
                "ProductEndpoint.g.cs",
                "ProductFeature.g.cs",
                "ProductRecord.g.cs",
                "ProductSql.g.cs",
                "ProductIntegrationCompileProbe.g.cs",
            },
            projection.SourceFiles
                .Select(file => Path.GetFileName(file.FullPath))
                .ToArray());
        Assert.IsFalse(projection.SourceFiles.Any(file =>
            file.FullPath.Contains("clients", StringComparison.Ordinal)
            || file.FullPath.Contains("reports", StringComparison.Ordinal)
            || file.FullPath.Contains("templates", StringComparison.Ordinal)));
        var probe = projection.SourceFiles.Single(file =>
            file.FullPath.EndsWith(
                "ProductIntegrationCompileProbe.g.cs",
                StringComparison.Ordinal));
        StringAssert.Contains(
            probe.Content,
            "using Acme.Modules.Catalog.Generated;");
        StringAssert.Contains(
            probe.Content,
            "services.AddFullNetGeneratedModuleFeatures()");
        StringAssert.Contains(
            probe.Content,
            "endpoints.MapFullNetGeneratedModuleFeatures()");

        var targets = XDocument.Parse(projection.TargetsContent);
        var compileItems = targets
            .Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .ToArray();
        var removeItems = compileItems
            .Where(item => item.Attribute("Remove") is not null)
            .ToArray();
        var includeItems = compileItems
            .Where(item => item.Attribute("Include") is not null)
            .ToArray();
        Assert.HasCount(2, removeItems);
        CollectionAssert.AreEquivalent(
            sourcePathsToRemove,
            removeItems
                .Select(item => item.Attribute("Remove")!.Value)
                .ToArray());
        Assert.HasCount(7, includeItems);
        Assert.IsTrue(includeItems.All(item =>
            item.Attribute("Include") is not null
            && item.Attribute("Link")!.Value.StartsWith(
                "Generated/",
                StringComparison.Ordinal)));
        StringAssert.Contains(
            targets.Root!.Element("ItemGroup")!.Attribute("Condition")!.Value,
            "$(FullNetModuleIntegrationProject)");
        Assert.IsFalse(Directory.Exists(projectionRoot));
    }

    [TestMethod]
    public void CreateEntryCandidate_replaces_only_module_entry_and_adds_probe()
    {
        var projectionRoot = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-codegen-entry-projection-test-{Guid.NewGuid():N}");
        var moduleProject = Path.GetFullPath(Path.Combine(
            "src",
            "Modules",
            "Acme.Modules.Catalog",
            "Acme.Modules.Catalog.csproj"));
        var moduleEntry = Path.Combine(
            Path.GetDirectoryName(moduleProject)!,
            "CatalogModule.cs");

        var projection = ModuleIntegrationBuildProjection.CreateEntryCandidate(
            FullNetCrudSchemaTests.CreateProductSchema(),
            moduleProject,
            moduleEntry,
            "namespace Acme.Modules.Catalog;\n",
            projectionRoot);

        Assert.HasCount(2, projection.SourceFiles);
        Assert.IsTrue(projection.SourceFiles.Any(file =>
            Path.GetFileName(file.FullPath) == "CatalogModule.cs"
            && file.Content == "namespace Acme.Modules.Catalog;\n"));
        Assert.IsTrue(projection.SourceFiles.Any(file =>
            Path.GetFileName(file.FullPath)
                == "ProductIntegrationCompileProbe.g.cs"));

        var compileItems = XDocument.Parse(projection.TargetsContent)
            .Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .ToArray();
        Assert.HasCount(
            1,
            compileItems.Where(item =>
                item.Attribute("Remove")?.Value == moduleEntry));
        Assert.HasCount(
            2,
            compileItems.Where(item =>
                item.Attribute("Include") is not null));
        Assert.IsFalse(Directory.Exists(projectionRoot));
    }
}
