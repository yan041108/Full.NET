using System.Xml.Linq;
using Full.NET.CodeGeneration.Cli;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CompositionIntegrationBuildProjectionTests
{
    [TestMethod]
    public void Create_injects_module_reference_and_replaces_catalog_without_writing()
    {
        var projectionRoot = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-composition-projection-test-{Guid.NewGuid():N}");
        var compositionProject = Path.GetFullPath(
            "src/Composition/Acme.Composition/Acme.Composition.csproj");
        var moduleProject = Path.GetFullPath(
            "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj");
        var catalog = Path.GetFullPath(
            "src/Composition/Acme.Composition/ModuleCatalog.cs");

        var projection = CompositionIntegrationBuildProjection.Create(
            compositionProject,
            moduleProject,
            catalog,
            "namespace Acme.Composition;\n",
            includeModuleReference: true,
            projectionRoot);

        Assert.AreEqual(
            compositionProject,
            projection.CompositionProjectFullPath);
        Assert.HasCount(1, projection.SourceFiles);
        Assert.AreEqual(
            "namespace Acme.Composition;\n",
            projection.SourceFiles[0].Content);
        var compileItems = XDocument.Parse(projection.TargetsContent)
            .Descendants()
            .ToArray();
        Assert.HasCount(
            1,
            compileItems.Where(item =>
                item.Name.LocalName == "ProjectReference"
                && item.Attribute("Include")?.Value == moduleProject));
        Assert.HasCount(
            1,
            compileItems.Where(item =>
                item.Name.LocalName == "Compile"
                && item.Attribute("Remove")?.Value == catalog));
        Assert.HasCount(
            1,
            compileItems.Where(item =>
                item.Name.LocalName == "Compile"
                && item.Attribute("Include")?.Value
                    == projection.SourceFiles[0].FullPath));
        Assert.IsFalse(Directory.Exists(projectionRoot));
    }
}
