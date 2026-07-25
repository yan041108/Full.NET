import type { ShellNavigationItem } from './fullNetShellAdapter';

/** 在已授权导航白名单内执行本地搜索，禁止动态路径解析。 */
export function filterShellNavigation(
  navigation: readonly ShellNavigationItem[],
  query: string
): ShellNavigationItem[] {
  const normalized = query.trim().toLowerCase();
  if (!normalized) {
    return [...navigation];
  }

  return navigation.filter(item =>
    item.title.toLowerCase().includes(normalized)
    || item.caption.toLowerCase().includes(normalized)
    || item.path.toLowerCase().includes(normalized)
    || item.componentKey.toLowerCase().includes(normalized)
  );
}
