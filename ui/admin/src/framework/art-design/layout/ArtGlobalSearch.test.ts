import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { mount } from '@vue/test-utils';
import ArtGlobalSearch from './ArtGlobalSearch.vue';
import { Grid } from '@element-plus/icons-vue';
import { createMemoryHistory, createRouter } from 'vue-router';
import { flushPromises } from '@vue/test-utils';

const navigation = [{
  path: '/tenants',
  routeName: 'tenant-management',
  componentKey: 'tenants',
  title: '租户管理',
  caption: 'Host 作用域租户目录',
  icon: Grid,
  isAffix: false
}];

describe('ArtGlobalSearch', () => {
  beforeEach(() => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: false }));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('暴露 open 方法并渲染搜索结果', async () => {
    const wrapper = mount(ArtGlobalSearch, {
      props: {
        navigation,
        title: '搜索',
        placeholder: '搜索功能',
        emptyLabel: '无结果',
        hintLabel: '提示'
      },
      global: {
        stubs: {
          ElDialog: {
            template: '<div><slot /><slot name="footer" /></div>'
          },
          ElInput: true,
          ElScrollbar: { template: '<div><slot /></div>' }
        }
      }
    });

    wrapper.vm.open();
    await wrapper.setProps({});
    expect(wrapper.text()).toContain('租户管理');
    wrapper.unmount();
  });

  it('方向键跨菜单与对象结果移动；作用域切换关闭搜索并保留原生操作按钮', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }, { path: '/tenants', name: 'tenant-management', component: { template: '<div />' } }] });
    await router.push('/'); await router.isReady();
    const wrapper = mount(ArtGlobalSearch, { attachTo: document.body, props: { navigation, title: 'Search', placeholder: 'Search', emptyLabel: 'Empty', hintLabel: 'Hint',
      objectContext: 'one', objectSources: [{ key: 'source', label: 'Source', routeName: 'tenant-management', queryKey: 'id', load: vi.fn() }] },
      global: { plugins: [router], stubs: {
        ElDialog: { template: '<div><slot/><slot name="footer"/></div>' }, ElScrollbar: { template: '<div><slot/></div>' },
        ObjectSearchPanel: { props: ['active'], template: '<section :data-source-active="active"><button data-global-search-result data-testid="source-hit">Object</button><button data-testid="retry">Retry</button></section>' }
      } } });
    try {
      wrapper.vm.open(); await flushPromises(); const input = wrapper.get('input'); await input.trigger('keydown', { key: 'ArrowDown' });
      expect(document.activeElement).toBe(wrapper.get('[data-global-search-result]').element);
      await wrapper.get('[data-global-search-result]').trigger('keydown', { key: 'ArrowDown' }); expect(document.activeElement).toBe(wrapper.get('[data-testid="source-hit"]').element);
      const retry = wrapper.get('[data-testid="retry"]'); await retry.trigger('keydown', { key: 'Enter' }); expect(router.currentRoute.value.path).toBe('/');
      await wrapper.setProps({ objectContext: 'two' }); expect(wrapper.get('[data-source-active]').attributes('data-source-active')).toBe('false');
    } finally { wrapper.unmount(); }
  });
});
