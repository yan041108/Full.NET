import { describe, expect, it } from 'vitest';
import { Grid } from '@element-plus/icons-vue';
import type { NavigationNode } from '@fullnet/client-contracts';
import {
  buildShellNavigation,
  buildShellNavigationGroups,
  closeShellTab,
  resolveActiveGroupId,
  resolveShellIcon,
  upsertShellTab
} from './fullNetShellAdapter';

const navigation: NavigationNode[] = [{
  id: 'overview',
  parentId: null,
  routeName: 'overview',
  path: '/',
  componentKey: 'overview',
  title: 'SERVER TITLE',
  caption: 'SERVER CAPTION',
  icon: 'dashboard',
  order: 10,
  requiredPermission: 'platform.dashboard.read',
  children: []
}, {
  id: 'tenants',
  parentId: null,
  routeName: 'tenant-management',
  path: '/tenants',
  componentKey: 'tenants',
  title: '租户',
  caption: '租户管理',
  icon: 'building',
  order: 20,
  requiredPermission: 'tenancy.tenants.read',
  children: []
}];

describe('fullNetShellAdapter', () => {
  it('只暴露白名单导航并优先使用本地文案', () => {
    const items = buildShellNavigation({
      navigation,
      translate: key => (key === 'navigation.overview.title' ? '工作台' : key)
    });

    expect(items.map(item => item.path)).toEqual(['/', '/tenants']);
    expect(items[0]?.title).toBe('工作台');
    expect(items[1]?.title).toBe('navigation.tenants.title');
  });

  it('未知 componentKey 会被忽略', () => {
    const items = buildShellNavigation({
      navigation: [...navigation, {
        id: 'unknown',
        parentId: null,
        routeName: 'unknown',
        path: '/unknown',
        componentKey: 'not-published',
        title: '未知',
        caption: '未知',
        icon: 'dashboard',
        order: 30,
        requiredPermission: 'platform.dashboard.read',
        children: []
      }],
      translate: key => key
    });

    expect(items.map(item => item.path)).toEqual(['/', '/tenants']);
  });

  it('未知图标回退到默认图标组件', () => {
    expect(resolveShellIcon('missing-icon')).toBe(Grid);
  });

  it('标签页只跟踪授权路径', () => {
    const navigationItems = buildShellNavigation({
      navigation,
      translate: key => key
    });
    const first = upsertShellTab([], navigationItems, '/');
    const second = upsertShellTab(first, navigationItems, '/tenants');

    expect(second).toEqual([
      { path: '/', title: 'navigation.overview.title' },
      { path: '/tenants', title: 'navigation.tenants.title' }
    ]);
  });

  it('关闭当前标签页时回退到相邻授权路径', () => {
    const tabs = [
      { path: '/', title: '工作台' },
      { path: '/tenants', title: '租户' }
    ];

    expect(closeShellTab(tabs, '/tenants', '/tenants')).toEqual({
      tabs: [{ path: '/', title: '工作台' }],
      nextPath: '/'
    });
  });

  it('扁平导航会按路径前缀推断一级分组', () => {
    const groups = buildShellNavigationGroups({
      navigation,
      translate: key => {
        if (key === 'shell.navGroup.platform') {
          return '工作台';
        }

        if (key === 'shell.navGroup.tenancy') {
          return '租户';
        }

        return key;
      }
    });

    expect(groups.map(group => group.id)).toEqual(['platform', 'tenancy']);
    expect(groups[0]?.items.map(item => item.path)).toEqual(['/']);
    expect(groups[1]?.items.map(item => item.path)).toEqual(['/tenants']);
  });

  it('服务端树含 children 时优先按树分组', () => {
    const groups = buildShellNavigationGroups({
      navigation: [{
        id: 'workspace',
        parentId: null,
        routeName: 'overview',
        path: '/',
        componentKey: 'overview',
        title: '工作台',
        caption: '概览',
        icon: 'dashboard',
        order: 10,
        requiredPermission: 'platform.dashboard.read',
        children: [{
          id: 'tenants',
          parentId: 'workspace',
          routeName: 'tenant-management',
          path: '/tenants',
          componentKey: 'tenants',
          title: '租户',
          caption: '租户管理',
          icon: 'building',
          order: 20,
          requiredPermission: 'tenancy.tenants.read',
          children: []
        }]
      }],
      translate: key => key
    });

    expect(groups).toHaveLength(1);
    expect(groups[0]?.id).toBe('workspace');
    expect(groups[0]?.items.map(item => item.path)).toEqual(['/', '/tenants']);
  });

  it('根据当前路径解析一级分组', () => {
    const groups = buildShellNavigationGroups({
      navigation,
      translate: key => key
    });

    expect(resolveActiveGroupId(groups, '/tenants')).toBe('tenancy');
    expect(resolveActiveGroupId(groups, '/')).toBe('platform');
  });
});
