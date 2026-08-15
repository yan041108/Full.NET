using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Authorization;

/// <summary>
/// 权限码目录与导航定义的不可变注册表。聚合全部 IAuthorizationCatalogContributor，
/// 在启动时完成父子约束、去重、无环、作用域包含与页面/动作权限绑定等一致性校验。
/// 不允许在运行时动态追加权限——新增权限需通过模块 Contributor 重新发布。
/// </summary>
internal sealed class AuthorizationCatalog
{
    private AuthorizationCatalog(
        IReadOnlyList<AuthorizationModuleDefinition> modules,
        IReadOnlyDictionary<string, string> navigationModuleKeys,
        IReadOnlyList<PermissionDefinition> permissions,
        IReadOnlyList<NavigationDefinition> navigation,
        IReadOnlyList<AuthorizationActionDefinition> actions)
    {
        Modules = modules;
        NavigationModuleKeys = navigationModuleKeys;
        Permissions = permissions;
        Navigation = navigation;
        Actions = actions;
    }

    /// <summary>注册的模块列表，按 Order 与 Key 排序。</summary>
    public IReadOnlyList<AuthorizationModuleDefinition> Modules { get; }

    /// <summary>Navigation Id → 所属 Module Key 的映射，用于授权树投影归属。</summary>
    public IReadOnlyDictionary<string, string> NavigationModuleKeys { get; }

    /// <summary>全局权限定义，按 Code 字典序排序，代码为唯一主键。</summary>
    public IReadOnlyList<PermissionDefinition> Permissions { get; }

    /// <summary>导航菜单定义（含父子引用），按 Order/Id 排序。</summary>
    public IReadOnlyList<NavigationDefinition> Navigation { get; }

    /// <summary>页面动作（按钮级）定义，挂载于特定 Navigation 并绑定独立权限。</summary>
    public IReadOnlyList<AuthorizationActionDefinition> Actions { get; }

    /// <summary>
    /// 从所有已注册的 IAuthorizationCatalogContributor 聚合并构造不可变目录。
    /// 构造时即执行全部一致性校验：权限/导航/动作无重复、导航父子关系无环且不孤立、
    /// 动作权限作用域必须被父页面权限作用域包含，不允许动作复用页面读权限。
    /// </summary>
    /// <param name="contributors">来自各业务模块的权限目录贡献者集合。</param>
    /// <exception cref="InvalidOperationException">当目录违反任何一致性约束时抛出。</exception>
    public static AuthorizationCatalog Create(
        IEnumerable<IAuthorizationCatalogContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        var materialized = contributors.ToArray();
        var modules = materialized
            .Select(contributor => contributor.Module)
            .OrderBy(module => module.Order)
            .ThenBy(module => module.Key, StringComparer.Ordinal)
            .ToArray();
        var navigationModuleKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var contributor in materialized)
        {
            foreach (var navigationItem in contributor.Navigation)
            {
                if (!navigationModuleKeys.TryAdd(navigationItem.Id, contributor.Module.Key))
                {
                    throw new InvalidOperationException(
                        $"Authorization catalog contains duplicate navigation id '{navigationItem.Id}'.");
                }
            }
        }
        var permissions = materialized
            .SelectMany(contributor => contributor.Permissions)
            .ToArray();
        var navigation = materialized
            .SelectMany(contributor => contributor.Navigation)
            .ToArray();
        var actions = materialized
            .SelectMany(contributor => contributor.Actions)
            .ToArray();

        ValidateModules(modules);
        ValidatePermissions(permissions);
        ValidateNavigation(permissions, navigation);
        ValidateActions(permissions, navigation, actions);

        var orderedNavigation = navigation
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var navigationOrder = orderedNavigation
            .Select((item, index) => (item.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);

        return new AuthorizationCatalog(
            modules,
            navigationModuleKeys,
            permissions.OrderBy(item => item.Code, StringComparer.Ordinal).ToArray(),
            orderedNavigation,
            actions
                .OrderBy(item => navigationOrder[item.NavigationId])
                .ThenBy(item => item.Order)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray());
    }

