import { describe, expect, it, beforeEach } from 'vitest';
import {
  buildFlatNavigationItems,
  buildShellNavigationGroups,
  resolveActiveGroupId
} from '../js/core/shell-navigation-groups.js';

const t = key => {
  if (key === 'shell.navGroup.platform') {
    return '工作台';
  }

  if (key === 'shell.navGroup.tenancy') {
    return '租户';
  }

  return key;
};

const navigation = [{
  id: 'overview',
  parentId: null,
  routeName: 'overview',
  path: '/',
  componentKey: 'overview',
  title: '工作台',
  caption: '概览',
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

describe('shell-navigation-groups', () => {
  it('扁平导航按路径前缀推断一级分组', () => {
    const groups = buildShellNavigationGroups(navigation, t);

    expect(groups.map(group => group.id)).toEqual(['platform', 'tenancy']);
    expect(groups[0]?.items.map(item => item.path)).toEqual(['/']);
    expect(groups[1]?.items.map(item => item.path)).toEqual(['/tenants']);
  });

  it('根据当前路径解析一级分组', () => {
    const groups = buildShellNavigationGroups(navigation, t);

    expect(resolveActiveGroupId(groups, '/tenants')).toBe('tenancy');
    expect(resolveActiveGroupId(groups, '/')).toBe('platform');
  });

  it('只暴露白名单导航项', () => {
    const items = buildFlatNavigationItems([
      ...navigation,
      {
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
      }
    ], t);

    expect(items.map(item => item.path)).toEqual(['/', '/tenants']);
  });
});

describe('shell-preferences', () => {
  beforeEach(() => {
    sessionStorage.clear();
    document.documentElement.removeAttribute('data-fn-menu-layout');
    document.documentElement.removeAttribute('data-fn-dual-menu-show-text');
  });

  it('持久化菜单布局偏好', async () => {
    const { patchShellSettings, readShellSettings, applyShellSettingsToDocument } =
      await import('../js/core/shell-art-settings.js');

    const next = patchShellSettings({ menuLayout: 'top-left', dualMenuShowText: true });
    applyShellSettingsToDocument(next);

    expect(readShellSettings().menuLayout).toBe('top-left');
    expect(readShellSettings().dualMenuShowText).toBe(true);
    expect(document.documentElement.dataset.fnMenuLayout).toBe('top-left');
    expect(document.documentElement.dataset.fnDualMenuShowText).toBe('true');
  });
});

describe('shell-layout', () => {
  beforeEach(() => {
    sessionStorage.clear();
    document.body.innerHTML = `
      <div id="root">
        <div data-session-shell class="fn-shell fn-shell--layout-left">
          <aside data-primary-sidebar class="fn-sidebar">
            <nav data-navigation></nav>
          </aside>
          <nav data-horizontal-menu hidden></nav>
          <nav data-mixed-menu hidden></nav>
          <aside data-dual-rail hidden>
            <nav data-dual-rail-nav></nav>
          </aside>
        </div>
      </div>
    `;
  });

  it('切换布局偏好时复用已缓存的翻译函数', async () => {
    const { createShellLayoutController } = await import('../js/core/shell-layout.js');
    const root = document.getElementById('root');
    const controller = createShellLayoutController(root, {
      getNavigation: () => navigation,
      getActivePath: () => '/'
    });

    controller.render({ navigation, activePath: '/', t });
    expect(root.querySelector('[data-horizontal-menu]').hidden).toBe(true);

    controller.updatePreferences({ menuLayout: 'top' });
    expect(root.querySelector('.fn-shell--layout-top')).toBeTruthy();
    expect(root.querySelector('[data-horizontal-menu]').hidden).toBe(false);
    expect(root.querySelector('[data-horizontal-menu] a[data-route="/"]')).toBeTruthy();
  });
});
