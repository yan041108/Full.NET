import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { mount } from '@vue/test-utils';
import ArtGlobalSearch from './ArtGlobalSearch.vue';
import { Grid } from '@element-plus/icons-vue';

const navigation = [{
  path: '/tenants',
  routeName: 'tenant-management',
  componentKey: 'tenants',
  title: '租户管理',
  caption: 'Host 作用域租户目录',
  icon: Grid
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
  });
});
