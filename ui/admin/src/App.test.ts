import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import { createMemoryHistory } from 'vue-router';
import App from './App.vue';
import { createAppRouter } from './router';

describe('Vue 管理端壳层', () => {
  it('展示品牌、租户和核心后台导航', async () => {
    const router = createAppRouter(createMemoryHistory());
    await router.push('/');
    await router.isReady();

    const wrapper = mount(App, {
      global: { plugins: [router] }
    });

    expect(wrapper.text()).toContain('Full.NET');
    expect(wrapper.text()).toContain('星云科技');
    expect(wrapper.text()).toContain('工作台');
    expect(wrapper.text()).toContain('身份权限');
    expect(wrapper.text()).toContain('组织架构');
    expect(wrapper.text()).toContain('系统设置');
  });

  it('403 路由呈现权限错误页', async () => {
    const router = createAppRouter(createMemoryHistory());
    await router.push('/403');
    await router.isReady();

    const wrapper = mount(App, {
      global: { plugins: [router] }
    });

    expect(wrapper.text()).toContain('没有访问权限');
    expect(wrapper.text()).toContain('403');
  });
});
