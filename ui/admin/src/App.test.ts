import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import { createMemoryHistory } from 'vue-router';
import { createPinia, setActivePinia } from 'pinia';
import { ElOption } from 'element-plus';
import App from './App.vue';
import { createAppRouter } from './router';
import { useSessionStore } from './auth/session';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

function createAuthenticatedPinia() {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.$patch({
    state: 'authenticated',
    currentUser: {
      id: 'user-id', username: 'admin', displayName: '系统管理员',
      tenantId: null, actorScope: 'host', scope: 'host',
      permissions: ['platform.dashboard.read', 'tenancy.tenants.read'],
      sessionId: 'session-id'
    },
    navigation: [{
      id: 'overview', parentId: null, routeName: 'overview', path: '/',
      componentKey: 'overview', title: '工作台', caption: '平台运行概览',
      icon: 'dashboard', order: 10,
      requiredPermission: 'platform.dashboard.read', children: []
    }, {
      id: 'tenant-context', parentId: null, routeName: 'tenant-context',
      path: '/tenant-context', componentKey: 'tenant-context',
      title: '租户上下文', caption: '进入租户或返回 Host',
      icon: 'building', order: 20,
      requiredPermission: 'tenancy.tenants.read', children: []
    }],
    availableTenants: [{
      id: tenantId,
      identifier: 'acme',
      name: 'Acme Corporation',
      domain: 'acme.localhost'
    }]
  });
  return pinia;
}

describe('Vue 管理端壳层', () => {
  it('展示服务端导航、Host 上下文和可用租户', async () => {
    const pinia = createAuthenticatedPinia();
    const router = createAppRouter(createMemoryHistory(), pinia);
    await router.push('/');
    await router.isReady();

    const wrapper = mount(App, {
      global: { plugins: [pinia, router] }
    });

    expect(wrapper.text()).toContain('Full.NET');
    expect(wrapper.text()).toContain('Full.NET Host');
    expect(wrapper.findAllComponents(ElOption).some(option =>
      option.props('label') === 'Acme Corporation'
    )).toBe(true);
    expect(wrapper.text()).toContain('工作台');
    expect(wrapper.text()).toContain('租户上下文');
    expect(wrapper.text()).not.toContain('身份权限');
  });

  it('403 路由呈现权限错误页', async () => {
    const pinia = createAuthenticatedPinia();
    const router = createAppRouter(createMemoryHistory(), pinia);
    await router.push('/403');
    await router.isReady();

    const wrapper = mount(App, {
      global: { plugins: [pinia, router] }
    });

    expect(wrapper.text()).toContain('没有访问权限');
    expect(wrapper.text()).toContain('403');
  });
});
