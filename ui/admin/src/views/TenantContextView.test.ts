import { describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
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
        permissions: ['tenancy.tenants.read', 'tenancy.tenants.switch'],
        sessionId: 'session-id'
      },
      availableTenants: [{
        id: tenantId, identifier: 'acme', name: 'Acme Corporation',
        domain: 'acme.localhost'
      }]
    });
    const switchTenant = vi.spyOn(session, 'switchTenant')
      .mockResolvedValue(undefined);

    const wrapper = mount(TenantContextView, {
      global: { plugins: [pinia] }
    });
    await wrapper.get(`[data-tenant-id="${tenantId}"]`).trigger('click');

    expect(wrapper.text()).toContain('Full.NET Host');
    expect(wrapper.text()).toContain('Acme Corporation');
    expect(switchTenant).toHaveBeenCalledWith(tenantId);
  });

  it('没有切换权限时不呈现操作按钮', () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const session = useSessionStore();
    session.$patch({
      state: 'authenticated',
      currentUser: {
        id: 'user-id', username: 'viewer', displayName: '只读管理员',
        tenantId: null, actorScope: 'host', scope: 'host',
        permissions: ['tenancy.tenants.read'], sessionId: 'session-id'
      },
      availableTenants: [{
        id: tenantId, identifier: 'acme', name: 'Acme Corporation',
        domain: 'acme.localhost'
      }]
    });

    const wrapper = mount(TenantContextView, {
      global: { plugins: [pinia] }
    });

    expect(wrapper.find('[data-tenant-id]').exists()).toBe(false);
  });
});
