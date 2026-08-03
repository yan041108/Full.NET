import { describe, expect, it } from 'vitest';
import { Grid } from '@element-plus/icons-vue';
import type { NavigationNode } from '@fullnet/client-contracts';
import {
  buildShellNavigation,
  buildShellNavigationGroups,
  buildShellNavigationTree,
  closeShellTab,
  closeShellTabs,
  resolveActiveGroupId,
  resolveDefaultOpenedMenuPaths,
  resolveNavigationBreadcrumb,
  resolveShellIcon,
  SHELL_NAV_GROUP_PATH_PREFIX,
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

  it('翻译函数异常时回退服务端标题', () => {
    const items = buildShellNavigation({
      navigation: [{
        id: 'overview',
        parentId: null,
        routeName: 'overview',
        path: '/',
        componentKey: 'overview',
        title: '服务端工作台',
        caption: '服务端概览',
        icon: 'dashboard',
        order: 10,
        requiredPermission: 'platform.dashboard.read',
        children: []
      }],
      translate: () => {
        throw new Error('translate unavailable');
      }
    });

    expect(items).toEqual([{
      path: '/',
      routeName: 'overview',
      componentKey: 'overview',
      title: '服务端工作台',
      caption: '服务端概览',
      icon: expect.anything()
    }]);
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
      { path: '/', title: 'navigation.overview.title', icon: expect.anything() },
      { path: '/tenants', title: 'navigation.tenants.title', icon: expect.anything() }
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

  it('扁平导航会生成二级侧栏树', () => {
    const groupedNavigation: NavigationNode[] = [
      ...navigation,
      {
        id: 'tenant-context',
        parentId: null,
        routeName: 'tenant-context',
        path: '/tenant-context',
        componentKey: 'tenant-context',
        title: '租户上下文',
        caption: '租户上下文',
        icon: 'building',
        order: 15,
        requiredPermission: 'tenancy.tenants.read',
        children: []
      },
      {
        id: 'tenant-packages',
        parentId: null,
        routeName: 'tenant-packages',
        path: '/tenant-packages',
        componentKey: 'tenant-packages',
        title: '租户套餐',
        caption: '租户套餐',
        icon: 'building',
        order: 25,
        requiredPermission: 'tenancy.tenants.read',
        children: []
      }
    ];
    const tree = buildShellNavigationTree({
      navigation: groupedNavigation,
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

    const platformGroup = tree.find(item =>
      item.path === `${SHELL_NAV_GROUP_PATH_PREFIX}platform`
    );
    const tenancyGroup = tree.find(item =>
      item.path === `${SHELL_NAV_GROUP_PATH_PREFIX}tenancy`
    );

    expect(platformGroup?.children.map(item => item.path)).toEqual([
      '/',
      '/tenant-context'
    ]);
    expect(tenancyGroup?.children.map(item => item.path)).toEqual([
      '/tenants',
      '/tenant-packages'
    ]);
  });

  it('会展开包含当前路由的父级目录', () => {
    const groupedNavigation: NavigationNode[] = [
      ...navigation,
      {
        id: 'tenant-packages',
        parentId: null,
        routeName: 'tenant-packages',
        path: '/tenant-packages',
        componentKey: 'tenant-packages',
        title: '租户套餐',
        caption: '租户套餐',
        icon: 'building',
        order: 25,
        requiredPermission: 'tenancy.tenants.read',
        children: []
      }
    ];
    const tree = buildShellNavigationTree({
      navigation: groupedNavigation,
      translate: key => key
    });

    expect(resolveDefaultOpenedMenuPaths(tree, '/tenants')).toEqual([
      `${SHELL_NAV_GROUP_PATH_PREFIX}tenancy`
    ]);
  });

  it('会生成多级面包屑', () => {
    const groupedNavigation: NavigationNode[] = [
      ...navigation,
      {
        id: 'tenant-packages',
        parentId: null,
        routeName: 'tenant-packages',
        path: '/tenant-packages',
        componentKey: 'tenant-packages',
        title: '租户套餐',
        caption: '租户套餐',
        icon: 'building',
        order: 25,
        requiredPermission: 'tenancy.tenants.read',
        children: []
      }
    ];
    const tree = buildShellNavigationTree({
      navigation: groupedNavigation,
      translate: key => {
        if (key === 'shell.navGroup.tenancy') {
          return '租户';
        }

        return key;
      }
    });

    expect(resolveNavigationBreadcrumb(tree, '/tenants', '控制台')).toEqual([
      '控制台',
      '租户',
      'navigation.tenants.title'
    ]);
  });

  it('会按右键菜单语义批量关闭标签页', () => {
    const tabs = [
      { path: '/', title: '工作台' },
      { path: '/tenants', title: '租户' },
      { path: '/users', title: '用户' }
    ];

    expect(closeShellTabs(tabs, 'left', '/users', '/users')).toEqual({
      tabs: [{ path: '/users', title: '用户' }],
      nextPath: '/users'
    });

    expect(closeShellTabs(tabs, 'right', '/', '/users')).toEqual({
      tabs: [{ path: '/', title: '工作台' }],
      nextPath: '/'
    });

    expect(closeShellTabs(tabs, 'other', '/tenants', '/users')).toEqual({
      tabs: [{ path: '/tenants', title: '租户' }],
      nextPath: '/tenants'
    });

    expect(closeShellTabs(tabs, 'all', '/tenants', '/users')).toEqual({
      tabs: [{ path: '/tenants', title: '租户' }],
      nextPath: '/tenants'
    });
  });
});
