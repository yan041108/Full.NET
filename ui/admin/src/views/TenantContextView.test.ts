import { describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import TenantContextView from './TenantContextView.vue';
import { useSessionStore } from '../auth/session';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

describe('Vue 租户上下文页面', () => {
  it('展示可信上下文并通过 Store 发起租户切换', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const session = useSessionStore();
    session.$patch({
      state: 'authenticated',
      currentUser: {
        id: 'user-id', username: 'admin', displayName: '系统管理员',
        tenantId: null, actorScope: 'host', scope: 'host',
        isSuperAdministrator: true,
        permissions: ['tenancy.tenants.read', 'tenancy.tenants.switch'],
        sessionId: 'session-id', preferredLocale: 'zh-CN', profileVersion: 1
      },
      availableTenants: [{
        id: tenantId, identifier: 'acme', name: 'Acme Corporation',
        domain: 'acme.localhost'
      }]
    });
    const switchTenant = vi.spyOn(session, 'switchTenant')
      .mockResolvedValue(undefined);
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: { template: '<div />' } }]
    });
    await router.push('/tenant-context');

    const wrapper = mount(TenantContextView, {
      global: { plugins: [pinia, router] },
      attachTo: document.body
    });
    await flushPromises();

    const button = document.querySelector(`[data-tenant-id="${tenantId}"]`) as HTMLElement;
    expect(button).not.toBeNull();
    await button.click();
    await flushPromises();

    expect(wrapper.text()).toContain('Full.NET Host');
    expect(wrapper.text()).toContain('Acme Corporation');
    expect(switchTenant).toHaveBeenCalledWith(tenantId);
    expect(router.currentRoute.value.path).toBe('/');
    wrapper.unmount();
  });

  it('没有切换权限时不呈现操作按钮', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const session = useSessionStore();
    session.$patch({
      state: 'authenticated',
      currentUser: {
        id: 'user-id', username: 'viewer', displayName: '只读管理员',
        tenantId: null, actorScope: 'host', scope: 'host',
        isSuperAdministrator: false,
        permissions: ['tenancy.tenants.read'], sessionId: 'session-id',
        preferredLocale: 'zh-CN', profileVersion: 1
      },
      availableTenants: [{
        id: tenantId, identifier: 'acme', name: 'Acme Corporation',
        domain: 'acme.localhost'
      }]
    });

    const wrapper = mount(TenantContextView, {
      global: { plugins: [pinia] },
      attachTo: document.body
    });
    await flushPromises();

    expect(document.querySelector('[data-tenant-id]')).toBeNull();
    wrapper.unmount();
  });
});
