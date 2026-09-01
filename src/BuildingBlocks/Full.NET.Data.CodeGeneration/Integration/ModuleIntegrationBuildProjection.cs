using System.Collections.ObjectModel;
using System.Text;
using System.Xml.Linq;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 表示只存在于系统临时目录中的一个编译源文件。
/// 确定性：落盘路径使用正斜杠与全小写的 GUID 前缀目录，编译输出不因临时目录位置漂移；内容为空字符串时 FAIL-closed 抛异常，禁止写入空壳文件。
/// </summary>
/// <param name="FullPath">临时目录中文件的绝对路径。</param>
/// <param name="Content">写入临时文件的完整 UTF-8 文本。</param>
public sealed record ModuleIntegrationProjectedSourceFile(
    [property: System.ComponentModel.Description("临时目录中文件的绝对路径；由 ModuleIntegrationBuildProjection 在 projectionRoot/generated 下生成。")]
    string FullPath,
    [property: System.ComponentModel.Description("写入临时文件的完整 UTF-8 文本；包括候选 Catalog、探针入口或替换过的模块入口。")]
    string Content);

/// <summary>
/// 保存后端生成物、接入探针与临时 MSBuild 注入文件的纯内存投影。
/// </summary>
public sealed class ModuleIntegrationBuildProjection
{
    private ModuleIntegrationBuildProjection(
        string moduleProjectFullPath,
        IEnumerable<ModuleIntegrationProjectedSourceFile> sourceFiles,
        string targetsPath,
        string targetsContent)
    {
        ModuleProjectFullPath = moduleProjectFullPath;
        SourceFiles =
            new ReadOnlyCollection<ModuleIntegrationProjectedSourceFile>(
                sourceFiles.ToArray());
        TargetsPath = targetsPath;
        TargetsContent = targetsContent;
    }

    /// <summary>被投影的真实模块项目绝对路径，用于 MSBuild 注入。</summary>
    public string ModuleProjectFullPath { get; }

    /// <summary>临时生成目录下的候选源文件列表，包括编译探针。</summary>
    public IReadOnlyList<ModuleIntegrationProjectedSourceFile> SourceFiles
    {
        get;
    }

    /// <summary>临时 MSBuild targets 文件的绝对路径，用于在编译时替换真实模块入口。</summary>
    public string TargetsPath { get; }

    /// <summary>临时 targets 文件的完整内容，描述 Compile 项移除与候选 Include。</summary>
    public string TargetsContent { get; }

    /// <summary>
    /// 创建只替换手写模块入口的临时投影，模块中已落盘的生成产物继续由真实项目读取。
    /// </summary>
    public static ModuleIntegrationBuildProjection CreateEntryCandidate(
        FullNetCrudSchema schema,
        string moduleProjectFullPath,
        string moduleEntryFullPath,
        string desiredEntryContent,
        string projectionRoot)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(desiredEntryContent);
        if (!Path.IsPathFullyQualified(moduleEntryFullPath))
        {
            throw new ArgumentException(
                "模块入口路径必须是绝对路径。",
                nameof(moduleEntryFullPath));
        }

