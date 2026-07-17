import { beforeEach, describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import LoginView from './LoginView.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';

describe('Vue 登录页', () => {
  beforeEach(() => {
    localStorage.clear();
    useAdminI18n().setLocale('zh-CN');
  });

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

  it('提供可访问的双语登录表单且不改变认证状态', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const wrapper = mount(LoginView, { global: { plugins: [pinia] } });

    expect(wrapper.get('label[for="login-username"]').text()).toBe('账号');
    expect(wrapper.get('#login-username').attributes()).toMatchObject({
      name: 'username',
      autocomplete: 'username',
      spellcheck: 'false'
    });
    expect(wrapper.get('#login-password').attributes('autocomplete'))
      .toBe('current-password');

    await wrapper.get('select[name="locale"]').setValue('en-US');

    expect(wrapper.get('h1').text()).toContain('delivery control plane');
    expect(wrapper.get('h2').text()).toBe('Administrator sign in');
    expect(useSessionStore().state).toBe('initializing');
  });
});
