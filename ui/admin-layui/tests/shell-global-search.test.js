import { describe, expect, it, vi } from 'vitest';
import { createShellGlobalSearch } from '../js/core/shell-global-search.js';

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
  title: '租户管理',
  caption: '租户',
  icon: 'building',
  order: 20,
  requiredPermission: 'tenancy.tenants.read',
  children: []
}];

describe('shell-global-search', () => {
  it('打开搜索并按标题导航', () => {
    document.body.innerHTML = `
      <div id="root">
        <button data-shell-search-open></button>
        <div data-shell-search hidden>
          <button data-shell-search-backdrop></button>
          <h2 data-shell-search-title></h2>
          <input data-shell-search-input>
          <p data-shell-search-hint></p>
          <div data-shell-search-results></div>
          <p data-shell-search-empty hidden></p>
        </div>
      </div>
    `;
    const root = document.getElementById('root');
    const onNavigate = vi.fn();
    const search = createShellGlobalSearch(root, {
      getNavigation: () => navigation,
      onNavigate
    });
    const t = key => {
      if (key === 'navigation.overview.title') return '工作台';
      if (key === 'navigation.tenants.title') return '租户管理';
      if (key === 'navigation.tenants.caption') return '租户目录';
      return key;
    };
    search.render(t);
    search.open();
    root.querySelector('[data-shell-search-input]').value = '租户';
    root.querySelector('[data-shell-search-input]').dispatchEvent(new Event('input'));
    root.querySelector('.fn-search-modal__item').click();
    expect(onNavigate).toHaveBeenCalledWith('/tenants');
    expect(root.querySelector('[data-shell-search]').hidden).toBe(true);
  });
});
