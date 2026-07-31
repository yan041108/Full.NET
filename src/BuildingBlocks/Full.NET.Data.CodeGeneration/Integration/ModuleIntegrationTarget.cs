using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 保存双管理端路由接入所需的本地可信映射，禁止从服务端数据推断可执行组件。
/// </summary>
public sealed record ModuleClientRouteTarget
{
    private ModuleClientRouteTarget(
        string routePath,
        string vueRouteName,
        string vueComponentPath,
        string layuiControllerPath,
        string layuiControllerExport)
    {
        RoutePath = routePath;
        VueRouteName = vueRouteName;
        VueComponentPath = vueComponentPath;
        LayuiControllerPath = layuiControllerPath;
        LayuiControllerExport = layuiControllerExport;
    }

    public string RoutePath { get; }

    public string VueRouteName { get; }

    public string VueComponentPath { get; }

    public string LayuiControllerPath { get; }

    public string LayuiControllerExport { get; }

    /// <summary>
    /// 创建经过稳定机器码和仓库相对路径校验的双端路由映射。
    /// </summary>
    public static ModuleClientRouteTarget Create(
        string routePath,
        string vueRouteName,
        string vueComponentPath,
        string layuiControllerPath,
        string layuiControllerExport)
    {
        if (!IsRoutePath(routePath))
        {
            throw new ArgumentException(
                "客户端路由必须由小写 kebab-case 路径段组成。",
                nameof(routePath));
        }

        if (!IsKebabCode(vueRouteName))
        {
            throw new ArgumentException(
                "Vue 路由名称必须是稳定小写 kebab-case 机器码。",
                nameof(vueRouteName));
        }

        if (!IsControllerExport(layuiControllerExport))
        {
            throw new ArgumentException(
                "Layui controller export 必须使用 create{Name}Controller。",
                nameof(layuiControllerExport));
        }

        var vuePath = GenerationArtifactPath.Validate(
            vueComponentPath,
            nameof(vueComponentPath));
        var layuiPath = GenerationArtifactPath.Validate(
            layuiControllerPath,
            nameof(layuiControllerPath));
        RequireSuffix(vuePath, ".vue", nameof(vueComponentPath));
        RequireSuffix(layuiPath, ".js", nameof(layuiControllerPath));
        if (StringComparer.OrdinalIgnoreCase.Equals(
                vuePath,
                layuiPath))
        {
            throw new ArgumentException(
                "Vue View 与 Layui controller 路径不得重复。");
        }

        return new ModuleClientRouteTarget(
            routePath,
            vueRouteName,
            vuePath,
            layuiPath,
            layuiControllerExport);
    }

    private static bool IsRoutePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value == "/"
            || !value.StartsWith("/", StringComparison.Ordinal)
            || value.EndsWith("/", StringComparison.Ordinal)
            || value.Contains("//", StringComparison.Ordinal)
            || value.Contains('?')
            || value.Contains('#'))
        {
            return false;
        }

        return value[1..]
            .Split('/', StringSplitOptions.None)
            .All(IsKebabCode);
    }

    private static bool IsKebabCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return IsAsciiLower(value[0])
            && value[^1] != '-'
            && !value.Contains("--", StringComparison.Ordinal)
            && value.Skip(1).All(character =>
                IsAsciiLower(character)
                || character is >= '0' and <= '9'
                || character == '-');
    }

    private static bool IsControllerExport(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith("create", StringComparison.Ordinal)
        && value.EndsWith("Controller", StringComparison.Ordinal)
        && value.Length > "createController".Length
        && value["create".Length] is >= 'A' and <= 'Z'
        && value.All(character =>
            character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9');

    private static bool IsAsciiLower(char value) =>
        value is >= 'a' and <= 'z';

    private static void RequireSuffix(
        string path,
        string suffix,
        string parameterName)
    {
        if (!path.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"客户端接入目标必须使用 {suffix} 文件。",
                parameterName);
        }
    }
}

