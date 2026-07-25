import { flattenNavigation, localNavigationFor } from './navigation.js';

const groupRules = [
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

function toNavigationItem(node, t) {
  const local = localNavigationFor(node.componentKey);
  if (!local) {
    return null;
  }

  return {
    path: node.path,
    componentKey: node.componentKey,
    title: local.routeName === node.routeName ? t(local.titleKey) : node.title,
    iconClass: local.iconClass
  };
}

/** 将授权导航树转为可渲染的扁平菜单项。 */
export function buildFlatNavigationItems(navigation, t) {
  return flattenNavigation(navigation).flatMap(node => {
    const item = toNavigationItem(node, t);
    return item ? [item] : [];
  });
}

function buildGroupsFromTree(navigation, t) {
  const groups = [];

  for (const node of navigation) {
    const childItems = node.children
      .map(child => toNavigationItem(child, t))
      .filter(item => item !== null);
    const selfItem = toNavigationItem(node, t);
    if (childItems.length === 0) {
      continue;
    }

    groups.push({
      id: node.id,
      title: selfItem?.title ?? node.title,
      iconClass: selfItem?.iconClass ?? childItems[0]?.iconClass ?? 'layui-icon-app',
      items: selfItem ? [selfItem, ...childItems] : childItems
    });
  }

  return groups;
}

function buildGroupsFromFlatItems(items, t) {
  const grouped = new Map(
    groupRules.map(rule => [rule.id, {
      id: rule.id,
      title: t(rule.titleKey),
      iconClass: 'layui-icon-app',
      items: []
    }])
  );

  for (const item of items) {
    const rule = groupRules.find(candidate => candidate.matchPath(item.path));
    const groupId = rule?.id ?? 'platform';
    const group = grouped.get(groupId);
    if (!group) {
      continue;
    }

    group.items.push(item);
    if (group.items.length === 1) {
      group.iconClass = item.iconClass;
    }
  }

  return [...grouped.values()].filter(group => group.items.length > 0);
}

/** 为混合/双栏布局生成一级分组。 */
export function buildShellNavigationGroups(navigation, t) {
  const flatItems = buildFlatNavigationItems(navigation, t);
  const hasHierarchy = navigation.some(node => node.children.length > 0);
  if (hasHierarchy) {
    const treeGroups = buildGroupsFromTree(navigation, t);
    if (treeGroups.length > 0) {
      return treeGroups;
    }
  }

  const inferredGroups = buildGroupsFromFlatItems(flatItems, t);
  if (inferredGroups.length > 0) {
    return inferredGroups;
  }

  return [{
    id: 'platform',
    title: t('shell.navGroup.platform'),
    iconClass: flatItems[0]?.iconClass ?? 'layui-icon-app',
    items: flatItems
  }];
}

/** 根据当前路径解析应高亮的一级菜单分组。 */
export function resolveActiveGroupId(groups, path) {
  const matched = groups.find(group =>
    group.items.some(item => item.path === path)
  );
  return matched?.id ?? groups[0]?.id ?? 'platform';
}
