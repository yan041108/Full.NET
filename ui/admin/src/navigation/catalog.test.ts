import { describe, expect, it } from 'vitest';
import type { NavigationNode } from '@fullnet/client-contracts';
import {
  flattenNavigation,
  isSupportedNavigationTree,
  localNavigationFor
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
      createNode('tenant-context'),
      createNode('api-keys', { path: '/identity/api-keys' })
    ])).toBe(true);
    expect(isSupportedNavigationTree([
      createNode('remote-script')
    ])).toBe(false);
    expect(isSupportedNavigationTree([
      createNode('overview', { path: '/remote' })
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

  it('只从本地可信目录返回导航消息键', () => {
    expect(localNavigationFor('overview')).toMatchObject({
      titleKey: 'navigation.overview.title',
      captionKey: 'navigation.overview.caption'
    });
    expect(localNavigationFor('remote-script')).toBeUndefined();
    expect(localNavigationFor('api-keys')).toMatchObject({
      path: '/identity/api-keys',
      titleKey: 'navigation.apiKeys.title',
      captionKey: 'navigation.apiKeys.caption'
    });
    expect(localNavigationFor('code-generation-previews')).toMatchObject({
      path: '/code-generation/previews',
      titleKey: 'navigation.codeGenerationPreviews.title',
      captionKey: 'navigation.codeGenerationPreviews.caption'
    });
    expect(localNavigationFor('host-document-items')).toMatchObject({
      path: '/document/host-items',
      titleKey: 'navigation.hostDocumentItems.title',
      captionKey: 'navigation.hostDocumentItems.caption'
    });
    expect(localNavigationFor('workflow-todos')).toMatchObject({
      path: '/workflow/todos',
      titleKey: 'navigation.workflowTodos.title',
      captionKey: 'navigation.workflowTodos.caption'
    });
    expect(localNavigationFor('workflow-cc')).toMatchObject({
      path: '/workflow/cc',
      titleKey: 'navigation.workflowCc.title',
      captionKey: 'navigation.workflowCc.caption'
    });
    expect(localNavigationFor('workflow-definitions')).toMatchObject({
      path: '/workflow/definitions',
      titleKey: 'navigation.workflowDefinitions.title',
      captionKey: 'navigation.workflowDefinitions.caption'
    });
    expect(localNavigationFor('workflow-forms')).toMatchObject({
      path: '/workflow/forms',
      titleKey: 'navigation.workflowForms.title',
      captionKey: 'navigation.workflowForms.caption'
    });
    expect(localNavigationFor('workflow-instances')).toMatchObject({
      path: '/workflow/instances',
      titleKey: 'navigation.workflowInstances.title',
      captionKey: 'navigation.workflowInstances.caption'
    });
    expect(localNavigationFor('notification-templates')).toMatchObject({
      path: '/notifications/templates',
      titleKey: 'navigation.notificationTemplates.title',
      captionKey: 'navigation.notificationTemplates.caption'
    });
  });
});
