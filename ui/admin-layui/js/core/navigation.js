import { createAdminNavigationCatalog } from '@fullnet/client-contracts';

const navigationCatalog = createAdminNavigationCatalog();

const presentation = new Map([
  ['overview', {
    view: 'overview',
    iconClass: 'layui-icon-console',
    titleKey: 'navigation.overview.title',
    captionKey: 'navigation.overview.caption'
  }],
  ['tenant-context', {
    view: 'tenant-context',
    iconClass: 'layui-icon-group',
    titleKey: 'navigation.tenantContext.title',
    captionKey: 'navigation.tenantContext.caption'
  }],
  ['tenants', {
    view: 'tenants',
    iconClass: 'layui-icon-app',
    titleKey: 'navigation.tenants.title',
    captionKey: 'navigation.tenants.caption'
  }],
  ['tenant-packages', {
    view: 'tenant-packages',
    iconClass: 'layui-icon-template-1',
    titleKey: 'navigation.tenantPackages.title',
    captionKey: 'navigation.tenantPackages.caption'
  }],
  ['users', {
    view: 'users',
    iconClass: 'layui-icon-user',
    titleKey: 'navigation.users.title',
    captionKey: 'navigation.users.caption'
  }],
  ['online-sessions', {
    view: 'online-sessions',
    iconClass: 'layui-icon-log',
    titleKey: 'navigation.onlineSessions.title',
    captionKey: 'navigation.onlineSessions.caption'
  }],
  ['roles', {
    view: 'roles',
    iconClass: 'layui-icon-group',
    titleKey: 'navigation.roles.title',
    captionKey: 'navigation.roles.caption'
  }],
  ['menus', {
    view: 'menus',
    iconClass: 'layui-icon-app',
    titleKey: 'navigation.menus.title',
    captionKey: 'navigation.menus.caption'
  }],
  ['org-units', {
    view: 'org-units',
    iconClass: 'layui-icon-home',
    titleKey: 'navigation.orgUnits.title',
    captionKey: 'navigation.orgUnits.caption'
  }],
  ['org-user-units', {
    view: 'org-user-units',
    iconClass: 'layui-icon-user',
    titleKey: 'navigation.orgUserUnits.title',
    captionKey: 'navigation.orgUserUnits.caption'
  }],
  ['org-positions', {
    view: 'org-positions',
    iconClass: 'layui-icon-username',
    titleKey: 'navigation.orgPositions.title',
    captionKey: 'navigation.orgPositions.caption'
  }],
  ['org-user-positions', {
    view: 'org-user-positions',
    iconClass: 'layui-icon-user',
    titleKey: 'navigation.orgUserPositions.title',
    captionKey: 'navigation.orgUserPositions.caption'
  }],
  ['super-administrators', {
    view: 'super-administrators',
    iconClass: 'layui-icon-auz',
    titleKey: 'navigation.superAdministrators.title',
    captionKey: 'navigation.superAdministrators.caption'
  }],
  ['dict-types', {
    view: 'dict-types',
    iconClass: 'layui-icon-read',
    titleKey: 'navigation.dictTypes.title',
    captionKey: 'navigation.dictTypes.caption'
  }],
  ['config-entries', {
    view: 'config-entries',
    iconClass: 'layui-icon-set',
    titleKey: 'navigation.configEntries.title',
    captionKey: 'navigation.configEntries.caption'
  }],
  ['enum-catalogs', {
    view: 'enum-catalogs',
    iconClass: 'layui-icon-template',
    titleKey: 'navigation.enumCatalogs.title',
    captionKey: 'navigation.enumCatalogs.caption'
  }],
  ['host-files', {
    view: 'host-files',
    iconClass: 'layui-icon-file',
    titleKey: 'navigation.hostFiles.title',
    captionKey: 'navigation.hostFiles.caption'
  }],
  ['host-announcements', {
    view: 'host-announcements',
    iconClass: 'layui-icon-notice',
    titleKey: 'navigation.hostAnnouncements.title',
    captionKey: 'navigation.hostAnnouncements.caption'
  }],
  ['inbox-messages', {
    view: 'inbox-messages',
    iconClass: 'layui-icon-dialogue',
    titleKey: 'navigation.inboxMessages.title',
    captionKey: 'navigation.inboxMessages.caption'
  }],
  ['host-jobs', {
    view: 'host-jobs',
    iconClass: 'layui-icon-time',
    titleKey: 'navigation.hostJobs.title',
    captionKey: 'navigation.hostJobs.caption'
  }],
  ['access-logs', {
    view: 'access-logs',
    iconClass: 'layui-icon-file',
    titleKey: 'navigation.accessLogs.title',
    captionKey: 'navigation.accessLogs.caption'
  }],
  ['operation-logs', {
    view: 'operation-logs',
    iconClass: 'layui-icon-edit',
    titleKey: 'navigation.operationLogs.title',
    captionKey: 'navigation.operationLogs.caption'
  }],
  ['exception-logs', {
    view: 'exception-logs',
    iconClass: 'layui-icon-close',
    titleKey: 'navigation.exceptionLogs.title',
    captionKey: 'navigation.exceptionLogs.caption'
  }]
]);