/// <summary>
/// 保存模块接入规划必须显式确认的仓库相对路径，禁止规划器猜测项目拓扑。
/// </summary>
public sealed record ModuleIntegrationTarget
{
    private ModuleIntegrationTarget(
        string moduleName,
        string moduleProjectPath,
        string moduleEntryPointPath,
        string compositionProjectPath,
        string compositionCatalogPath,
        string vueRouterPath,
        string layuiRouterPath,
        ModuleClientRouteTarget? clientRoute)
    {
        ModuleName = moduleName;
        ModuleProjectPath = moduleProjectPath;
        ModuleEntryPointPath = moduleEntryPointPath;
        CompositionProjectPath = compositionProjectPath;
        CompositionCatalogPath = compositionCatalogPath;
        VueRouterPath = vueRouterPath;
        LayuiRouterPath = layuiRouterPath;
        ClientRoute = clientRoute;
    }

    public string ModuleName { get; }

    public string ModuleProjectPath { get; }

    public string ModuleEntryPointPath { get; }

    public string CompositionProjectPath { get; }

    public string CompositionCatalogPath { get; }

    public string VueRouterPath { get; }

    public string LayuiRouterPath { get; }

    public ModuleClientRouteTarget? ClientRoute { get; }

    /// <summary>
    /// 创建经过可移植路径校验的显式接入目标。
    /// </summary>
    public static ModuleIntegrationTarget Create(
        string moduleName,
        string moduleProjectPath,
        string moduleEntryPointPath,
        string compositionProjectPath,
        string compositionCatalogPath,
        string vueRouterPath,
        string layuiRouterPath,
        ModuleClientRouteTarget? clientRoute = null)
    {
        if (string.IsNullOrWhiteSpace(moduleName)
            || !IsIdentifier(moduleName))
        {
            throw new ArgumentException(
                "模块名称必须是有效的 C# 标识符。",
                nameof(moduleName));
        }

        var paths = new[]
        {
            GenerationArtifactPath.Validate(
                moduleProjectPath,
                nameof(moduleProjectPath)),
            GenerationArtifactPath.Validate(
                moduleEntryPointPath,
                nameof(moduleEntryPointPath)),
            GenerationArtifactPath.Validate(
                compositionProjectPath,
                nameof(compositionProjectPath)),
            GenerationArtifactPath.Validate(
                compositionCatalogPath,
                nameof(compositionCatalogPath)),
            GenerationArtifactPath.Validate(
                vueRouterPath,
                nameof(vueRouterPath)),
            GenerationArtifactPath.Validate(
                layuiRouterPath,
                nameof(layuiRouterPath)),
        };
        var allPaths = clientRoute is null
            ? paths
            : paths
                .Concat(
                [
                    clientRoute.VueComponentPath,
                    clientRoute.LayuiControllerPath,
                ])
                .ToArray();
        if (allPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != allPaths.Length)
        {
            throw new ArgumentException(
                "模块接入目标路径不得重复或形成不可移植的大小写别名。");
        }

        RequireSuffix(paths[0], ".csproj", nameof(moduleProjectPath));
        RequireSuffix(paths[1], ".cs", nameof(moduleEntryPointPath));
        RequireSuffix(paths[2], ".csproj", nameof(compositionProjectPath));
        RequireSuffix(paths[3], ".cs", nameof(compositionCatalogPath));
        RequireSuffix(paths[4], ".ts", nameof(vueRouterPath));
        RequireSuffix(paths[5], ".js", nameof(layuiRouterPath));

        return new ModuleIntegrationTarget(
            moduleName,
            paths[0],
            paths[1],
            paths[2],
            paths[3],
            paths[4],
            paths[5],
            clientRoute);
    }

    private static bool IsIdentifier(string value) =>
        (char.IsLetter(value[0]) || value[0] == '_')
        && value.Skip(1).All(character =>
            char.IsLetterOrDigit(character) || character == '_');

    private static void RequireSuffix(
        string path,
        string suffix,
        string parameterName)
    {
        if (!path.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"模块接入目标必须使用 {suffix} 文件。",
                parameterName);
        }
    }
}
