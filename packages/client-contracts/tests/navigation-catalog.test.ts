import { describe, expect, it } from 'vitest';
import type { NavigationNode } from '../src/authorization';
import { createAdminNavigationCatalog } from '../src/navigation-catalog';

function createNode(
  componentKey: string,
  overrides: Partial<NavigationNode> = {}
): NavigationNode {
  return {
    id: componentKey,
    parentId: null,
    routeName: componentKey,
    path: componentKey === 'overview' ? '/' : `/${componentKey}`,
    componentKey,
    title: componentKey,
    caption: componentKey,
    icon: 'dashboard',
    order: 10,
    requiredPermission: 'platform.dashboard.read',
    children: [],
    ...overrides
  };
}

describe('headless 管理端导航目录', () => {
  const catalog = createAdminNavigationCatalog();

  it('只接受已发布的 componentKey、routeName 与 path 组合', () => {
    expect(catalog.isSupportedNavigationTree([
      createNode('overview'),
      createNode('tenant-context'),
      createNode('api-keys', { path: '/identity/api-keys' })
    ])).toBe(true);
    expect(catalog.isSupportedNavigationTree([
      createNode('remote-script')
    ])).toBe(false);
    expect(catalog.isSupportedNavigationTree([
      createNode('overview', { path: '/remote' })
    ])).toBe(false);
  });

  it('按树顺序扁平化导航且不修改源数据', () => {
    const child = createNode('tenant-context', { parentId: 'overview' });
    const tree = [createNode('overview', { children: [child] })];
    const before = structuredClone(tree);

    expect(catalog.flattenNavigation(tree).map(node => node.id)).toEqual([
      'overview',
      'tenant-context'
    ]);
    expect(tree).toEqual(before);
  });
});
