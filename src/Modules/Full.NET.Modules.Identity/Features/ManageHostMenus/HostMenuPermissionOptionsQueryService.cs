using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.ManageHostMenus;

/// <summary>从服务端授权目录投影 Host 菜单可引用的权限选项。</summary>
internal sealed class HostMenuPermissionOptionsQueryService(
    AuthorizationCatalog authorizationCatalog)
{
    public IReadOnlyList<HostMenuPermissionOptionResponse> List()
    {
        var modulesByKey = authorizationCatalog.Modules.ToDictionary(
            module => module.Key,
            StringComparer.Ordinal);
        var navigationById = authorizationCatalog.Navigation.ToDictionary(
            navigation => navigation.Id,
            StringComparer.Ordinal);
        var permissionScopeByCode = authorizationCatalog.Permissions.ToDictionary(
            permission => permission.Code,
            permission => permission.Scope,
            StringComparer.Ordinal);
        var options = new List<HostMenuPermissionOptionResponse>();

        foreach (var navigation in authorizationCatalog.Navigation)
        {
            if (!IsAssignablePermission(
                    permissionScopeByCode,
                    navigation.RequiredPermission))
            {
                continue;
            }

            var moduleKey = authorizationCatalog.NavigationModuleKeys[navigation.Id];
            options.Add(new HostMenuPermissionOptionResponse(
                navigation.RequiredPermission,
                moduleKey,
                modulesByKey[moduleKey].Title,
                navigation.Id,
                navigation.Title,
                Kind: "page",
                DisplayName: navigation.Title,
                DisplayNameKey: $"authorization.pages.{navigation.Id}"));
        }

        foreach (var action in authorizationCatalog.Actions)
        {
            if (!IsAssignablePermission(
                    permissionScopeByCode,
                    action.PermissionCode))
            {
                continue;
            }

            var navigation = navigationById[action.NavigationId];
            var moduleKey = authorizationCatalog.NavigationModuleKeys[navigation.Id];
            options.Add(new HostMenuPermissionOptionResponse(
                action.PermissionCode,
                moduleKey,
                modulesByKey[moduleKey].Title,
                navigation.Id,
                navigation.Title,
                Kind: "action",
                DisplayName: action.Name,
                DisplayNameKey: $"authorization.actions.{action.Id}",
                ActionId: action.Id,
                ActionKey: action.ClientActionKey));
        }

        return options;
    }

    private static bool IsAssignablePermission(
        IReadOnlyDictionary<string, AuthorizationScope> permissionScopeByCode,
        string permissionCode)
    {
        if (!permissionScopeByCode.TryGetValue(permissionCode, out var scope))
        {
            return false;
        }

        return scope.HasFlag(AuthorizationScope.Host)
            && !permissionCode.StartsWith(
                "identity.super_administrators.",
                StringComparison.Ordinal);
    }
}
