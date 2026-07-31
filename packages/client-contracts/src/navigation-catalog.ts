import type { NavigationNode } from './authorization.js';

/** 管理端本地导航目录项；componentKey 必须与服务端导航树一致。 */
export interface AdminNavigationCatalogEntry {
  componentKey: string;
  routeName: string;
  path: string;
}

/** 当前双管理端共同发布的导航白名单。 */
export const ADMIN_NAVIGATION_CATALOG: readonly AdminNavigationCatalogEntry[] = [
  {
    componentKey: 'overview',
    routeName: 'overview',
    path: '/'
  },
  {
    componentKey: 'tenant-context',
    routeName: 'tenant-context',
    path: '/tenant-context'
  },
  {
    componentKey: 'tenants',
    routeName: 'tenant-management',
    path: '/tenants'
  },
  {
    componentKey: 'tenant-packages',
    routeName: 'tenant-packages',
    path: '/tenant-packages'
  },
  {
    componentKey: 'users',
    routeName: 'users',
    path: '/identity/users'
  },
  {
    componentKey: 'online-sessions',
    routeName: 'online-sessions',
    path: '/identity/online-sessions'
  },
  {
    componentKey: 'api-keys',
    routeName: 'api-keys',
    path: '/identity/api-keys'
  },
  {
    componentKey: 'roles',
    routeName: 'roles',
    path: '/identity/roles'
  },
  {
    componentKey: 'menus',
    routeName: 'menus',
    path: '/identity/menus'
  },
  {
    componentKey: 'org-units',
    routeName: 'org-units',
    path: '/organization/units'
  },
  {
    componentKey: 'org-user-units',
    routeName: 'org-user-units',
    path: '/organization/user-units'
  },
  {
    componentKey: 'org-positions',
    routeName: 'org-positions',
    path: '/organization/positions'
  },
  {
    componentKey: 'org-position-levels',
    routeName: 'org-position-levels',
    path: '/organization/position-levels'
  },
  {
    componentKey: 'org-user-positions',
    routeName: 'org-user-positions',
    path: '/organization/user-positions'
  },
  {
    componentKey: 'super-administrators',
    routeName: 'super-administrators',
    path: '/identity/super-administrators'
  },
  {
    componentKey: 'dict-types',
    routeName: 'dict-types',
    path: '/settings/dict-types'
  },
  {
    componentKey: 'tenant-dict-types',
    routeName: 'tenant-dict-types',
    path: '/settings/tenant-dict-types'
  },
  {
    componentKey: 'config-entries',
    routeName: 'config-entries',
    path: '/settings/config-entries'
  },
  {
    componentKey: 'enum-catalogs',
    routeName: 'enum-catalogs',
    path: '/settings/enum-catalogs'
  },
  {
    componentKey: 'host-files',
    routeName: 'host-files',
    path: '/files/host-files'
  },
  {
    componentKey: 'host-announcements',
    routeName: 'host-announcements',
    path: '/notifications/host-announcements'
  },
  {
    componentKey: 'inbox-messages',
    routeName: 'inbox-messages',
    path: '/notifications/inbox-messages'
  },
  {
    componentKey: 'host-jobs',
    routeName: 'host-jobs',
    path: '/jobs/host-definitions'
  },
  {
    componentKey: 'code-generation-previews',
    routeName: 'code-generation-previews',
    path: '/code-generation/previews'
  },
  {
    componentKey: 'access-logs',
    routeName: 'access-logs',
    path: '/auditing/access-logs'
  },
  {
    componentKey: 'operation-logs',
    routeName: 'operation-logs',
    path: '/auditing/operation-logs'
  },
  {
    componentKey: 'exception-logs',
    routeName: 'exception-logs',
    path: '/auditing/exception-logs'
  }
] as const;

export interface AdminNavigationCatalog {
  localNavigationFor(
    componentKey: string
  ): Readonly<AdminNavigationCatalogEntry> | undefined;
  isSupportedNavigationTree(
    navigation: readonly NavigationNode[]
  ): boolean;
  flattenNavigation(navigation: readonly NavigationNode[]): NavigationNode[];
}

/** 基于固定目录创建导航白名单校验器，供 Vue/Layui 共享同一组 route/path 规则。 */
export function createAdminNavigationCatalog(
  catalog: readonly AdminNavigationCatalogEntry[] = ADMIN_NAVIGATION_CATALOG
): AdminNavigationCatalog {
  const supportedComponents = new Map(
    catalog.map(entry => [entry.componentKey, entry])
  );

  function localNavigationFor(
    componentKey: string
  ): Readonly<AdminNavigationCatalogEntry> | undefined {
    return supportedComponents.get(componentKey);
  }

  function isSupportedNavigationTree(
    navigation: readonly NavigationNode[]
  ): boolean {
    return navigation.every(node => {
      const local = supportedComponents.get(node.componentKey);
      return local !== undefined
        && local.routeName === node.routeName
        && local.path === node.path
        && isSupportedNavigationTree(node.children);
    });
  }

  function flattenNavigation(
    navigation: readonly NavigationNode[]
  ): NavigationNode[] {
    const result: NavigationNode[] = [];
    for (const node of navigation) {
      result.push(node, ...flattenNavigation(node.children));
    }

    return result;
  }

  return {
    localNavigationFor,
    isSupportedNavigationTree,
    flattenNavigation
  };
}
