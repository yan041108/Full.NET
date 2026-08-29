using System.Xml.Linq;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class WorkflowModuleArchitectureTests
{
    [TestMethod]
    public void Workflow_module_references_only_approved_building_blocks_and_identity_contracts()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var projectPath = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Workflow",
            "Full.NET.Modules.Workflow.csproj");
        var document = XDocument.Load(projectPath);
        var references = document.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(
                element.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "Full.NET.Hosting",
                "Full.NET.Modularity",
                "Full.NET.Modules.Identity.Contracts",
            },
            references);
        Assert.IsFalse(document.Descendants("PackageReference").Any());
    }

    [TestMethod]
    public void Workflow_module_does_not_use_runtime_dynamic_code_or_concrete_sql_drivers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleRoot = Path.Combine(root, "src", "Modules", "Full.NET.Modules.Workflow");
        var source = string.Join(
            '\n',
            Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        foreach (var forbidden in new[]
        {
            "System.Reflection.Emit",
            "Assembly.Load",
            "Activator.CreateInstance",
            "Microsoft.Data.SqlClient",
            "MySqlConnector",
            "Dapper.SqlMapper",
        })
        {
            Assert.IsFalse(
                source.Contains(forbidden, StringComparison.Ordinal),
                $"Workflow 模块包含禁止的动态或具体驱动依赖：{forbidden}");
        }
    }
}
