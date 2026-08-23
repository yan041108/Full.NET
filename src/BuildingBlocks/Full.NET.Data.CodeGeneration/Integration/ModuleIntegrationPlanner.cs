using System.Xml.Linq;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 根据显式目标和只读文本快照生成保守的模块接入建议。
/// </summary>
public static class ModuleIntegrationPlanner
{
    /// <summary>
    /// 根据显式目标和只读文本快照生成保守的模块接入建议，仅做规划而不写盘。
    /// </summary>
    /// <param name="schema">待接入实体的 CRUD Schema</param>
    /// <param name="target">显式声明的接入目标路径与可选客户端路由</param>
    /// <param name="snapshot">只读文件文本快照，禁止访问真实文件系统</param>
    /// <returns>包含 8 个固定影响区域的接入计划，每项给出保守状态与说明</returns>
    public static ModuleIntegrationPlan Plan(
        FullNetCrudSchema schema,
        ModuleIntegrationTarget target,
        ModuleIntegrationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);

        var moduleProjectExists = snapshot.TryGetContent(
            target.ModuleProjectPath,
            out _);
        var moduleEntryExists = snapshot.TryGetContent(
            target.ModuleEntryPointPath,
            out var moduleEntry);
        var backendDirectory = string.Concat(
            RepositoryDirectory(target.ModuleProjectPath),
            "/Generated");
        var backendCount = CrudArtifactGenerator.Generate(schema).Count(
            artifact => artifact.Kind == GeneratedArtifactKind.Backend);
        const string addFeature =
            "AddFullNetGeneratedModuleFeatures";
        const string mapFeature =
            "MapFullNetGeneratedModuleFeatures";

        var items = new List<ModuleIntegrationPlanItem>(8)
        {
            new(
                ModuleIntegrationArea.BackendArtifacts,
                moduleProjectExists
                    ? ModuleIntegrationStatus.ChangeRequired
                    : ModuleIntegrationStatus.Blocked,
                backendDirectory,
                moduleProjectExists
                    ? $"将 {backendCount} 个后端生成产物规划到该目录；本命令不会写入。"
                    : "模块项目不存在，无法确认后端生成产物的编译边界。"),
            new(
                ModuleIntegrationArea.ModuleProject,
                moduleProjectExists
                    ? ModuleIntegrationStatus.Satisfied
                    : ModuleIntegrationStatus.Blocked,
                target.ModuleProjectPath,
                moduleProjectExists
                    ? "模块项目存在；SDK 默认会包含同目录下的 .g.cs 文件。"
                    : "必须先创建或修正显式指定的模块项目。"),
            RegistrationItem(
                ModuleIntegrationArea.ModuleServices,
                target.ModuleEntryPointPath,
                moduleEntryExists,
                moduleEntry,
                addFeature),
            RegistrationItem(
                ModuleIntegrationArea.ModuleEndpoints,
                target.ModuleEntryPointPath,
                moduleEntryExists,
                moduleEntry,
                mapFeature),
            CompositionProjectItem(
                target,
                snapshot,
                moduleProjectExists),
            CompositionCatalogItem(
                schema,
                target,
                snapshot,
                moduleProjectExists),
            RouteItem(
                ModuleIntegrationArea.VueRoute,
                target,
                snapshot,
                vue: true),
            RouteItem(
                ModuleIntegrationArea.LayuiRoute,
                target,
                snapshot,
                vue: false),
        };

