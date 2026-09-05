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

  it('发布租户职级管理路由', () => {
    expect(catalog.localNavigationFor('org-position-levels')).toEqual({
      componentKey: 'org-position-levels',
      routeName: 'org-position-levels',
      path: '/organization/position-levels'
    });
    expect(catalog.localNavigationFor('code-generation-previews')).toEqual({
      componentKey: 'code-generation-previews',
      routeName: 'code-generation-previews',
      path: '/code-generation/previews'
    });
    expect(catalog.localNavigationFor('host-job-schedules')).toEqual({
      componentKey: 'host-job-schedules',
      routeName: 'host-job-schedules',
      path: '/jobs/host-schedules'
    });
    expect(catalog.localNavigationFor('code-generation-templates')).toEqual({
      componentKey: 'code-generation-templates',
      routeName: 'code-generation-templates',
      path: '/code-generation/templates'
    });
    expect(catalog.localNavigationFor('serial-number-rules')).toEqual({
      componentKey: 'serial-number-rules',
      routeName: 'serial-number-rules',
      path: '/serial-numbers/rules'
    });
    expect(catalog.localNavigationFor('host-document-items')).toEqual({
      componentKey: 'host-document-items',
      routeName: 'host-document-items',
      path: '/document/host-items'
    });
    expect(catalog.localNavigationFor('document-categories')).toEqual({
      componentKey: 'document-categories',
      routeName: 'document-categories',
      path: '/document/categories'
    });
    expect(catalog.localNavigationFor('observability-log-files')).toEqual({
      componentKey: 'observability-log-files',
      routeName: 'observability-log-files',
      path: '/observability/log-files'
    });
    expect(catalog.localNavigationFor('workflow-todos')).toEqual({
      componentKey: 'workflow-todos',
      routeName: 'workflow-todos',
      path: '/workflow/todos'
    });
    expect(catalog.localNavigationFor('workflow-definitions')).toEqual({
      componentKey: 'workflow-definitions',
      routeName: 'workflow-definitions',
      path: '/workflow/definitions'
    });
    expect(catalog.localNavigationFor('workflow-recovery-tasks')).toEqual({
      componentKey: 'workflow-recovery-tasks',
      routeName: 'workflow-recovery-tasks',
      path: '/workflow/recovery-tasks'
    });
    expect(catalog.localNavigationFor('notification-templates')).toEqual({
      componentKey: 'notification-templates',
      routeName: 'notification-templates',
      path: '/notifications/templates'
    });
  });

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
    expect(catalog.isSupportedNavigationTree([
      {
        ...createNode('layout', {
          id: 'module-identity',
          routeName: 'module-identity',
          path: '/modules/identity'
        }),
        children: [
          createNode('users', {
            id: 'users',
            parentId: 'module-identity',
            routeName: 'users',
            path: '/identity/users'
          })
        ]
      }
    ])).toBe(true);
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
