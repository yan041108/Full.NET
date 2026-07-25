/** 在已授权导航白名单内执行本地搜索，禁止动态路径解析。 */
export function filterShellNavigation(navigation, query) {
  const normalized = query.trim().toLowerCase();
  if (!normalized) {
    return [...navigation];
  }

  return navigation.filter(item => {
    const caption = item.caption ?? '';
    const componentKey = item.componentKey ?? '';
    return item.title.toLowerCase().includes(normalized)
      || caption.toLowerCase().includes(normalized)
      || item.path.toLowerCase().includes(normalized)
      || componentKey.toLowerCase().includes(normalized);
  });
}
