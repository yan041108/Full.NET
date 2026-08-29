using System.Xml.Linq;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class BuildingBlockProjectDependencyBoundaryTests
{
    [TestMethod]
    public void Building_block_projects_must_not_reference_business_module_projects()
    {
        var offenders = BuildingBlockProjectDependencyGuard.FindModuleReferences(
            Path.Combine(FindRepositoryRoot(), "src", "BuildingBlocks"));

        Assert.HasCount(
            0,
            offenders,
            "BuildingBlocks 不得反向引用 Full.NET.Modules.* 项目。违规: "
            + string.Join("; ", offenders));
    }

    [TestMethod]
    public void Module_contract_project_reference_is_detected_by_prefix()
    {
        Assert.IsTrue(
            BuildingBlockProjectDependencyGuard.IsModuleReference(
                @"..\..\Modules\Full.NET.Modules.Settings.Contracts\Full.NET.Modules.Settings.Contracts.csproj"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}

internal static class BuildingBlockProjectDependencyGuard
{
    internal static IReadOnlyList<string> FindModuleReferences(string buildingBlocksRoot)
    {
        var offenders = new List<string>();
        foreach (var projectPath in Directory.EnumerateFiles(
                     buildingBlocksRoot,
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            var projectName = Path.GetFileName(projectPath);
            foreach (var reference in XDocument.Load(projectPath)
                         .Descendants("ProjectReference")
                         .Select(element => (string?)element.Attribute("Include"))
                         .Where(include => !string.IsNullOrWhiteSpace(include)))
            {
                if (IsModuleReference(reference!))
                {
                    offenders.Add($"{projectName} -> {reference}");
                }
            }
        }

        return offenders;
    }

    internal static bool IsModuleReference(string projectReference)
    {
        var referencedProject = Path.GetFileNameWithoutExtension(
            projectReference.Replace('\u005c', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar));
        return referencedProject.StartsWith("Full.NET.Modules.", StringComparison.OrdinalIgnoreCase);
    }
}
