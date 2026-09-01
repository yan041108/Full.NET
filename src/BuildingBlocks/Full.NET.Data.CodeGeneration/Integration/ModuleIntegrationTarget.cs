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
        string? layuiControllerPath,
        string? layuiControllerExport)
    {
        RoutePath = routePath;
        VueRouteName = vueRouteName;
        VueComponentPath = vueComponentPath;
        LayuiControllerPath = layuiControllerPath;
        LayuiControllerExport = layuiControllerExport;
    }

    /// <summary>
    /// 管理端浏览器访问该资源使用的 URL 路径段；必须以 "/" 开头，使用小写 kebab-case，禁止查询参数与哈希。
    /// 确定性：路径格式由 Create 严格校验；非法输入 FAIL-closed 抛 ArgumentException。
    /// </summary>
    public string RoutePath { get; }

    /// <summary>
    /// Vue Router 使用的稳定路由名称；必须是小写 kebab-case 机器码，用于 keep-alive 与路由导航匹配。
    /// </summary>
    public string VueRouteName { get; }

    /// <summary>
    /// 生成的 Vue SFC 页面在仓库中的相对路径；必须以 ".vue" 结尾，且已通过 GenerationArtifactPath 可移植性校验。
    /// </summary>
    public string VueComponentPath { get; }

    /// <summary>
    /// Layui 管理端使用的 controller 文件相对路径；Layui 未启用时为空。存在时必须以 ".js" 结尾且不得与 Vue 路径重复。
    /// </summary>
    public string? LayuiControllerPath { get; }

    /// <summary>
    /// Layui controller 导出的工厂函数名称；必须匹配 "create{Name}Controller" 模式。
    /// 与 LayuiControllerPath 必须同时提供或同时省略。
    /// </summary>
    public string? LayuiControllerExport { get; }

    /// <summary>
    /// 创建经过稳定机器码和仓库相对路径校验的路由映射；Layui 两端必须同时缺省或同时提供。
    /// </summary>
    public static ModuleClientRouteTarget Create(
        string routePath,
        string vueRouteName,
        string vueComponentPath,
        string? layuiControllerPath = null,
        string? layuiControllerExport = null)
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

        var hasLayuiPath = !string.IsNullOrWhiteSpace(layuiControllerPath);
        var hasLayuiExport = !string.IsNullOrWhiteSpace(layuiControllerExport);
        if (hasLayuiPath != hasLayuiExport)
        {
            throw new ArgumentException(
                "Layui controller 路径与 export 必须同时提供或同时省略。");
        }

        if (hasLayuiExport && !IsControllerExport(layuiControllerExport!))
        {
            throw new ArgumentException(
                "Layui controller export 必须使用 create{Name}Controller。",
                nameof(layuiControllerExport));
        }

        var vuePath = GenerationArtifactPath.Validate(
            vueComponentPath,
            nameof(vueComponentPath));
        RequireSuffix(vuePath, ".vue", nameof(vueComponentPath));
        string? layuiPath = null;
        if (hasLayuiPath)
        {
            layuiPath = GenerationArtifactPath.Validate(
                layuiControllerPath!,
                nameof(layuiControllerPath));
            RequireSuffix(layuiPath, ".js", nameof(layuiControllerPath));
            if (StringComparer.OrdinalIgnoreCase.Equals(
                    vuePath,
                    layuiPath))
            {
                throw new ArgumentException(
                    "Vue View 与 Layui controller 路径不得重复。");
            }
        }

        return new ModuleClientRouteTarget(
            routePath,
            vueRouteName,
            vuePath,
            layuiPath,
            hasLayuiExport ? layuiControllerExport : null);
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
        string? layuiRouterPath,
        ModuleClientRouteTarget? clientRoute,
        string? authorizationContributorPath)
    {
        ModuleName = moduleName;
        ModuleProjectPath = moduleProjectPath;
        ModuleEntryPointPath = moduleEntryPointPath;
        CompositionProjectPath = compositionProjectPath;
        CompositionCatalogPath = compositionCatalogPath;
        VueRouterPath = vueRouterPath;
        LayuiRouterPath = layuiRouterPath;
        ClientRoute = clientRoute;
        AuthorizationContributorPath = authorizationContributorPath;
    }

    /// <summary>
    /// 模块稳定名称；必须是有效的 C# 标识符，与 FullNetModule.Name 保持一致。
    /// 用于 CompositionCatalogEditor 生成 AddModule 调用时的类型查找。
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// 模块项目 .csproj 的仓库相对路径；必须以 ".csproj" 结尾且通过 GenerationArtifactPath 可移植性校验。
    /// 用于 CompositionProjectEditor 校验项目引用未漂移。
    /// </summary>
    public string ModuleProjectPath { get; }

    /// <summary>
    /// 模块入口点（如 XxxModule.cs）的仓库相对路径；必须以 ".cs" 结尾。
    /// 用于 ModuleEntryIntegrationEditor 在 AddServices/MapEndpoints 内插入片段。
    /// </summary>
    public string ModuleEntryPointPath { get; }

    /// <summary>
    /// Composition 宿主项目 .csproj 的仓库相对路径；必须以 ".csproj" 结尾。
    /// 用于校验 Composition 是否包含模块项目引用。
    /// </summary>
    public string CompositionProjectPath { get; }

    /// <summary>
    /// FullNetModuleCatalog.cs 的仓库相对路径；必须以 ".cs" 结尾。
    /// 用于 CompositionCatalogEditor 在 CreateAllModules 中追加模块实例化行。
    /// </summary>
    public string CompositionCatalogPath { get; }

    /// <summary>
    /// Vue 管理端路由注册文件相对路径；必须以 ".ts" 结尾。
    /// 用于 ClientRouteIntegrationEditors 在路由表内追加 { path, name, component } 片段。
    /// </summary>
    public string VueRouterPath { get; }

    /// <summary>
    /// Layui 管理端路由注册文件相对路径；Layui 未启用时为空。存在时必须以 ".js" 结尾。
    /// </summary>
    public string? LayuiRouterPath { get; }

    /// <summary>
    /// 双管理端页面路由目标；包含 URL 路径段、Vue route name、组件路径以及可选的 Layui controller 映射。
    /// 为空时规划器只接入后端与目录，不生成前端路由片段。
    /// </summary>
    public ModuleClientRouteTarget? ClientRoute { get; }

    /// <summary>可选的目标模块 AuthorizationContributor 路径；缺省时不插入菜单片段。</summary>
    public string? AuthorizationContributorPath { get; }

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
        string? layuiRouterPath,
        ModuleClientRouteTarget? clientRoute = null,
        string? authorizationContributorPath = null)
    {
        if (string.IsNullOrWhiteSpace(moduleName)
            || !IsIdentifier(moduleName))
        {
            throw new ArgumentException(
                "模块名称必须是有效的 C# 标识符。",
                nameof(moduleName));
        }

        var pathList = new List<string>
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
        };
        string? layuiPath = null;
        if (!string.IsNullOrWhiteSpace(layuiRouterPath))
        {
            layuiPath = GenerationArtifactPath.Validate(
                layuiRouterPath,
                nameof(layuiRouterPath));
            pathList.Add(layuiPath);
        }

        string? contributorPath = null;
        if (!string.IsNullOrWhiteSpace(authorizationContributorPath))
        {
            contributorPath = GenerationArtifactPath.Validate(
                authorizationContributorPath,
                nameof(authorizationContributorPath));
            pathList.Add(contributorPath);
        }

        if (clientRoute is not null)
        {
            pathList.Add(clientRoute.VueComponentPath);
            if (clientRoute.LayuiControllerPath is not null)
            {
                pathList.Add(clientRoute.LayuiControllerPath);
            }
        }

        if (pathList.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != pathList.Count)
        {
            throw new ArgumentException(
                "模块接入目标路径不得重复或形成不可移植的大小写别名。");
        }

        RequireSuffix(pathList[0], ".csproj", nameof(moduleProjectPath));
        RequireSuffix(pathList[1], ".cs", nameof(moduleEntryPointPath));
        RequireSuffix(pathList[2], ".csproj", nameof(compositionProjectPath));
        RequireSuffix(pathList[3], ".cs", nameof(compositionCatalogPath));
        RequireSuffix(pathList[4], ".ts", nameof(vueRouterPath));
        if (layuiPath is not null)
        {
            RequireSuffix(layuiPath, ".js", nameof(layuiRouterPath));
        }

        if (contributorPath is not null)
        {
            RequireSuffix(contributorPath, ".cs", nameof(authorizationContributorPath));
        }

        return new ModuleIntegrationTarget(
            moduleName,
            pathList[0],
            pathList[1],
            pathList[2],
            pathList[3],
            pathList[4],
            layuiPath,
            clientRoute,
            contributorPath);
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
