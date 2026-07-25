import type { Component } from 'vue';
import {
  Grid,
  OfficeBuilding,
  User,
  Menu as MenuIcon,
  Setting,
  House
} from '@element-plus/icons-vue';
import type { NavigationNode } from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';
import {
  flattenNavigation,
  localNavigationFor
} from '../../../navigation/catalog';

/** Art 壳层展示用的扁平导航项；仅包含已通过白名单校验的页面。 */
export interface ShellNavigationItem {
  path: string;
  routeName: string;
  componentKey: string;
  title: string;
  caption: string;
  icon: Component;
}

/** Art 壳层标签页项；路径必须来自已授权导航。 */
export interface ShellTabItem {
  path: string;
  title: string;
}

/** 混合/双栏布局的一级菜单分组；items 仅包含已发布页面。 */
export interface ShellNavigationGroup {
  id: string;
  title: string;
  icon: Component;
  items: ShellNavigationItem[];
}

export interface BuildShellNavigationOptions {
  navigation: NavigationNode[];
  translate: (key: MessageKey) => string;
}

interface NavigationGroupRule {
  id: string;
  titleKey: MessageKey;
  matchPath: (path: string) => boolean;
}

const navigationGroupRules: NavigationGroupRule[] = [
  {
    id: 'platform',
    titleKey: 'shell.navGroup.platform',
    matchPath: path => path === '/' || path === '/tenant-context'
  },
  {
    id: 'tenancy',
    titleKey: 'shell.navGroup.tenancy',
    matchPath: path => path === '/tenants' || path === '/tenant-packages'
  },
  {
    id: 'identity',
    titleKey: 'shell.navGroup.identity',
    matchPath: path => path.startsWith('/identity')
  },
  {
    id: 'organization',
    titleKey: 'shell.navGroup.organization',
    matchPath: path => path.startsWith('/organization')
  },
  {
    id: 'settings',
    titleKey: 'shell.navGroup.settings',
    matchPath: path => path.startsWith('/settings')
  }
];

const iconCatalog: Record<string, Component> = {
  dashboard: Grid,
  building: OfficeBuilding,
  users: User,
  menus: MenuIcon,
  roles: Setting,
  overview: House
};

/**
 * 将服务端导航树转换为 Art 壳层可渲染菜单。
 * 未知 componentKey 会被过滤，禁止动态路径或字符串组件加载。
 */
function toShellNavigationItem(
  node: NavigationNode,
  translate: (key: MessageKey) => string
): ShellNavigationItem | null {
  const local = localNavigationFor(node.componentKey);
  if (!local) {
    return null;
  }

  const title = local.routeName === node.routeName
    ? translate(local.titleKey)
    : node.title;
  const caption = local.routeName === node.routeName
    ? translate(local.captionKey)
    : node.caption;

  return {
    path: node.path,
    routeName: node.routeName,
    componentKey: node.componentKey,
    title,
    caption,
    icon: resolveShellIcon(node.icon)
  };
}

export function buildShellNavigation(
  options: BuildShellNavigationOptions
): ShellNavigationItem[] {
  return flattenNavigation(options.navigation).flatMap(node => {
    const item = toShellNavigationItem(node, options.translate);
    return item ? [item] : [];
  });
}

function buildGroupsFromNavigationTree(
  navigation: NavigationNode[],
  translate: (key: MessageKey) => string
): ShellNavigationGroup[] {
  const groups: ShellNavigationGroup[] = [];

  for (const node of navigation) {
    const childItems = node.children
      .map(child => toShellNavigationItem(child, translate))
      .filter((item): item is ShellNavigationItem => item !== null);
    const selfItem = toShellNavigationItem(node, translate);

    if (childItems.length === 0) {
      continue;
    }

    const items = selfItem ? [selfItem, ...childItems] : childItems;
    const title = selfItem?.title ?? node.title;
    groups.push({
      id: node.id,
      title,
      icon: selfItem?.icon ?? childItems[0]?.icon ?? Grid,
      items
    });
  }

  return groups;
}

function buildGroupsFromFlatNavigation(
  items: ShellNavigationItem[],
  translate: (key: MessageKey) => string
): ShellNavigationGroup[] {
  const grouped = new Map<string, ShellNavigationGroup>();

  for (const rule of navigationGroupRules) {
    grouped.set(rule.id, {
      id: rule.id,
      title: translate(rule.titleKey),
      icon: Grid,
      items: []
    });
  }

  for (const item of items) {
    const rule = navigationGroupRules.find(candidate => candidate.matchPath(item.path));
    const groupId = rule?.id ?? 'platform';
    const group = grouped.get(groupId);
    if (!group) {
      continue;
    }

    group.items.push(item);
    if (group.items.length === 1) {
      group.icon = item.icon;
    }
  }

  return [...grouped.values()].filter(group => group.items.length > 0);
}

/**
 * 为混合/双栏布局生成一级分组。
 * 服务端树若含 children 则按树分组，否则按路径前缀推断分组。
 */
export function buildShellNavigationGroups(
  options: BuildShellNavigationOptions
): ShellNavigationGroup[] {
  const flatItems = buildShellNavigation(options);
  const hasHierarchy = options.navigation.some(node => node.children.length > 0);
  if (hasHierarchy) {
    const treeGroups = buildGroupsFromNavigationTree(
      options.navigation,
      options.translate
    );
    if (treeGroups.length > 0) {
      return treeGroups;
    }
  }

  const inferredGroups = buildGroupsFromFlatNavigation(
    flatItems,
    options.translate
  );
  if (inferredGroups.length > 0) {
    return inferredGroups;
  }

  return [{
    id: 'platform',
    title: options.translate('shell.navGroup.platform'),
    icon: flatItems[0]?.icon ?? Grid,
    items: flatItems
  }];
}

/** 根据当前路径解析应高亮的一级菜单分组。 */
export function resolveActiveGroupId(
  groups: ShellNavigationGroup[],
  path: string
): string {
  const matched = groups.find(group =>
    group.items.some(item => item.path === path)
  );
  return matched?.id ?? groups[0]?.id ?? 'platform';
}

/** 解析导航图标；仅允许预置映射，避免运行时字符串导入。 */
export function resolveShellIcon(icon: string): Component {
  return iconCatalog[icon] ?? Grid;
}

/** 根据当前路径与授权导航生成标签页集合。 */
export function upsertShellTab(
  tabs: ShellTabItem[],
  navigation: ShellNavigationItem[],
  activePath: string
): ShellTabItem[] {
  const active = navigation.find(item => item.path === activePath);
  if (!active) {
    return tabs;
  }

  if (tabs.some(tab => tab.path === active.path)) {
    return tabs;
  }

  return [...tabs, { path: active.path, title: active.title }];
}

/** 关闭标签页后返回应激活的路径。 */
export function closeShellTab(
  tabs: ShellTabItem[],
  closingPath: string,
  activePath: string
): { tabs: ShellTabItem[]; nextPath: string } {
  if (tabs.length <= 1) {
    return { tabs, nextPath: activePath };
  }

  const index = tabs.findIndex(tab => tab.path === closingPath);
  if (index < 0) {
    return { tabs, nextPath: activePath };
  }

  const nextTabs = tabs.filter(tab => tab.path !== closingPath);
  if (closingPath !== activePath) {
    return { tabs: nextTabs, nextPath: activePath };
  }

  const fallback = nextTabs[Math.max(0, index - 1)] ?? nextTabs[0];
  return { tabs: nextTabs, nextPath: fallback?.path ?? '/' };
}
