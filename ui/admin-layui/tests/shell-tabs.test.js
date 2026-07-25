import { describe, expect, it, beforeEach } from 'vitest';
import {
  closeShellTab,
  createShellTabsController,
  upsertShellTab
} from '../js/core/shell-tabs.js';

const navigation = [
  { path: '/', title: '工作台' },
  { path: '/tenants', title: '租户管理' }
];

describe('shell-tabs', () => {
  it('只跟踪授权路径并支持关闭回退', () => {
    let tabs = upsertShellTab([], navigation, '/');
    tabs = upsertShellTab(tabs, navigation, '/tenants');
    expect(tabs).toEqual([
      { path: '/', title: '工作台' },
      { path: '/tenants', title: '租户管理' }
    ]);

    const closed = closeShellTab(tabs, '/tenants', '/tenants');
    expect(closed.tabs).toEqual([{ path: '/', title: '工作台' }]);
    expect(closed.nextPath).toBe('/');
  });

  it('渲染标签页并响应设置开关', () => {
    document.body.innerHTML = `
      <div id="root">
        <nav data-page-tabs hidden></nav>
      </div>
    `;
    const root = document.getElementById('root');
    const controller = createShellTabsController(root);
    const t = key => key;

    controller.render({
      navigation: [{
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
      }],
      activePath: '/',
      t,
      settings: { showPageTabs: true, tabStyle: 'card' }
    });

    const tabs = root.querySelector('[data-page-tabs]');
    expect(tabs.hidden).toBe(false);
    expect(tabs.className).toContain('fn-page-tabs--card');
    expect(tabs.querySelector('.fn-page-tabs__item.is-active')?.textContent).toContain('navigation.overview.title');

    controller.render({
      navigation: [{
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
      }],
      activePath: '/',
      t,
      settings: { showPageTabs: false, tabStyle: 'default' }
    });
    expect(root.querySelector('[data-page-tabs]').hidden).toBe(true);
  });
});

describe('shell-chrome breadcrumb', () => {
  beforeEach(() => {
    document.body.innerHTML = `
      <div id="root">
        <div data-session-shell>
          <div data-shell-chrome="breadcrumb">面包屑</div>
        </div>
      </div>
    `;
  });

  it('根据 showBreadcrumb 切换面包屑', async () => {
    const { applyShellChrome } = await import('../js/core/shell-chrome.js');
    const { createDefaultShellSettings } = await import('../js/core/shell-art-settings.js');
    const root = document.getElementById('root');

    applyShellChrome(root, { ...createDefaultShellSettings(), showBreadcrumb: false });
    expect(root.querySelector('[data-shell-chrome="breadcrumb"]').hidden).toBe(true);
  });
});
