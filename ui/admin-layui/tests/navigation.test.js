import { describe, expect, it, vi } from 'vitest';
import {
  applyPermissionVisibility,
  flattenNavigation,
  isSupportedNavigationTree,
  localNavigationFor,
  renderNavigation
} from '../js/core/navigation.js';
import { translate } from '@fullnet/admin-i18n';

describe('Layui 本地导航与 DOM 安全', () => {
  it('只接受本地组件、路由名和路径的精确映射', () => {
    expect(isSupportedNavigationTree([
      navigationNode('overview'),
      navigationNode('tenant-context'),
      {
        ...navigationNode('overview'),
        id: 'api-keys',
        routeName: 'api-keys',
        path: '/identity/api-keys',
        componentKey: 'api-keys',
        requiredPermission: 'identity.api_keys.read'
      }
    ])).toBe(true);
    expect(isSupportedNavigationTree([
      { ...navigationNode('overview'), path: '/remote' }
    ])).toBe(false);
    expect(isSupportedNavigationTree([
      { ...navigationNode('overview'), componentKey: 'remote-script' }
    ])).toBe(false);
  });

  it('使用本地消息键和安全 DOM API 呈现导航', () => {
    const container = document.createElement('nav');
    const createElement = vi.spyOn(document, 'createElement');
    const node = {
      ...navigationNode('overview'),
      title: '<img src=x onerror=alert(1)>'
    };

    renderNavigation(
      container,
      [node],
      '/',
      (key, parameters) => translate('zh-CN', key, parameters)
    );

    expect(createElement).toHaveBeenCalled();
    expect(container.textContent).toContain('工作台');
    expect(container.textContent).not.toContain('<img src=x onerror=alert(1)>');
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('a')?.getAttribute('data-route')).toBe('/');
    expect(container.querySelector('a')?.getAttribute('aria-current')).toBe('page');
    expect(localNavigationFor('remote-script')).toBeUndefined();
    expect(localNavigationFor('api-keys')).toMatchObject({
      view: 'api-keys',
      path: '/identity/api-keys',
      titleKey: 'navigation.apiKeys.title'
    });
    expect(localNavigationFor('code-generation-previews')).toMatchObject({
      view: 'code-generation-previews',
      path: '/code-generation/previews',
      titleKey: 'navigation.codeGenerationPreviews.title'
    });
  });

  it('权限元素只接受完整且区分大小写的权限码', () => {
    const root = document.createElement('div');
    for (const permission of [
      'tenancy.tenants.switch',
      'Tenancy.Tenants.Switch',
      'tenancy.tenants'
    ]) {
      const button = document.createElement('button');
      button.dataset.permission = permission;
      root.append(button);
    }

    applyPermissionVisibility(root, ['tenancy.tenants.switch']);

    expect([...root.querySelectorAll('button')].map(button => button.hidden))
      .toEqual([false, true, true]);
  });

  it('平铺导航时保持服务端树顺序且不修改源数据', () => {
    const child = { ...navigationNode('tenant-context'), parentId: 'overview' };
    const tree = [{ ...navigationNode('overview'), children: [child] }];
    const before = structuredClone(tree);

    expect(flattenNavigation(tree).map(node => node.id)).toEqual([
      'overview',
      'tenant-context'
    ]);
    expect(tree).toEqual(before);
  });
});

function navigationNode(componentKey) {
  const tenant = componentKey === 'tenant-context';
  return {
    id: componentKey,
    parentId: null,
    routeName: componentKey,
    path: tenant ? '/tenant-context' : '/',
    componentKey,
    title: tenant ? '租户上下文' : '工作台',
    caption: tenant ? '进入租户或返回 Host' : '平台运行概览',
    icon: tenant ? 'building' : 'dashboard',
    order: tenant ? 20 : 10,
    requiredPermission: tenant
      ? 'tenancy.tenants.read'
      : 'platform.dashboard.read',
    children: []
  };
}