        return Create(
            schema,
            moduleProjectFullPath,
            projectionRoot,
            [Path.GetFullPath(moduleEntryFullPath)],
            [
                new GeneratedArtifact(
                    Path.GetFileName(moduleEntryFullPath),
                    GeneratedArtifactKind.Backend,
                    desiredEntryContent),
            ]);
    }

    /// <summary>
    /// 创建不访问文件系统的临时构建投影，仅选择后端生成产物。
    /// </summary>
    public static ModuleIntegrationBuildProjection Create(
        FullNetCrudSchema schema,
        string moduleProjectFullPath,
        string projectionRoot,
        IReadOnlyList<string>? sourcePathsToRemove = null,
        IReadOnlyList<GeneratedArtifact>? candidateArtifacts = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!Path.IsPathFullyQualified(moduleProjectFullPath))
        {
            throw new ArgumentException(
                "模块项目路径必须是绝对路径。",
                nameof(moduleProjectFullPath));
        }

        if (!Path.IsPathFullyQualified(projectionRoot))
        {
            throw new ArgumentException(
                "临时投影根目录必须是绝对路径。",
                nameof(projectionRoot));
        }

        var generatedDirectory = Path.Combine(
            projectionRoot,
            "generated");
        var effectiveArtifacts = candidateArtifacts
            ?? [
                .. ModuleIntegrationBackendWorkspace.CreateArtifacts(schema),
                ModuleIntegrationBackendWorkspace.CreateRegistryArtifact(
                    schema,
                    [schema.ClrTypeName]),
            ];
        if (effectiveArtifacts.Any(artifact =>
                artifact.Kind != GeneratedArtifactKind.Backend))
        {
            throw new ArgumentException(
                "模块编译候选只能包含后端产物。",
                nameof(candidateArtifacts));
        }

        var sourceFiles = effectiveArtifacts
            .OrderBy(
                artifact => artifact.RelativePath,
                StringComparer.Ordinal)
            .Select(artifact => new ModuleIntegrationProjectedSourceFile(
                Path.Combine(
                    generatedDirectory,
                    Path.GetFileName(artifact.RelativePath)),
                artifact.Content))
            .ToList();
        sourceFiles.Add(new ModuleIntegrationProjectedSourceFile(
            Path.Combine(
                generatedDirectory,
                $"{schema.ClrTypeName}IntegrationCompileProbe.g.cs"),
            GenerateProbe(schema)));

        var targetsPath = Path.Combine(
            projectionRoot,
            "FullNet.ModuleIntegration.targets");
        var normalizedRemovalPaths = (
                sourcePathsToRemove
                ?? Array.Empty<string>())
            .Select(path =>
            {
                if (!Path.IsPathFullyQualified(path))
                {
                    throw new ArgumentException(
                        "待替换的模块源码路径必须是绝对路径。",
                        nameof(sourcePathsToRemove));
                }

                return Path.GetFullPath(path);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        return new ModuleIntegrationBuildProjection(
            Path.GetFullPath(moduleProjectFullPath),
            sourceFiles,
            targetsPath,
            GenerateTargets(
                sourceFiles,
                normalizedRemovalPaths));
    }

    private static string GenerateProbe(FullNetCrudSchema schema) =>
        Normalize(
            $$"""
            #nullable enable

            using {{schema.RootNamespace}}.Generated;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.DependencyInjection;

            namespace {{schema.RootNamespace}};

            public static class {{schema.ClrTypeName}}IntegrationCompileProbe
            {
                internal static void Register(
                    IServiceCollection services,
                    IEndpointRouteBuilder endpoints)
                {
                    services.AddFullNetGeneratedModuleFeatures();
                    endpoints.MapFullNetGeneratedModuleFeatures();
                }
            }
            """);

    private static string GenerateTargets(
        IReadOnlyList<ModuleIntegrationProjectedSourceFile> sourceFiles,
        IReadOnlyList<string> sourcePathsToRemove)
    {
        var compileItems = sourcePathsToRemove
            .Select(path =>
                new XElement(
                    "Compile",
                    new XAttribute("Remove", path)))
            .Concat(sourceFiles.Select(file =>
                new XElement(
                    "Compile",
                    new XAttribute("Include", file.FullPath),
                    new XAttribute(
                        "Link",
                        $"Generated/{Path.GetFileName(file.FullPath)}"))));
        var document = new XDocument(
            new XElement(
                "Project",
                new XElement(
                    "ItemGroup",
                    new XAttribute(
                        "Condition",
                        "'$(MSBuildProjectFullPath)' "
                        + "== '$(FullNetModuleIntegrationProject)'"),
                    compileItems)));
        return Normalize(document.ToString(SaveOptions.None));
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
