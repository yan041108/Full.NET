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
    componentKey: 'users',
    routeName: 'users',
    path: '/identity/users'
  },
  {
    componentKey: 'roles',
    routeName: 'roles',
    path: '/identity/roles'
  },
  {
    componentKey: 'super-administrators',
    routeName: 'super-administrators',
    path: '/identity/super-administrators'
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
