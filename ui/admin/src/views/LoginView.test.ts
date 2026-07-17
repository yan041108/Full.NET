import { describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import LoginView from './LoginView.vue';
import { useSessionStore } from '../auth/session';

describe('Vue 登录页', () => {
  it('提交凭据后进入认证状态', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({
        accessToken: 'access-token', tokenType: 'Bearer',
        expiresAtUtc: '2026-07-17T04:00:00Z'
      }), { status: 200, headers: { 'content-type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        id: 'user-id', username: 'admin', displayName: '系统管理员',
        tenantId: null, actorScope: 'host', scope: 'host',
        permissions: ['platform.dashboard.read'], sessionId: 'session-id'
      }), { status: 200, headers: { 'content-type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify([{
        id: 'overview', parentId: null, routeName: 'overview', path: '/',
        componentKey: 'overview', title: '工作台', caption: '平台运行概览',
        icon: 'dashboard', order: 10,
        requiredPermission: 'platform.dashboard.read', children: []
      }]), { status: 200, headers: { 'content-type': 'application/json' } })));
    const wrapper = mount(LoginView, { global: { plugins: [pinia] } });

    await wrapper.get('input[name="username"]').setValue('admin');
    await wrapper.get('input[name="password"]').setValue('FullNet!2026Secure');
    await wrapper.get('form').trigger('submit');
    await vi.waitFor(() => expect(useSessionStore().state).toBe('authenticated'));

    expect(wrapper.text()).toContain('安全会话已建立');
  });
});