        return new ModuleIntegrationPlan(items);
    }

    private static ModuleIntegrationPlanItem RegistrationItem(
        ModuleIntegrationArea area,
        string path,
        bool entryExists,
        string? entry,
        string extensionName)
    {
        if (!entryExists)
        {
            return new ModuleIntegrationPlanItem(
                area,
                ModuleIntegrationStatus.Blocked,
                path,
                "模块入口不存在，不能规划服务或 Endpoint 注册。");
        }

        var satisfied = ContainsExactInvocation(
            entry!,
            extensionName);
        return new ModuleIntegrationPlanItem(
            area,
            satisfied
                ? ModuleIntegrationStatus.Satisfied
                : ModuleIntegrationStatus.ChangeRequired,
            path,
            satisfied
                ? $"已检测到精确扩展调用 {extensionName}。"
                : $"在对应模块生命周期中显式调用 {extensionName}。");
    }

    private static ModuleIntegrationPlanItem CompositionProjectItem(
        ModuleIntegrationTarget target,
        ModuleIntegrationSnapshot snapshot,
        bool moduleProjectExists)
    {
        if (!moduleProjectExists)
        {
            return new ModuleIntegrationPlanItem(
                ModuleIntegrationArea.CompositionProject,
                ModuleIntegrationStatus.Blocked,
                target.CompositionProjectPath,
                "目标模块项目不存在，Composition 引用即使出现也属于悬空接入。");
        }

        if (!snapshot.TryGetContent(
                target.CompositionProjectPath,
                out var project))
        {
            return new ModuleIntegrationPlanItem(
                ModuleIntegrationArea.CompositionProject,
                ModuleIntegrationStatus.Blocked,
                target.CompositionProjectPath,
                "Composition 项目不存在，无法确认模块项目引用。");
        }

        var referenced = HasProjectReference(
            project,
            target.CompositionProjectPath,
            target.ModuleProjectPath);
        return new ModuleIntegrationPlanItem(
            ModuleIntegrationArea.CompositionProject,
            referenced switch
            {
                true => ModuleIntegrationStatus.Satisfied,
                false => ModuleIntegrationStatus.ChangeRequired,
                null => ModuleIntegrationStatus.Blocked,
            },
            target.CompositionProjectPath,
            referenced switch
            {
                true => "Composition 已精确引用目标模块项目。",
                false => "Composition 需要显式引用目标模块项目。",
                null => "Composition 项目 XML 无法解析，必须先修复项目文件。",
            });
    }

    private static ModuleIntegrationPlanItem CompositionCatalogItem(
        FullNetCrudSchema schema,
        ModuleIntegrationTarget target,
        ModuleIntegrationSnapshot snapshot,
        bool moduleProjectExists)
    {
        if (!moduleProjectExists)
        {
            return new ModuleIntegrationPlanItem(
                ModuleIntegrationArea.CompositionCatalog,
                ModuleIntegrationStatus.Blocked,
                target.CompositionCatalogPath,
                "目标模块项目不存在，不能确认 Composition 模块目录接入。");
        }

        if (!snapshot.TryGetContent(
                target.CompositionCatalogPath,
                out var catalog))
        {
            return new ModuleIntegrationPlanItem(
                ModuleIntegrationArea.CompositionCatalog,
                ModuleIntegrationStatus.Blocked,
                target.CompositionCatalogPath,
                "Composition 模块目录不存在，无法确认模块注册。");
        }

        var hasNamespace = catalog.Contains(
            $"using {schema.RootNamespace};",
            StringComparison.Ordinal);
        var hasModule = catalog.Contains(
            $"new {target.ModuleName}Module()",
            StringComparison.Ordinal);
        return new ModuleIntegrationPlanItem(
            ModuleIntegrationArea.CompositionCatalog,
            hasNamespace && hasModule
                ? ModuleIntegrationStatus.Satisfied
                : ModuleIntegrationStatus.ChangeRequired,
            target.CompositionCatalogPath,
            hasNamespace && hasModule
                ? "Composition 模块目录已包含精确命名空间和模块构造。"
                : $"Composition 模块目录需要显式注册 {target.ModuleName}Module。");
    }

    private static ModuleIntegrationPlanItem RouteItem(
        ModuleIntegrationArea area,
        ModuleIntegrationTarget target,
        ModuleIntegrationSnapshot snapshot,
        bool vue)
    {
        if (!vue && target.LayuiRouterPath is null)
        {
            return new ModuleIntegrationPlanItem(
                area,
                ModuleIntegrationStatus.Satisfied,
                target.VueRouterPath,
                "未声明 Layui 路由；仅规划 Vue 接入。");
        }

        var routerPath = vue
            ? target.VueRouterPath
            : target.LayuiRouterPath!;
        var route = target.ClientRoute;
        if (route is null)
        {
            return snapshot.TryGetContent(routerPath, out _)
                ? new ModuleIntegrationPlanItem(
                    area,
                    ModuleIntegrationStatus.ManualReview,
                    routerPath,
                    "目标未声明 clientRoute；复核页面、权限、菜单、翻译和动态导航后手工接入。")
                : new ModuleIntegrationPlanItem(
                    area,
                    ModuleIntegrationStatus.Blocked,
                    routerPath,
                    "显式指定的客户端路由文件不存在。");
        }

        if (!snapshot.TryGetContent(routerPath, out var router))
        {
            return new ModuleIntegrationPlanItem(
                area,
                ModuleIntegrationStatus.Blocked,
                routerPath,
                "显式指定的客户端路由文件不存在。");
        }

        var adapterPath = vue
            ? route.VueComponentPath
            : route.LayuiControllerPath;
        if (!snapshot.TryGetContent(adapterPath!, out var adapter))
        {
            return new ModuleIntegrationPlanItem(
                area,
                ModuleIntegrationStatus.Blocked,
                routerPath,
                $"显式客户端适配文件不存在：{adapterPath}。");
        }

        if (string.IsNullOrWhiteSpace(adapter)
            || (!vue && !ContainsLayuiExport(
                adapter,
                route.LayuiControllerExport!)))
        {
            return new ModuleIntegrationPlanItem(
                area,
                ModuleIntegrationStatus.Blocked,
                routerPath,
                $"显式客户端适配文件不包含可验证入口：{adapterPath}。");
        }

        var edit = vue
            ? VueRouteIntegrationEditor.Edit(
                router,
                routerPath,
                route)
            : LayuiRouteIntegrationEditor.Edit(
                router,
                routerPath,
                route);
        return new ModuleIntegrationPlanItem(
            area,
            !edit.Succeeded
                ? ModuleIntegrationStatus.Blocked
                : edit.Changed
                    ? ModuleIntegrationStatus.ChangeRequired
                    : ModuleIntegrationStatus.Satisfied,
            routerPath,
            !edit.Succeeded
                ? edit.Diagnostics[0]
                : edit.Changed
                    ? $"可按显式 clientRoute 接入本地适配文件：{adapterPath}。"
                    : "客户端本地路由已包含精确显式映射。");
    }

    private static bool ContainsLayuiExport(
        string source,
        string exportName)
    {
        if (source.Contains('`'))
        {
            return false;
        }

        var lines = ClientRouteText.SplitLines(source);
        var sanitized = ClientRouteText.RemoveComments(lines);
        return sanitized.Any(line =>
            line.TrimStart().StartsWith(
                $"export function {exportName}(",
                StringComparison.Ordinal));
    }

    private static bool? HasProjectReference(
        string project,
        string compositionProjectPath,
        string moduleProjectPath)
    {
        try
        {
            var document = XDocument.Parse(
                project,
                LoadOptions.None);
            return document
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == "ProjectReference")
                .Select(element =>
                    element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => ResolveReference(
                    compositionProjectPath,
                    value!))
                .Any(path => StringComparer.OrdinalIgnoreCase.Equals(
                    path,
                    moduleProjectPath));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static bool ContainsExactInvocation(
        string content,
        string extensionName)
    {
        // 多行字符串可能包含看似调用的示例；无法无损解析时保持保守的待修改状态。
        if (content.Contains("\"\"\"", StringComparison.Ordinal)
            || content.Contains("@\"", StringComparison.Ordinal))
        {
            return false;
        }

        var inBlockComment = false;
        foreach (var sourceLine in content.Split('\n'))
        {
            var line = RemoveComments(sourceLine, ref inBlockComment).Trim();
            var dot = line.IndexOf('.');
            if (dot <= 0
                || !line.EndsWith(
                    $".{extensionName}();",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var receiver = line[..dot];
            if ((char.IsLetter(receiver[0]) || receiver[0] == '_')
                && receiver.Skip(1).All(character =>
                    char.IsLetterOrDigit(character)
                    || character == '_'))
            {
                return true;
            }
        }

        return false;
    }

    private static string RemoveComments(
        string line,
        ref bool inBlockComment)
    {
        var remaining = line;
        var result = string.Empty;
        while (remaining.Length > 0)
        {
            if (inBlockComment)
            {
                var blockEnd = remaining.IndexOf(
                    "*/",
                    StringComparison.Ordinal);
                if (blockEnd < 0)
                {
                    return result;
                }

                remaining = remaining[(blockEnd + 2)..];
                inBlockComment = false;
                continue;
            }

            var lineComment = remaining.IndexOf(
                "//",
                StringComparison.Ordinal);
            var blockStart = remaining.IndexOf(
                "/*",
                StringComparison.Ordinal);
            if (lineComment >= 0
                && (blockStart < 0 || lineComment < blockStart))
            {
                return result + remaining[..lineComment];
            }

            if (blockStart < 0)
            {
                return result + remaining;
            }

            result += remaining[..blockStart];
            remaining = remaining[(blockStart + 2)..];
            inBlockComment = true;
        }

        return result;
    }

    private static string ResolveReference(
        string projectPath,
        string reference)
    {
        var segments = RepositoryDirectory(projectPath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        foreach (var segment in reference
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return string.Empty;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static string RepositoryDirectory(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }
}
