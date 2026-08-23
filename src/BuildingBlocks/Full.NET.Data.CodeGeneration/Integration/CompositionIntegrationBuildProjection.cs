using System.Collections.ObjectModel;
using System.Text;
using System.Xml.Linq;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 保存 Composition Catalog 候选与临时 MSBuild 注入文件的纯内存投影。
/// </summary>
public sealed class CompositionIntegrationBuildProjection
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

    /// <summary>被投影的真实 Composition 项目绝对路径，用于 MSBuild 注入。</summary>
    public string CompositionProjectFullPath { get; }

    /// <summary>临时目录下的候选 Catalog 源文件列表。</summary>
    public IReadOnlyList<ModuleIntegrationProjectedSourceFile> SourceFiles
    {
        get;
    }

    /// <summary>临时 MSBuild targets 文件的绝对路径。</summary>
    public string TargetsPath { get; }

    /// <summary>临时 targets 文件的完整内容，描述 ProjectReference 与 Catalog 替换。</summary>
    public string TargetsContent { get; }

    /// <summary>
    /// 创建不访问文件系统的临时 Composition 编译投影，候选 Catalog 与可选 ProjectReference 注入。
    /// </summary>
    /// <param name="compositionProjectFullPath">真实 Composition 项目绝对路径</param>
    /// <param name="moduleProjectFullPath">目标模块项目绝对路径</param>
    /// <param name="compositionCatalogFullPath">真实 Catalog 绝对路径</param>
    /// <param name="desiredCatalogContent">候选 Catalog 内容</param>
    /// <param name="includeModuleReference">是否在 targets 中注入临时 ProjectReference</param>
    /// <param name="projectionRoot">临时投影根目录绝对路径</param>
    /// <returns>纯内存投影，调用方负责落盘与清理</returns>
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
