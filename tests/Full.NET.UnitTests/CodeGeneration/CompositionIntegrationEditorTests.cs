using Full.NET.CodeGeneration.Cli;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CompositionIntegrationEditorTests
{
    [TestMethod]
    public void Project_edit_adds_exact_relative_reference_to_existing_group()
    {
        var result = CompositionProjectEditor.Edit(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\..\BuildingBlocks\Acme.Core\Acme.Core.csproj" />
              </ItemGroup>
            </Project>
            """,
            "src/Composition/Acme.Composition/Acme.Composition.csproj",
            "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj");

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.Changed);
        StringAssert.Contains(
            result.DesiredContent,
            """
              <ItemGroup>
                <ProjectReference Include="..\..\BuildingBlocks\Acme.Core\Acme.Core.csproj" />
                <ProjectReference Include="..\..\Modules\Acme.Modules.Catalog\Acme.Modules.Catalog.csproj" />
              </ItemGroup>
            """);
    }

    [TestMethod]
    public void Project_edit_is_idempotent_for_equivalent_reference()
    {
        const string source =
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="../../Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj" />
              </ItemGroup>
            </Project>
            """;

        var result = CompositionProjectEditor.Edit(
            source,
            "src/Composition/Acme.Composition/Acme.Composition.csproj",
            "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj");

        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.Changed);
        Assert.AreEqual(source, result.DesiredContent);
    }

    [TestMethod]
    public void Project_edit_rejects_multiple_project_reference_groups()
    {
        var result = CompositionProjectEditor.Edit(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="A.csproj" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="B.csproj" />
              </ItemGroup>
            </Project>
            """,
            "src/Composition/Acme.Composition/Acme.Composition.csproj",
            "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj");

        Assert.IsFalse(result.Succeeded);
        CollectionAssert.Contains(
            result.Diagnostics.ToArray(),
            "Composition 项目必须且只能存在一个可验证的 ProjectReference ItemGroup。");
    }

    [TestMethod]
    public void Catalog_edit_adds_using_and_module_at_list_tail()
    {
        var result = CompositionCatalogEditor.Edit(
            """
            using Full.NET.Modularity.Modules;
            using Acme.Modules.Identity;

            namespace Acme.Composition;

            public static class ModuleCatalog
            {
                private static IReadOnlyList<IFullNetModule> CreateModules() =>
                [
                    new IdentityModule(),
                ];
            }
            """,
            "Acme.Modules.Catalog",
            "Catalog");

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.Changed);
        StringAssert.Contains(
            result.DesiredContent,
            "using Acme.Modules.Catalog;");
        StringAssert.Contains(
            result.DesiredContent,
            """
                    new IdentityModule(),
                    new CatalogModule(),
                ];
            """);
    }

    [TestMethod]
    public void Catalog_edit_is_idempotent_when_exact_construction_exists()
    {
        const string source =
            """
            using Full.NET.Modularity.Modules;
            using Acme.Modules.Catalog;

            namespace Acme.Composition;

            public static class ModuleCatalog
            {
                private static IReadOnlyList<IFullNetModule> CreateModules() =>
                [
                    new CatalogModule(),
                ];
            }
            """;

        var result = CompositionCatalogEditor.Edit(
            source,
            "Acme.Modules.Catalog",
            "Catalog");

        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.Changed);
        Assert.AreEqual(source, result.DesiredContent);
    }

    [TestMethod]
    public void Catalog_edit_ignores_decoys_and_rejects_nonstandard_shape()
    {
        var result = CompositionCatalogEditor.Edit(
            """
            using Full.NET.Modularity.Modules;

            namespace Acme.Composition;

            public static class ModuleCatalog
            {
                // new CatalogModule(),
                private static IReadOnlyList<IFullNetModule> CreateModules()
                {
                    var example = "new CatalogModule(),";
                    return [];
                }
            }
            """,
            "Acme.Modules.Catalog",
            "Catalog");

        Assert.IsFalse(result.Succeeded);
        CollectionAssert.Contains(
            result.Diagnostics.ToArray(),
            "Composition Catalog 必须包含唯一的标准 CreateModules() => [ ... ];。");
    }
}
