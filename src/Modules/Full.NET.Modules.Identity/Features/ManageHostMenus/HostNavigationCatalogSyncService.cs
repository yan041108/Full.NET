using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Contracts;

/// <summary>Identity 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Identity.Features.ManageHostMenus;

internal sealed class HostNavigationCatalogSyncService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    AuthorizationCatalog authorizationCatalog,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string HostScope = "host";

    // Platform 的本人任务和更新页面向租户，模块容器不要求 Host 更新日志管理权限。
    internal static readonly SqlStatement RepairPlatformDirectoryPermission = new("identity.navigation.repair_platform_directory_permission", """
        UPDATE fn_identity_navigation SET RequiredPermission=@RequiredPermission, UpdatedAtUtc=@Now, Version=Version+1
        WHERE ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND MenuType='directory'
            AND RouteName='module-platform' AND Path='/modules/platform' AND RequiredPermission LIKE 'platform.%'
        """, SqlDataScope.HostOnly);

    // 配置页以读取资格进入，发布与预览仍是独立动作；只修框架原始系统节点，不改自定义菜单。
    internal static readonly SqlStatement RepairPrintingTemplatePermission = new("identity.navigation.repair_printing_template_permission", """
        UPDATE fn_identity_navigation SET RequiredPermission=@RequiredPermission,UpdatedAtUtc=@Now,Version=Version+1
        WHERE ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND RequiredPermission='printing.templates.preview'
            AND ((MenuType='directory' AND RouteName='module-printing' AND Path='/modules/printing')
                OR (MenuType='menu' AND RouteName='printing-preview' AND Path='/printing/preview' AND ComponentKey='printing-preview'))
        """, SqlDataScope.HostOnly);

    // 导航资格是独立的基础权限，移除早期误挂到工作台的系统按钮，保留权限和用户自定义项。
    internal static readonly SqlStatement RemoveObsoleteNavigationAction = new("identity.navigation.remove_obsolete_navigation_action", """
        DELETE FROM fn_identity_navigation WHERE ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND MenuType='button'
            AND RouteName='identity-navigation-read' AND ComponentKey='read-navigation' AND RequiredPermission=@RequiredPermission
        """, SqlDataScope.HostOnly);

    // 领域分组只要求导航资格；只修复本框架原有的系统目录，不改自定义目录或页面权限。
    internal static readonly SqlStatement RepairDomainDirectoryPermission = new("identity.navigation.repair_domain_directory_permission", """
        UPDATE fn_identity_navigation SET RequiredPermission=@RequiredPermission, UpdatedAtUtc=@Now, Version=Version+1
        WHERE ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND MenuType='directory'
            AND RequiredPermission=@PreviousPermission AND (
                (RouteName='domain-overview-platform' AND Path='/domains/overview-platform') OR
                (RouteName='domain-tenancy-commerce' AND Path='/domains/tenancy-commerce') OR
                (RouteName='domain-collaboration' AND Path='/domains/collaboration') OR
                (RouteName='domain-data-intelligence' AND Path='/domains/data-intelligence') OR
                (RouteName='domain-integration-dev' AND Path='/domains/integration-dev') OR
                (RouteName='domain-operations-security' AND Path='/domains/operations-security') OR
                (RouteName='domain-other' AND Path='/domains/other'))
        """, SqlDataScope.HostOnly);

    // 租户导出与 Host 定义管理共用目录，容器本身不能要求 Host 数据源权限。
    internal static readonly SqlStatement RepairReportingDirectoryPermission = new("identity.navigation.repair_reporting_directory_permission", """
        UPDATE fn_identity_navigation SET RequiredPermission=@RequiredPermission, UpdatedAtUtc=@Now, Version=Version+1
        WHERE ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND MenuType='directory'
            AND RouteName='module-reporting' AND Path='/modules/reporting' AND RequiredPermission LIKE 'reporting.%'
        """, SqlDataScope.HostOnly);

    // 打印分组同时包含 Host 配置与租户获授版本，不能让租户入口依赖 Host 草稿权限。
    internal static readonly SqlStatement RepairPrintingDirectoryPermission = new("identity.navigation.repair_printing_directory_permission", """
        UPDATE fn_identity_navigation SET RequiredPermission=@RequiredPermission, UpdatedAtUtc=@Now, Version=Version+1
        WHERE ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND MenuType='directory'
            AND RouteName='module-printing' AND Path='/modules/printing' AND RequiredPermission LIKE 'printing.%'
        """, SqlDataScope.HostOnly);

    // 工作流目录只承载分组，不能让本人待办隐式依赖定义管理或协作授权管理权限；叶子仍逐项精确授权。
    internal static readonly SqlStatement RepairWorkflowDirectoryPermission = new("identity.navigation.repair_workflow_directory_permission", """
        UPDATE fn_identity_navigation SET RequiredPermission=@RequiredPermission, UpdatedAtUtc=@Now, Version=Version+1
        WHERE ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND MenuType='directory'
            AND RouteName='module-workflow' AND Path='/modules/workflow' AND RequiredPermission LIKE 'workflow.%'
        """, SqlDataScope.HostOnly);

    // AI 审核者不需要模型配置读取权限；分组只要求导航资格，具体页面仍各自授权。
    internal static readonly SqlStatement RepairAiDirectoryPermission = new("identity.navigation.repair_ai_directory_permission", """
        UPDATE fn_identity_navigation SET RequiredPermission=@RequiredPermission, UpdatedAtUtc=@Now, Version=Version+1
        WHERE ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND MenuType='directory'
            AND RouteName='module-ai' AND Path='/modules/ai' AND RequiredPermission LIKE 'ai.%'
        """, SqlDataScope.HostOnly);

    // 站内信支持租户会话，容器不能继承 Host 公告管理权限并隐藏已授权收件箱。
    internal static readonly SqlStatement RepairNotificationsDirectoryPermission = new("identity.navigation.repair_notifications_directory_permission", """
        UPDATE fn_identity_navigation SET RequiredPermission=@RequiredPermission, UpdatedAtUtc=@Now, Version=Version+1
        WHERE ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND MenuType='directory'
            AND RouteName='module-notifications' AND Path='/modules/notifications' AND RequiredPermission LIKE 'notifications.%'
        """, SqlDataScope.HostOnly);

    public async Task<(int Created, int Skipped, int Reparented, int PermissionUpdated)> SyncMissingCatalogEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        var routeNameIndex = await LoadRouteNameIndexAsync(cancellationToken)
            .ConfigureAwait(false);
        var moduleCreated = await SyncModuleDirectoriesAsync(
                routeNameIndex,
                cancellationToken)
            .ConfigureAwait(false);
        var permissionUpdated = await commandExecutor.ExecuteAsync(RemoveObsoleteNavigationAction,
            new Dictionary<string, object?> { ["RequiredPermission"] = IdentityAuthorizationContributor.NavigationRead }, cancellationToken).ConfigureAwait(false);
        permissionUpdated += await commandExecutor.ExecuteAsync(RepairDomainDirectoryPermission,
            new Dictionary<string, object?> { ["RequiredPermission"] = IdentityAuthorizationContributor.NavigationRead,
                ["PreviousPermission"] = IdentityAuthorizationContributor.DashboardRead, ["Now"] = clock.UtcNow }, cancellationToken).ConfigureAwait(false);
        permissionUpdated += await commandExecutor.ExecuteAsync(RepairWorkflowDirectoryPermission,
            new Dictionary<string, object?> { ["RequiredPermission"] = IdentityAuthorizationContributor.NavigationRead, ["Now"] = clock.UtcNow }, cancellationToken).ConfigureAwait(false);
        permissionUpdated += await commandExecutor.ExecuteAsync(RepairAiDirectoryPermission,
            new Dictionary<string, object?> { ["RequiredPermission"] = IdentityAuthorizationContributor.NavigationRead, ["Now"] = clock.UtcNow }, cancellationToken).ConfigureAwait(false);
        permissionUpdated += await commandExecutor.ExecuteAsync(RepairNotificationsDirectoryPermission,
            new Dictionary<string, object?> { ["RequiredPermission"] = IdentityAuthorizationContributor.NavigationRead, ["Now"] = clock.UtcNow }, cancellationToken).ConfigureAwait(false);
        permissionUpdated += await commandExecutor.ExecuteAsync(RepairReportingDirectoryPermission,
            new Dictionary<string, object?> { ["RequiredPermission"] = IdentityAuthorizationContributor.NavigationRead, ["Now"] = clock.UtcNow }, cancellationToken).ConfigureAwait(false);
        permissionUpdated += await commandExecutor.ExecuteAsync(RepairPrintingDirectoryPermission,
            new Dictionary<string, object?> { ["RequiredPermission"] = IdentityAuthorizationContributor.NavigationRead, ["Now"] = clock.UtcNow }, cancellationToken).ConfigureAwait(false);
        permissionUpdated += await commandExecutor.ExecuteAsync(RepairPlatformDirectoryPermission,
            new Dictionary<string, object?> { ["RequiredPermission"] = IdentityAuthorizationContributor.NavigationRead, ["Now"] = clock.UtcNow }, cancellationToken).ConfigureAwait(false);
        if (authorizationCatalog.Navigation.Any(item => item.Id == "printing-preview" && item.RequiredPermission == "printing.templates.read"))
            permissionUpdated += await commandExecutor.ExecuteAsync(RepairPrintingTemplatePermission,
                new Dictionary<string, object?> { ["RequiredPermission"] = "printing.templates.read", ["Now"] = clock.UtcNow }, cancellationToken).ConfigureAwait(false);
        var navigationCreated = await SyncMissingNavigationEntriesAsync(
                routeNameIndex,
                cancellationToken)
            .ConfigureAwait(false);
        var buttonCreated = await SyncMissingActionEntriesAsync(
                routeNameIndex,
                cancellationToken)
            .ConfigureAwait(false);
        var reparented = await ReparentRootMenusToModulesAsync(
                routeNameIndex,
                cancellationToken)
            .ConfigureAwait(false);
        var domainCreated = await SyncDomainDirectoriesAsync(
                routeNameIndex,
                cancellationToken)
            .ConfigureAwait(false);
        var domainReparented = await ReparentModuleDirectoriesToDomainsAsync(
                routeNameIndex,
                cancellationToken)
            .ConfigureAwait(false);
        var totalDefinitions = authorizationCatalog.Navigation.Count
            + authorizationCatalog.Actions.Count
            + authorizationCatalog.Modules.Count;
        var created = moduleCreated + navigationCreated + buttonCreated + domainCreated;
        return (
            created,
            totalDefinitions - navigationCreated - buttonCreated,
            reparented + domainReparented,
            permissionUpdated);
    }

    private async Task<int> SyncModuleDirectoriesAsync(
        Dictionary<string, Guid> routeNameIndex,
        CancellationToken cancellationToken)
    {
        var created = 0;
        var now = clock.UtcNow;

        foreach (var module in authorizationCatalog.Modules)
        {
            var routeName = BuildModuleDirectoryRouteName(module.Key);
            if (routeNameIndex.ContainsKey(routeName))
            {
                continue;
            }

            var menuId = idGenerator.NewId();
            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.InsertHostMenu,
                    new InsertIdentityNavigation(
                        menuId,
                        null,
                        HostScope,
                        null,
                        routeName,
                        BuildModuleDirectoryPath(module.Key),
                        "layout",
                        module.Title,
                        module.Title,
                        "grid",
                        module.Order * 100,
                        ResolveModuleDirectoryPermission(module.Key),
                        true,
                        true,
                        now,
                        null,
                        1,
                        IdentityHostMenuTypes.Directory,
                        null,
                        null,
                        false,
                        false,
                        false,
                        false,
                        null),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affectedRows != 1)
            {
                throw new InvalidOperationException(
                    $"Host module directory sync insert affected {affectedRows} rows instead of one.");
            }

            routeNameIndex[routeName] = menuId;
            created++;
        }

        return created;
    }

    private async Task<int> SyncMissingNavigationEntriesAsync(
        Dictionary<string, Guid> routeNameIndex,
        CancellationToken cancellationToken)
    {
        var definitions = authorizationCatalog.Navigation.ToArray();
        if (definitions.Length == 0)
        {
            return 0;
        }

        var pending = definitions
            .Where(definition => !routeNameIndex.ContainsKey(definition.RouteName))
            .ToList();
        if (pending.Count == 0)
        {
            return 0;
        }

        var navigationById = definitions.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        var created = 0;
        var now = clock.UtcNow;
        var guard = 0;
        while (pending.Count > 0 && guard < pending.Count + definitions.Length)
        {
            guard++;
            for (var index = pending.Count - 1; index >= 0; index--)
            {
                var definition = pending[index];
                if (definition.ParentId is not null
                    && (!navigationById.TryGetValue(definition.ParentId, out var parentDefinition)
                        || !routeNameIndex.ContainsKey(parentDefinition.RouteName)))
                {
                    continue;
                }

                var parentId = ResolveNavigationParentId(definition, navigationById, routeNameIndex);
                var menuId = idGenerator.NewId();
                var affectedRows = await commandExecutor.ExecuteAsync(
                        IdentitySql.InsertHostMenu,
                        new InsertIdentityNavigation(
                            menuId,
                            null,
                            HostScope,
                            parentId,
                            definition.RouteName,
                            definition.Path,
                            definition.ComponentKey,
                            definition.Title,
                            definition.Caption,
                            definition.Icon,
                            definition.Order,
                            definition.RequiredPermission,
                            true,
                            true,
                            now,
                            null,
                            1,
                            definition.MenuType,
                            definition.Redirect,
                            definition.LinkUrl,
                            definition.IsHidden,
                            definition.IsKeepAlive,
                            definition.IsAffix,
                            definition.IsEmbedded,
                            null),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (affectedRows != 1)
                {
                    throw new InvalidOperationException(
                        $"Host navigation catalog sync insert affected {affectedRows} rows instead of one.");
                }

                routeNameIndex[definition.RouteName] = menuId;
                pending.RemoveAt(index);
                created++;
            }
        }

        if (pending.Count > 0)
        {
            var missingParents = string.Join(
                ", ",
                pending.Select(item => item.RouteName));
            throw new InvalidOperationException(
                $"Host navigation catalog sync could not resolve parents for: {missingParents}.");
        }

        return created;
    }

    private async Task<int> SyncMissingActionEntriesAsync(
        Dictionary<string, Guid> routeNameIndex,
        CancellationToken cancellationToken)
    {
        var navigationById = authorizationCatalog.Navigation.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        var created = 0;
        var now = clock.UtcNow;

        foreach (var action in authorizationCatalog.Actions)
        {
            if (!navigationById.TryGetValue(action.NavigationId, out var navigation))
            {
                continue;
            }

            var routeName = BuildActionRouteName(action.Id);
            if (routeNameIndex.ContainsKey(routeName))
            {
                continue;
            }

            if (!routeNameIndex.TryGetValue(navigation.RouteName, out var parentMenuId))
            {
                continue;
            }

            var menuId = idGenerator.NewId();
            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.InsertHostMenu,
                    new InsertIdentityNavigation(
                        menuId,
                        null,
                        HostScope,
                        parentMenuId,
                        routeName,
                        string.Empty,
                        action.ClientActionKey,
                        action.Name,
                        action.Name,
                        "key",
                        action.Order,
                        action.PermissionCode,
                        true,
                        true,
                        now,
                        null,
                        1,
                        IdentityHostMenuTypes.Button,
                        null,
                        null,
                        false,
                        false,
                        false,
                        false,
                        null),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affectedRows != 1)
            {
                throw new InvalidOperationException(
                    $"Host action catalog sync insert affected {affectedRows} rows instead of one.");
            }

            routeNameIndex[routeName] = menuId;
            created++;
        }

        return created;
    }

    private async Task<int> ReparentRootMenusToModulesAsync(
        Dictionary<string, Guid> routeNameIndex,
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<HostMenuSyncRow>(
                IdentitySql.ListHostMenuSyncRows,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var navigationByRouteName = authorizationCatalog.Navigation.ToDictionary(
            item => item.RouteName,
            StringComparer.Ordinal);
        var navigationById = authorizationCatalog.Navigation.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        var actionByRouteName = authorizationCatalog.Actions.ToDictionary(
            action => BuildActionRouteName(action.Id),
            StringComparer.Ordinal);
        var reparented = 0;
        var now = clock.UtcNow;

        foreach (var row in rows)
        {
            if (row.ParentId is not null
                || IsModuleDirectoryRouteName(row.RouteName)
                || IsDomainDirectoryRouteName(row.RouteName))
            {
                continue;
            }

            Guid? targetParentId = null;
            if (navigationByRouteName.TryGetValue(row.RouteName, out var navigation))
            {
                targetParentId = ResolveNavigationParentId(
                    navigation,
                    navigationById,
                    routeNameIndex);
            }
            else if (actionByRouteName.TryGetValue(row.RouteName, out var action)
                && navigationById.TryGetValue(action.NavigationId, out var pageNavigation))
            {
                if (routeNameIndex.TryGetValue(pageNavigation.RouteName, out var pageMenuId))
                {
                    targetParentId = pageMenuId;
                }
            }

            if (targetParentId is not Guid parentMenuId || parentMenuId == row.Id)
            {
                continue;
            }

            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.ReparentHostSystemMenu,
                    IdentitySqlParameters.Create(
                        ("MenuId", row.Id),
                        ("ParentId", parentMenuId),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affectedRows == 1)
            {
                reparented++;
            }
        }

        return reparented;
    }

    private async Task<int> SyncDomainDirectoriesAsync(
        Dictionary<string, Guid> routeNameIndex,
        CancellationToken cancellationToken)
    {
        var created = 0;
        var now = clock.UtcNow;

        foreach (var domain in HostNavigationDomainLayout.AllDomains)
        {
            var routeName = BuildDomainDirectoryRouteName(domain.Key);
            if (routeNameIndex.ContainsKey(routeName))
            {
                continue;
            }

            var menuId = idGenerator.NewId();
            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.InsertHostMenu,
                    new InsertIdentityNavigation(
                        menuId,
                        null,
                        HostScope,
                        null,
                        routeName,
                        BuildDomainDirectoryPath(domain.Key),
                        "layout",
                        domain.Title,
                        domain.Title,
                        "grid",
                        domain.Order * 100,
                        HostNavigationDomainLayout.DomainDirectoryPermission,
                        true,
                        true,
                        now,
                        null,
                        1,
                        IdentityHostMenuTypes.Directory,
                        null,
                        null,
                        false,
                        false,
                        false,
                        false,
                        null),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affectedRows != 1)
            {
                throw new InvalidOperationException(
                    $"Host domain directory sync insert affected {affectedRows} rows instead of one.");
            }

            routeNameIndex[routeName] = menuId;
            created++;
        }

        return created;
    }

    private async Task<int> ReparentModuleDirectoriesToDomainsAsync(
        Dictionary<string, Guid> routeNameIndex,
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<HostMenuSyncRow>(
                IdentitySql.ListHostMenuSyncRows,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var reparented = 0;
        var now = clock.UtcNow;

        foreach (var row in rows)
        {
            if (!IsModuleDirectoryRouteName(row.RouteName))
            {
                continue;
            }

            var moduleKey = row.RouteName["module-".Length..];
            var domainKey = HostNavigationDomainLayout.ResolveDomainKeyForModule(moduleKey);
            if (!routeNameIndex.TryGetValue(
                    BuildDomainDirectoryRouteName(domainKey),
                    out var domainParentId))
            {
                continue;
            }

            if (row.ParentId == domainParentId)
            {
                continue;
            }

            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.ReparentHostSystemMenu,
                    IdentitySqlParameters.Create(
                        ("MenuId", row.Id),
                        ("ParentId", domainParentId),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affectedRows == 1)
            {
                reparented++;
            }
        }

        return reparented;
    }

    private Guid? ResolveNavigationParentId(
        NavigationDefinition definition,
        IReadOnlyDictionary<string, NavigationDefinition> navigationById,
        IReadOnlyDictionary<string, Guid> routeNameIndex)
    {
        if (definition.ParentId is not null
            && navigationById.TryGetValue(definition.ParentId, out var parentDefinition)
            && routeNameIndex.TryGetValue(parentDefinition.RouteName, out var explicitParentId))
        {
            return explicitParentId;
        }

        return ResolveModuleDirectoryId(definition.Id, routeNameIndex);
    }

    private Guid? ResolveModuleDirectoryId(
        string navigationId,
        IReadOnlyDictionary<string, Guid> routeNameIndex)
    {
        if (!authorizationCatalog.NavigationModuleKeys.TryGetValue(navigationId, out var moduleKey))
        {
            return null;
        }

        return routeNameIndex.TryGetValue(
            BuildModuleDirectoryRouteName(moduleKey),
            out var moduleDirectoryId)
            ? moduleDirectoryId
            : null;
    }

    private string ResolveModuleDirectoryPermission(string moduleKey)
    {
        if (moduleKey == "workflow") return IdentityAuthorizationContributor.NavigationRead;
        if (moduleKey == "ai") return IdentityAuthorizationContributor.NavigationRead;
        if (moduleKey is "notifications" or "reporting" or "platform" or "printing") return IdentityAuthorizationContributor.NavigationRead;
        foreach (var navigation in authorizationCatalog.Navigation)
        {
            if (authorizationCatalog.NavigationModuleKeys.TryGetValue(navigation.Id, out var mappedModuleKey)
                && string.Equals(mappedModuleKey, moduleKey, StringComparison.Ordinal))
            {
                return navigation.RequiredPermission;
            }
        }

        return authorizationCatalog.Permissions[0].Code;
    }

    internal static string BuildActionRouteName(string actionId) =>
        actionId.Replace('.', '-');

    internal static string BuildModuleDirectoryRouteName(string moduleKey) =>
        $"module-{moduleKey}";

    internal static string BuildModuleDirectoryPath(string moduleKey) =>
        $"/modules/{moduleKey}";

    internal static bool IsModuleDirectoryRouteName(string routeName) =>
        routeName.StartsWith("module-", StringComparison.Ordinal);

    internal static string BuildDomainDirectoryRouteName(string domainKey) =>
        $"domain-{domainKey}";

    internal static string BuildDomainDirectoryPath(string domainKey) =>
        $"/domains/{domainKey}";

    internal static bool IsDomainDirectoryRouteName(string routeName) =>
        routeName.StartsWith("domain-", StringComparison.Ordinal);

    private async Task<Dictionary<string, Guid>> LoadRouteNameIndexAsync(
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<HostMenuRouteNameRow>(
                IdentitySql.ListHostMenuRouteNames,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(
            row => row.RouteName,
            row => row.Id,
            StringComparer.Ordinal);
    }

    internal sealed class HostMenuRouteNameRow
    {
        public Guid Id { get; set; }

        public string RouteName { get; set; } = string.Empty;
    }

    internal sealed class HostMenuSyncRow
    {
        public Guid Id { get; set; }

        public Guid? ParentId { get; set; }

        public string RouteName { get; set; } = string.Empty;

        public string MenuType { get; set; } = string.Empty;
    }
}
