import type { NavigationNode } from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';

export interface LocalNavigationDefinition {
  routeName: string;
  path: string;
  titleKey: MessageKey;
  captionKey: MessageKey;
}

const supportedComponents = new Map<string, LocalNavigationDefinition>([
  ['overview', {
    routeName: 'overview',
    path: '/',
    titleKey: 'navigation.overview.title',
    captionKey: 'navigation.overview.caption'
  }],
  ['tenant-context', {
    routeName: 'tenant-context',
    path: '/tenant-context',
    titleKey: 'navigation.tenantContext.title',
    captionKey: 'navigation.tenantContext.caption'
  }],
  ['super-administrators', {
    routeName: 'super-administrators',
    path: '/identity/super-administrators',
    titleKey: 'navigation.superAdministrators.title',
    captionKey: 'navigation.superAdministrators.caption'
  }]
]);

/** 返回组件键对应的本地可信导航定义，未知键始终拒绝。 */
export function localNavigationFor(
  componentKey: string
): Readonly<LocalNavigationDefinition> | undefined {
  return supportedComponents.get(componentKey);
}

/** 判断服务端导航中的每个组件键是否已由当前 Vue 版本显式发布。 */
export function isSupportedNavigationTree(
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

/** 按服务端树顺序生成只读平铺视图，供侧栏和路由权限检查复用。 */
export function flattenNavigation(
  navigation: readonly NavigationNode[]
): NavigationNode[] {
  const result: NavigationNode[] = [];
  for (const node of navigation) {
    result.push(node, ...flattenNavigation(node.children));
  }

  return result;
}