/** 返回组件键对应的本地可信导航定义，未知键始终拒绝。 */
export function localNavigationFor(componentKey) {
  const entry = navigationCatalog.localNavigationFor(componentKey);
  const local = presentation.get(componentKey);
  if (!entry || !local) {
    return undefined;
  }

  return {
    routeName: entry.routeName,
    path: entry.path,
    ...local
  };
}

/** 确认服务端导航只引用当前 Layui 版本显式发布的本地视图。 */
export const isSupportedNavigationTree = navigationCatalog.isSupportedNavigationTree;

/** 按树顺序生成平铺导航，不修改源数据。 */
export const flattenNavigation = navigationCatalog.flattenNavigation;

/** 查找与 Hash 路径精确匹配的已授权本地导航。 */
export function findNavigationByPath(navigation, path) {
  return flattenNavigation(navigation).find(node => node.path === path);
}

/** 获取组件键对应的固定本地视图键。 */
export function localViewFor(componentKey) {
  return presentation.get(componentKey)?.view;
}

/** 使用安全 DOM API 呈现导航，服务端文本不会进入 HTML 解析器。 */
export function renderNavigation(container, navigation, activePath, t) {
  const group = document.createElement('span');
  group.className = 'fn-nav__group';
  group.textContent = t('shell.managementDomain');
  const fragment = document.createDocumentFragment();
  fragment.append(group);

  flattenNavigation(navigation).forEach((node, index) => {
    const local = localNavigationFor(node.componentKey);
    if (!local) {
      return;
    }

    const link = document.createElement('a');
    link.href = `#${node.path}`;
    link.dataset.route = node.path;
    link.classList.toggle('is-active', node.path === activePath);
    if (node.path === activePath) {
      link.setAttribute('aria-current', 'page');
    }

    const icon = document.createElement('i');
    icon.className = `layui-icon ${local.iconClass}`;
    icon.setAttribute('aria-hidden', 'true');
    const useCatalogTitle = local.routeName === node.routeName;
    const title = document.createElement('span');
    title.textContent = useCatalogTitle ? t(local.titleKey) : node.title;
    const order = document.createElement('em');
    order.textContent = String(index + 1).padStart(2, '0');
    link.append(icon, title, order);
    fragment.append(link);
  });

  container.replaceChildren(fragment);
}

/** 按完整权限码控制声明式元素，只影响可见性而不替代服务端授权。 */
export function applyPermissionVisibility(root, permissions) {
  const granted = new Set(permissions);
  root.querySelectorAll('[data-permission]').forEach((element) => {
    element.hidden = !granted.has(element.dataset.permission);
  });
}
