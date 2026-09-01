import type { Component } from 'vue';
import {
  Bell,
  DataAnalysis,
  Document,
  Files,
  Grid,
  House,
  List,
  Menu as MenuIcon,
  Monitor,
  OfficeBuilding,
  Setting,
  Timer,
  User
} from '@element-plus/icons-vue';
import {
  createAdminNavigationCatalog,
  type NavigationNode
} from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';
import {
  flattenNavigation,
  localNavigationFor
} from '../../../navigation/catalog';
import { resolveMenuIconComponent } from '../../../identity/host-menu-icons';

/** Art 壳层侧栏树节点；目录节点可含 children。 */
export interface ShellNavigationTreeItem extends ShellNavigationItem {
  id: string;
  children: ShellNavigationTreeItem[];
}

/** 分组目录在 ElMenu 中使用的虚拟路径前缀。 */
export const SHELL_NAV_GROUP_PATH_PREFIX = '__group__:';

/** Art 壳层展示用的扁平导航项；仅包含已通过白名单校验的页面。 */
export interface ShellNavigationItem {
  path: string;
  routeName: string;
  componentKey: string;
  title: string;
  caption: string;
  icon: Component;
  isAffix: boolean;
}

/** Art 壳层标签页项；路径必须来自已授权导航。 */
export interface ShellTabItem {
  path: string;
  title: string;
  icon?: Component;
  isAffix?: boolean;
}

/** 混合/双栏布局的一级菜单分组；items 仅包含已发布页面。 */
export interface ShellNavigationGroup {
  id: string;
  title: string;
  icon: Component;
  items: ShellNavigationItem[];
}

/** 构建壳层导航所需的最小输入：服务端授权导航树与本地翻译器。 */
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
  },
  {
    id: 'document',
    titleKey: 'shell.navGroup.document',
    matchPath: path => path.startsWith('/document')
  },
  {
    id: 'files',
    titleKey: 'shell.navGroup.files',
    matchPath: path => path.startsWith('/files')
  },
  {
    id: 'notifications',
    titleKey: 'shell.navGroup.notifications',
    matchPath: path => path.startsWith('/notifications')
  },
  {
    id: 'jobs',
    titleKey: 'shell.navGroup.jobs',
    matchPath: path => path.startsWith('/jobs')
  },
  {
    id: 'code-generation',
    titleKey: 'shell.navGroup.codeGeneration',
    matchPath: path => path.startsWith('/code-generation')
  },
  {
    id: 'serial-numbers',
    titleKey: 'shell.navGroup.serialNumbers',
    matchPath: path => path.startsWith('/serial-numbers')
  },
  {
    id: 'auditing',
    titleKey: 'shell.navGroup.auditing',
    matchPath: path => path.startsWith('/auditing')
  }
];

const groupIconCatalog: Record<string, Component> = {
  platform: House,
  tenancy: OfficeBuilding,
  identity: User,
  organization: MenuIcon,
  settings: Setting,
  document: Document,
  files: Files,
  notifications: Bell,
  jobs: Timer,
  'code-generation': DataAnalysis,
  'serial-numbers': List,
  auditing: Monitor
};

const sharedNavigationCatalog = createAdminNavigationCatalog();

function translateNavigationLabel(
  translate: (key: MessageKey) => string,
  key: MessageKey,
  fallback: string
): string {
  try {
    return translate(key);
  } catch {
    return fallback;
  }
}

/**
 * 将服务端导航树转换为 Art 壳层可渲染菜单。
 * 未知 componentKey 会被过滤，禁止动态路径或字符串组件加载。
 */
function toShellNavigationItem(
  node: NavigationNode,
  translate: (key: MessageKey) => string
): ShellNavigationItem | null {
  const catalogEntry = sharedNavigationCatalog.localNavigationFor(node.componentKey);
  if (!catalogEntry) {
    return null;
  }

  const local = localNavigationFor(node.componentKey);
  const title = local && local.routeName === node.routeName
    ? translateNavigationLabel(translate, local.titleKey, node.title)
    : node.title;
  const caption = local && local.routeName === node.routeName
    ? translateNavigationLabel(translate, local.captionKey, node.caption)
    : node.caption;

  return {
    path: node.path,
    routeName: node.routeName,
    componentKey: node.componentKey,
    title,
    caption,
    icon: resolveShellIcon(node.icon),
    isAffix: resolveShellNavigationAffix(node)
  };
}

