using System.Collections.ObjectModel;
using System.Text;
using System.Xml.Linq;

using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 保存 Composition Catalog 候选与临时 MSBuild 注入文件的纯内存投影。
/// </summary>
internal sealed class CompositionIntegrationBuildProjection
{
    private CompositionIntegrationBuildProjection(
        string compositionProjectFullPath,
        IEnumerable<ModuleIntegrationProjectedSourceFile> sourceFiles,
        string targetsPath,
        string targetsContent)
    {
        CompositionProjectFullPath = compositionProjectFullPath;
        SourceFiles =
            new ReadOnlyCollection<ModuleIntegrationProjectedSourceFile>(
                sourceFiles.ToArray());
        TargetsPath = targetsPath;
        TargetsContent = targetsContent;
    }

    public string CompositionProjectFullPath { get; }

    public IReadOnlyList<ModuleIntegrationProjectedSourceFile> SourceFiles
    {
        get;
    }

    public string TargetsPath { get; }

    public string TargetsContent { get; }

    public static CompositionIntegrationBuildProjection Create(
        string compositionProjectFullPath,
        string moduleProjectFullPath,
        string compositionCatalogFullPath,
        string desiredCatalogContent,
        bool includeModuleReference,
        string projectionRoot)
    {
        ArgumentNullException.ThrowIfNull(desiredCatalogContent);
        RequireAbsolute(
            compositionProjectFullPath,
            nameof(compositionProjectFullPath));
        RequireAbsolute(
            moduleProjectFullPath,
            nameof(moduleProjectFullPath));
        RequireAbsolute(
            compositionCatalogFullPath,
            nameof(compositionCatalogFullPath));
        RequireAbsolute(projectionRoot, nameof(projectionRoot));

        var sourceFile = new ModuleIntegrationProjectedSourceFile(
            Path.Combine(
                projectionRoot,
                "generated",
                Path.GetFileName(compositionCatalogFullPath)),
            desiredCatalogContent);
        var targetsPath = Path.Combine(
            projectionRoot,
            "FullNet.CompositionIntegration.targets");
        return new CompositionIntegrationBuildProjection(
            Path.GetFullPath(compositionProjectFullPath),
            [sourceFile],
            targetsPath,
            GenerateTargets(
                compositionProjectFullPath,
                moduleProjectFullPath,
                compositionCatalogFullPath,
                sourceFile.FullPath,
                includeModuleReference));
    }

    private static string GenerateTargets(
        string compositionProjectFullPath,
        string moduleProjectFullPath,
        string compositionCatalogFullPath,
        string candidateCatalogFullPath,
        bool includeModuleReference)
    {
        var items = new List<XElement>();
        if (includeModuleReference)
        {
            items.Add(new XElement(
                "ProjectReference",
                new XAttribute(
                    "Include",
                    Path.GetFullPath(moduleProjectFullPath))));
        }

        items.Add(new XElement(
            "Compile",
            new XAttribute(
                "Remove",
                Path.GetFullPath(compositionCatalogFullPath))));
        items.Add(new XElement(
            "Compile",
            new XAttribute(
                "Include",
                candidateCatalogFullPath),
            new XAttribute(
                "Link",
                Path.GetFileName(compositionCatalogFullPath))));
        var document = new XDocument(
            new XElement(
                "Project",
                new XElement(
                    "ItemGroup",
                    new XAttribute(
                        "Condition",
                        "'$(MSBuildProjectFullPath)' "
                        + "== '$(FullNetCompositionIntegrationProject)'"),
                    items)));
        return Normalize(document.ToString(SaveOptions.None));
    }

    private static void RequireAbsolute(
        string path,
        string parameterName)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "Composition 编译投影路径必须是绝对路径。",
                parameterName);
        }
    }

    private static string Normalize(string content)
    {
        var builder = new StringBuilder(content.Length + 1);
        builder.Append(content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\r', '\n'));
        builder.Append('\n');
        return builder.ToString();
    }
}
