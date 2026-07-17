import { describe, expect, it } from 'vitest';
import type { NavigationNode } from '@fullnet/client-contracts';
import {
  flattenNavigation,
  isSupportedNavigationTree
} from './catalog';

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

describe('Vue 本地导航目录', () => {
  it('只接受 Vue 已发布的本地组件键', () => {
    expect(isSupportedNavigationTree([
      createNode('overview'),
      createNode('tenant-context')
    ])).toBe(true);
    expect(isSupportedNavigationTree([
      createNode('remote-script')
    ])).toBe(false);
  });

  it('按树顺序扁平化导航且不修改源数据', () => {
    const child = createNode('tenant-context', {
      parentId: 'overview'
    });
    const tree = [createNode('overview', { children: [child] })];
    const before = structuredClone(tree);

    expect(flattenNavigation(tree).map(node => node.id)).toEqual([
      'overview',
      'tenant-context'
    ]);
    expect(tree).toEqual(before);
  });
});