/** 工作台等首页在标签栏固定；与 Art Design Pro `fixedTab` / Admin.NET `isAffix` 对齐。 */
export function resolveShellNavigationAffix(
  node: Pick<NavigationNode, 'path' | 'componentKey' | 'isAffix'>
): boolean {
  return node.isAffix === true
    || node.path === '/'
    || node.componentKey === 'overview';
}

function mapNavigationTreeToShellItems(
  navigation: NavigationNode[],
  translate: (key: MessageKey) => string
): ShellNavigationItem[] {
  return flattenNavigation(navigation).flatMap(node => {
    const item = toShellNavigationItem(node, translate);
    return item ? [item] : [];
  });
}

/** 生成壳层可直接消费的扁平导航；映射异常时退回服务端标题，避免侧栏完全空白。 */
export function buildShellNavigation(
  options: BuildShellNavigationOptions
): ShellNavigationItem[] {
  const items = mapNavigationTreeToShellItems(
    options.navigation,
    options.translate
  );
  if (items.length > 0 || options.navigation.length === 0) {
    return items;
  }

  // 服务端导航已通过登录白名单，但壳层映射异常时回退服务端标题，避免侧栏空白。
  return flattenNavigation(options.navigation).map(node => ({
    path: node.path,
    routeName: node.routeName,
    componentKey: node.componentKey,
    title: node.title,
    caption: node.caption,
    icon: resolveShellIcon(node.icon),
    isAffix: resolveShellNavigationAffix(node)
  }));
}

/** 服务端树已分层时直接按树的一级节点分组，保留后端声明的结构语义。 */
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

