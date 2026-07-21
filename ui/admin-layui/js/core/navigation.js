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
  ['users', {
    view: 'users',
    iconClass: 'layui-icon-user',
    titleKey: 'navigation.users.title',
    captionKey: 'navigation.users.caption'
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
  ['super-administrators', {
    view: 'super-administrators',
    iconClass: 'layui-icon-auz',
    titleKey: 'navigation.superAdministrators.title',
    captionKey: 'navigation.superAdministrators.caption'
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
