import type { NavigationNode } from '@fullnet/client-contracts';

const supportedComponents = new Map([
  ['overview', { routeName: 'overview', path: '/' }],
  ['tenant-context', {
    routeName: 'tenant-context',
    path: '/tenant-context'
  }]
]);

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
