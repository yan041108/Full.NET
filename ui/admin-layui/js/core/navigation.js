const localNavigation = new Map([
  ['overview', {
    routeName: 'overview',
    path: '/',
    view: 'overview',
    iconClass: 'layui-icon-console'
  }],
  ['tenant-context', {
    routeName: 'tenant-context',
    path: '/tenant-context',
    view: 'tenant-context',
    iconClass: 'layui-icon-group'
  }]
]);

/** 确认服务端导航只引用当前 Layui 版本显式发布的本地视图。 */
export function isSupportedNavigationTree(navigation) {
  return navigation.every(node => {
    const local = localNavigation.get(node.componentKey);
    return local !== undefined
      && local.routeName === node.routeName
      && local.path === node.path
      && isSupportedNavigationTree(node.children);
  });
}

/** 按树顺序生成平铺导航，不修改源数据。 */
export function flattenNavigation(navigation) {
  const result = [];
  for (const node of navigation) {
    result.push(node, ...flattenNavigation(node.children));
  }

  return result;
}

/** 查找与 Hash 路径精确匹配的已授权本地导航。 */
export function findNavigationByPath(navigation, path) {
  return flattenNavigation(navigation).find(node => node.path === path);
}

/** 获取组件键对应的固定本地视图键。 */
export function localViewFor(componentKey) {
  return localNavigation.get(componentKey)?.view;
}

/** 使用安全 DOM API 呈现导航，服务端文本不会进入 HTML 解析器。 */
export function renderNavigation(container, navigation, activePath) {
  const group = document.createElement('span');
  group.className = 'fn-nav__group';
  group.textContent = '管理域';
  const fragment = document.createDocumentFragment();
  fragment.append(group);

  flattenNavigation(navigation).forEach((node, index) => {
    const local = localNavigation.get(node.componentKey);
    if (!local) {
      return;
    }

    const link = document.createElement('a');
    link.href = `#${node.path}`;
    link.dataset.route = node.path;
    link.classList.toggle('is-active', node.path === activePath);

    const icon = document.createElement('i');
    icon.className = `layui-icon ${local.iconClass}`;
    icon.setAttribute('aria-hidden', 'true');
    const title = document.createElement('span');
    title.textContent = node.title;
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
