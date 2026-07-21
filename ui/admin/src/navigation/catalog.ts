import {
  createAdminNavigationCatalog,
  type NavigationNode
} from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';

export interface LocalNavigationDefinition {
  routeName: string;
  path: string;
  titleKey: MessageKey;
  captionKey: MessageKey;
}

const navigationCatalog = createAdminNavigationCatalog();

const messageKeys = new Map<string, Pick<LocalNavigationDefinition, 'titleKey' | 'captionKey'>>([
  ['overview', {
    titleKey: 'navigation.overview.title',
    captionKey: 'navigation.overview.caption'
  }],
  ['tenant-context', {
    titleKey: 'navigation.tenantContext.title',
    captionKey: 'navigation.tenantContext.caption'
  }],
  ['users', {
    titleKey: 'navigation.users.title',
    captionKey: 'navigation.users.caption'
  }],
  ['roles', {
    titleKey: 'navigation.roles.title',
    captionKey: 'navigation.roles.caption'
  }],
  ['super-administrators', {
    titleKey: 'navigation.superAdministrators.title',
    captionKey: 'navigation.superAdministrators.caption'
  }]
]);

/** 返回组件键对应的本地可信导航定义，未知键始终拒绝。 */
export function localNavigationFor(
  componentKey: string
): Readonly<LocalNavigationDefinition> | undefined {
  const entry = navigationCatalog.localNavigationFor(componentKey);
  const keys = messageKeys.get(componentKey);
  if (entry === undefined || keys === undefined) {
    return undefined;
  }

  return {
    routeName: entry.routeName,
    path: entry.path,
    ...keys
  };
}

/** 判断服务端导航中的每个组件键是否已由当前 Vue 版本显式发布。 */
export function isSupportedNavigationTree(
  navigation: readonly NavigationNode[]
): boolean {
  return navigationCatalog.isSupportedNavigationTree(navigation);
}

/** 按服务端树顺序生成只读平铺视图，供侧栏和路由权限检查复用。 */
export function flattenNavigation(
  navigation: readonly NavigationNode[]
): NavigationNode[] {
  return navigationCatalog.flattenNavigation(navigation);
}