    private static void ValidateModules(
        IReadOnlyCollection<AuthorizationModuleDefinition> modules)
    {
        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module.Key)
                || string.IsNullOrWhiteSpace(module.Title))
            {
                throw new InvalidOperationException(
                    "Authorization catalog contains an incomplete module definition.");
            }
        }

        EnsureUnique(
            modules.Select(item => item.Key),
            "module key");
    }

    private static void ValidatePermissions(
        IReadOnlyCollection<PermissionDefinition> permissions)
    {
        foreach (var permission in permissions)
        {
            if (string.IsNullOrWhiteSpace(permission.Code)
                || string.IsNullOrWhiteSpace(permission.Name)
                || permission.Scope == 0)
            {
                throw new InvalidOperationException(
                    "Authorization catalog contains an incomplete permission definition.");
            }
        }

        EnsureUnique(
            permissions.Select(item => item.Code),
            "permission code");
    }

    private static void ValidateNavigation(
        IReadOnlyCollection<PermissionDefinition> permissions,
        IReadOnlyCollection<NavigationDefinition> navigation)
    {
        EnsureUnique(
            navigation.Select(item => item.Id),
            "navigation id");
        var permissionsByCode = permissions.ToDictionary(
            item => item.Code,
            item => item,
            StringComparer.Ordinal);
        var navigationIds = navigation
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var item in navigation)
        {
            if (string.IsNullOrWhiteSpace(item.Id)
                || string.IsNullOrWhiteSpace(item.RouteName)
                || string.IsNullOrWhiteSpace(item.Path)
                || string.IsNullOrWhiteSpace(item.ComponentKey)
                || string.IsNullOrWhiteSpace(item.Title)
                || string.IsNullOrWhiteSpace(item.RequiredPermission))
            {
                throw new InvalidOperationException(
                    "Authorization catalog contains an incomplete navigation definition.");
            }

            if (!permissionsByCode.ContainsKey(item.RequiredPermission))
            {
                throw new InvalidOperationException(
                    $"Navigation '{item.Id}' requires an unknown permission.");
            }

            if (item.ParentId is not null && !navigationIds.Contains(item.ParentId))
            {
                throw new InvalidOperationException(
                    $"Navigation '{item.Id}' references an unknown parent.");
            }
        }

        EnsureAcyclic(navigation);
    }

    private static void ValidateActions(
        IReadOnlyCollection<PermissionDefinition> permissions,
        IReadOnlyCollection<NavigationDefinition> navigation,
        IReadOnlyCollection<AuthorizationActionDefinition> actions)
    {
        EnsureUnique(
            actions.Select(item => item.Id),
            "action id");

        var permissionsByCode = permissions.ToDictionary(
            item => item.Code,
            item => item,
            StringComparer.Ordinal);
        var navigationById = navigation.ToDictionary(
            item => item.Id,
            item => item,
            StringComparer.Ordinal);
        var clientActionKeys = new HashSet<string>(StringComparer.Ordinal);
        var actionPermissionCodes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id)
                || string.IsNullOrWhiteSpace(action.NavigationId)
                || string.IsNullOrWhiteSpace(action.PermissionCode)
                || string.IsNullOrWhiteSpace(action.Name)
                || string.IsNullOrWhiteSpace(action.ClientActionKey))
            {
                throw new InvalidOperationException(
                    "Authorization catalog contains an incomplete action definition.");
            }

            if (!navigationById.TryGetValue(action.NavigationId, out var navigationItem))
            {
                throw new InvalidOperationException(
                    $"Action '{action.Id}' references an unknown navigation.");
            }

            if (!permissionsByCode.TryGetValue(action.PermissionCode, out var actionPermission))
            {
                throw new InvalidOperationException(
                    $"Action '{action.Id}' requires an unknown permission.");
            }

            var pagePermission = permissionsByCode[navigationItem.RequiredPermission];
            if ((actionPermission.Scope & pagePermission.Scope) != actionPermission.Scope)
            {
                throw new InvalidOperationException(
                    $"Action '{action.Id}' exceeds the parent page authorization scope.");
            }

            if (string.Equals(
                action.PermissionCode,
                navigationItem.RequiredPermission,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Action '{action.Id}' cannot use the page read permission.");
            }

            var clientActionKey = $"{action.NavigationId}:{action.ClientActionKey}";
            if (!clientActionKeys.Add(clientActionKey))
            {
                throw new InvalidOperationException(
                    $"Authorization catalog contains duplicate action client key '{clientActionKey}'.");
            }

            if (actionPermissionCodes.TryGetValue(action.PermissionCode, out var existingActionId)
                && !string.Equals(existingActionId, action.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Permission '{action.PermissionCode}' is already bound to action '{existingActionId}'.");
            }

            actionPermissionCodes[action.PermissionCode] = action.Id;
        }
    }

    private static void EnsureAcyclic(
        IReadOnlyCollection<NavigationDefinition> navigation)
    {
        var parents = navigation.ToDictionary(
            item => item.Id,
            item => item.ParentId,
            StringComparer.Ordinal);

        foreach (var item in navigation)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? current = item.Id;
            while (current is not null)
            {
                if (!visited.Add(current))
                {
                    throw new InvalidOperationException(
                        $"Navigation '{item.Id}' participates in a parent cycle.");
                }

                current = parents[current];
            }
        }
    }

    private static void EnsureUnique(
        IEnumerable<string> values,
        string definitionName)
    {
        var duplicate = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Authorization catalog contains duplicate {definitionName} '{duplicate.Key}'.");
        }
    }
}
