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

    /// <summary>
    /// 创建仅替换 Catalog 文件并按需追加模块 ProjectReference 的临时构建投影；所有源码均写入 <paramref name="projectionRoot"/>，不触碰仓库实际文件。
    /// </summary>
    /// <remarks>
    /// <paramref name="includeModuleReference"/> 仅在 Composition 项目首次接入模块时为 true；再次执行时若 ProjectReference 已存在则跳过，避免 MSBuild 报重复引用错误。
    /// </remarks>
    /// <param name="compositionProjectFullPath">Composition 项目文件的绝对路径，作为真实构建目标。</param>
    /// <param name="moduleProjectFullPath">模块项目文件的绝对路径，仅在需要追加引用时使用。</param>
    /// <param name="compositionCatalogFullPath">Composition Catalog 在仓库内的绝对路径，构建时由候选文件替换。</param>
    /// <param name="desiredCatalogContent">候选 Catalog 内容，必须由编辑器保证结构正确。</param>
    /// <param name="includeModuleReference">是否在临时 targets 中追加模块 ProjectReference。</param>
    /// <param name="projectionRoot">系统临时目录下的绝对路径，所有候选源码与 targets 写入此目录。</param>
    /// <returns>临时构建投影，调用方负责写入磁盘并在使用后删除目录。</returns>
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
