/** 从 Host navigation API 扁平化可测菜单页。 */
export function flattenNavigation(nodes, output = []) {
  for (const node of nodes ?? []) {
    const isLayout =
      node.componentKey === 'layout'
      || (node.path?.startsWith('/domains/') ?? false)
      || (node.path?.startsWith('/modules/') ?? false);
    const isMenu =
      node.menuType === 'Menu'
      || (!isLayout && node.menuType !== 'Button' && node.menuType !== 'Directory');
    if (isMenu && node.path && !node.path.startsWith('__group__')) {
      output.push({
        path: node.path,
        title: node.title,
        routeName: node.routeName,
        componentKey: node.componentKey,
        menuType: node.menuType
      });
    }
    if (node.children?.length) {
      flattenNavigation(node.children, output);
    }
  }
  return output;
}

export function uniqueByPath(items) {
  const seen = new Set();
  return items.filter(item => {
    if (seen.has(item.path)) {
      return false;
    }
    seen.add(item.path);
    return true;
  });
}

/** 扁平化并附带最近 domain 目录标题（/domains/* 节点）。 */
export function flattenNavigationWithDomain(nodes, parentDomain = '其他', output = []) {
  for (const node of nodes ?? []) {
    let domain = parentDomain;
    if (node.path?.startsWith('/domains/')) {
      domain = node.title ?? node.routeName ?? parentDomain;
    }
    const isLayout =
      node.componentKey === 'layout'
      || (node.path?.startsWith('/domains/') ?? false)
      || (node.path?.startsWith('/modules/') ?? false);
    const isMenu =
      node.menuType === 'Menu'
      || (!isLayout && node.menuType !== 'Button' && node.menuType !== 'Directory');
    if (isMenu && node.path && !node.path.startsWith('__group__')) {
      output.push({
        path: node.path,
        title: node.title,
        routeName: node.routeName,
        componentKey: node.componentKey,
        menuType: node.menuType,
        domain
      });
    }
    if (node.children?.length) {
      flattenNavigationWithDomain(node.children, domain, output);
    }
  }
  return output;
}