/** 服务端仅返回扁平导航时，按路径前缀推断一级分组以兼容 Art 壳层布局。 */
function buildGroupsFromFlatNavigation(
  items: ShellNavigationItem[],
  translate: (key: MessageKey) => string
): ShellNavigationGroup[] {
  const grouped = new Map<string, ShellNavigationGroup>();

  for (const rule of navigationGroupRules) {
    grouped.set(rule.id, {
      id: rule.id,
      title: translate(rule.titleKey),
      icon: groupIconCatalog[rule.id] ?? Grid,
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

  return navigationGroupRules
    .map(rule => grouped.get(rule.id))
    .filter((group): group is ShellNavigationGroup =>
      group !== undefined && group.items.length > 0
    );
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
  return resolveMenuIconComponent(icon);
}

/** 将壳层导航项包装成树节点，同时挂接稳定 id 供菜单组件追踪。 */
function toShellNavigationTreeItem(
  item: ShellNavigationItem,
  id: string,
  children: ShellNavigationTreeItem[] = []
): ShellNavigationTreeItem {
  return {
    ...item,
    id,
    children
  };
}

/** 为分组目录生成不会与真实业务路径冲突的虚拟菜单路径。 */
function groupPath(groupId: string): string {
  return `${SHELL_NAV_GROUP_PATH_PREFIX}${groupId}`;
}

/** 优先保留服务端树结构；仅在本地白名单命中时生成可渲染目录或页面节点。 */
function mapServerNavigationTree(
  navigation: NavigationNode[],
  translate: (key: MessageKey) => string
): ShellNavigationTreeItem[] {
  const mapNode = (node: NavigationNode): ShellNavigationTreeItem | null => {
    const childNodes = node.children
      .map(child => mapNode(child))
      .filter((item): item is ShellNavigationTreeItem => item !== null);
    const self = toShellNavigationItem(node, translate);

    if (!self && childNodes.length === 0) {
      return null;
    }

    if (childNodes.length === 0 && self) {
      return toShellNavigationTreeItem(self, node.id);
    }

    if (childNodes.length === 1 && !self) {
      return childNodes[0]!;
    }

    const directory: ShellNavigationTreeItem = self
      ? toShellNavigationTreeItem(self, node.id, childNodes)
      : {
          id: node.id,
          path: groupPath(node.id),
          routeName: node.id,
          componentKey: node.id,
          title: node.title,
          caption: node.caption,
          icon: resolveShellIcon(node.icon),
          isAffix: false,
          children: childNodes
        };

    return directory;
  };

  return navigation
    .map(node => mapNode(node))
    .filter((item): item is ShellNavigationTreeItem => item !== null);
}

/** 将一级导航分组转为左侧树节点；多项分组表现为目录，单项分组直接落为页面。 */
function buildTreeFromNavigationGroups(
  groups: ShellNavigationGroup[]
): ShellNavigationTreeItem[] {
  const tree: ShellNavigationTreeItem[] = [];

  for (const group of groups) {
    if (group.items.length === 0) {
      continue;
    }

    if (group.items.length === 1) {
      const [onlyItem] = group.items;
      tree.push(toShellNavigationTreeItem(onlyItem, onlyItem.path));
      continue;
    }

    tree.push({
      id: group.id,
      path: groupPath(group.id),
      routeName: group.id,
      componentKey: group.id,
      title: group.title,
      caption: '',
      icon: group.icon,
      isAffix: false,
      children: group.items.map(item => toShellNavigationTreeItem(item, item.path))
    });
  }

  return tree;
}

/**
 * 生成左侧菜单树；服务端有 children 时优先按树投影，否则按 Admin.NET 风格分组。
 */
export function buildShellNavigationTree(
  options: BuildShellNavigationOptions
): ShellNavigationTreeItem[] {
  const hasHierarchy = options.navigation.some(node => node.children.length > 0);
  if (hasHierarchy) {
    const tree = mapServerNavigationTree(options.navigation, options.translate);
    if (tree.length > 0) {
      return tree;
    }
  }

  const groups = buildShellNavigationGroups(options);
  return buildTreeFromNavigationGroups(groups);
}

/** 扁平化侧栏树，供标签页与搜索复用。 */
export function flattenShellNavigationTree(
  tree: ShellNavigationTreeItem[]
): ShellNavigationItem[] {
  const items: ShellNavigationItem[] = [];

  const walk = (nodes: ShellNavigationTreeItem[]): void => {
    for (const node of nodes) {
      if (!node.path.startsWith(SHELL_NAV_GROUP_PATH_PREFIX)) {
        items.push(node);
      }

      if (node.children.length > 0) {
        walk(node.children);
      }
    }
  };

  walk(tree);
  return items;
}

/** 根据当前路由展开包含激活页的父级目录。 */
export function resolveDefaultOpenedMenuPaths(
  tree: ShellNavigationTreeItem[],
  activePath: string
): string[] {
  const opened: string[] = [];

  const containsActivePath = (node: ShellNavigationTreeItem): boolean => {
    if (node.path === activePath) {
      return true;
    }

    return node.children.some(child => containsActivePath(child));
  };

  for (const node of tree) {
    if (node.children.length > 0 && containsActivePath(node)) {
      opened.push(node.path);
    }
  }

  return opened;
}

/** 生成面包屑分段：控制台 / 分组 / 当前页。 */
export function resolveNavigationBreadcrumb(
  tree: ShellNavigationTreeItem[],
  activePath: string,
  rootLabel: string
): string[] {
  const trail: string[] = [];

  const walk = (nodes: ShellNavigationTreeItem[]): boolean => {
    for (const node of nodes) {
      if (node.path === activePath) {
        trail.push(node.title);
        return true;
      }

      if (node.children.length > 0 && walk(node.children)) {
        trail.unshift(node.title);
        return true;
      }
    }

    return false;
  };

  if (!walk(tree)) {
    return [rootLabel];
  }

  return [rootLabel, ...trail];
}

/** 将扁平授权页列表转为无分组的侧栏树。 */
export function buildFlatShellNavigationTree(
  items: ShellNavigationItem[]
): ShellNavigationTreeItem[] {
  return items.map(item => toShellNavigationTreeItem(item, item.path));
}

/** 将导航页签化，保留标题、图标和 affix 语义。 */
function toShellTabItem(item: ShellNavigationItem): ShellTabItem {
  return {
    path: item.path,
    title: item.title,
    icon: item.icon,
    isAffix: item.isAffix
  };
}

/** 固定标签不可关闭，语义对齐 Art Design Pro `fixedTab`。 */
export function isShellTabClosable(tab: ShellTabItem): boolean {
  return !tab.isAffix;
}

/** 确保所有固定页始终出现在标签栏最前。 */
export function ensureAffixShellTabs(
  tabs: ShellTabItem[],
  navigation: ShellNavigationItem[]
): ShellTabItem[] {
  const affixNavigation = navigation.filter(item => item.isAffix);
  if (affixNavigation.length === 0) {
    return tabs;
  }

  const affixPaths = new Set(affixNavigation.map(item => item.path));
  const nonAffixTabs = tabs.filter(tab => !affixPaths.has(tab.path));
  const affixTabs = affixNavigation.map(item => {
    const existing = tabs.find(tab => tab.path === item.path);
    return existing
      ? { ...existing, isAffix: true }
      : toShellTabItem(item);
  });

  return [...affixTabs, ...nonAffixTabs];
}

/** 根据当前路径与授权导航生成标签页集合。 */
export function upsertShellTab(
  tabs: ShellTabItem[],
  navigation: ShellNavigationItem[],
  activePath: string
): ShellTabItem[] {
  const nextTabs = ensureAffixShellTabs(tabs, navigation);
  const active = navigation.find(item => item.path === activePath);
  if (!active) {
    return nextTabs;
  }

  if (nextTabs.some(tab => tab.path === active.path)) {
    return nextTabs;
  }

  return [...nextTabs, toShellTabItem(active)];
}

/** 关闭标签页后返回应激活的路径。 */
export function closeShellTab(
  tabs: ShellTabItem[],
  closingPath: string,
  activePath: string
): { tabs: ShellTabItem[]; nextPath: string } {
  const closingTab = tabs.find(tab => tab.path === closingPath);
  if (!closingTab || !isShellTabClosable(closingTab)) {
    return { tabs, nextPath: activePath };
  }

  const index = tabs.findIndex(tab => tab.path === closingPath);
  if (index < 0) {
    return { tabs, nextPath: activePath };
  }

  const nextTabs = tabs.filter(tab => tab.path !== closingPath);
  if (nextTabs.length === 0) {
    return { tabs, nextPath: activePath };
  }

  if (closingPath !== activePath) {
    return { tabs: nextTabs, nextPath: activePath };
  }

  const fallback = nextTabs[Math.max(0, index - 1)] ?? nextTabs[0];
  return { tabs: nextTabs, nextPath: fallback?.path ?? '/' };
}

export type ShellTabCloseScope = 'current' | 'left' | 'right' | 'other' | 'all';

/** 按右键菜单语义批量关闭标签页。 */
export function closeShellTabs(
  tabs: ShellTabItem[],
  scope: ShellTabCloseScope,
  targetPath: string,
  activePath: string
): { tabs: ShellTabItem[]; nextPath: string } {
  if (tabs.length === 0) {
    return { tabs, nextPath: activePath };
  }

  if (scope === 'current') {
    return closeShellTab(tabs, targetPath, activePath);
  }

  const targetIndex = tabs.findIndex(tab => tab.path === targetPath);
  if (targetIndex < 0) {
    return { tabs, nextPath: activePath };
  }

  let nextTabs = tabs;
  if (scope === 'left') {
    nextTabs = tabs.filter((tab, index) =>
      index >= targetIndex || !isShellTabClosable(tab));
  } else if (scope === 'right') {
    nextTabs = tabs.filter((tab, index) =>
      index <= targetIndex || !isShellTabClosable(tab));
  } else if (scope === 'other') {
    nextTabs = tabs.filter(tab =>
      tab.path === targetPath || !isShellTabClosable(tab));
  } else if (scope === 'all') {
    const affixTabs = tabs.filter(tab => tab.isAffix);
    nextTabs = affixTabs.length > 0
      ? affixTabs
      : [tabs[targetIndex] ?? tabs[0]!];
  }

  if (nextTabs.length === 0) {
    nextTabs = [tabs[targetIndex] ?? tabs[0]!];
  }

  const nextPath = nextTabs.some(tab => tab.path === activePath)
    ? activePath
    : nextTabs[Math.min(targetIndex, nextTabs.length - 1)]?.path
      ?? nextTabs[0]?.path
      ?? '/';

  return { tabs: nextTabs, nextPath };
}
