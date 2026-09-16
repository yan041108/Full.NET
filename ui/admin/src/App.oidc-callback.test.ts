import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createMemoryHistory } from 'vue-router';
import { createPinia, setActivePinia } from 'pinia';
import App from './App.vue';
import LoginView from './views/LoginView.vue';
import { createAppRouter } from './router';
import { useSessionStore } from './auth/session';
import { useAdminI18n } from './i18n/adminI18n';
import { completeAdminOidcCallback } from './auth/oidc-center-login';

const oidcMocks = vi.hoisted(() => ({
  completeAdminOidcCallback: vi.fn()
}));

vi.mock('./config/identity-auth', () => ({
  adminIdentityAuthMode: 'oidc-center',
  resolveAdminOidcClientId: () => 'admin-spa'
}));
vi.mock('./auth/oidc-center-login', () => ({
  completeAdminOidcCallback: oidcMocks.completeAdminOidcCallback,
  beginAdminOidcCenterLogin: vi.fn()
}));

describe('Vue 管理端 OIDC 回调壳层', () => {
  beforeEach(() => {
    localStorage.clear();
    useAdminI18n().setLocale('zh-CN');
    oidcMocks.completeAdminOidcCallback.mockReset();
    oidcMocks.completeAdminOidcCallback.mockImplementation(() => new Promise(() => {}));
  });

  it('匿名访问 OIDC 回调路由时渲染回调页而非登录页', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    useSessionStore().$patch({ state: 'anonymous' });
    const router = createAppRouter(createMemoryHistory(), pinia);
    await router.push('/identity/oidc/callback?code=auth-code&state=expected');
    await router.isReady();

    const wrapper = mount(App, { global: { plugins: [pinia, router] } });
    await flushPromises();

    expect(wrapper.findComponent(LoginView).exists()).toBe(false);
    expect(wrapper.find('input[name="username"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="login-oidc-center"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('正在完成 OIDC 回调');
    expect(completeAdminOidcCallback).toHaveBeenCalledOnce();
  });

  it('匿名访问普通路由时仍展示身份中心登录页', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    useSessionStore().$patch({ state: 'anonymous' });
    const router = createAppRouter(createMemoryHistory(), pinia);
    await router.push('/');
    await router.isReady();

    const wrapper = mount(App, { global: { plugins: [pinia, router] } });
    await flushPromises();

    expect(wrapper.findComponent(LoginView).exists()).toBe(true);
    expect(wrapper.get('[data-testid="login-oidc-center"]').text())
      .toContain('前往身份中心登录');
    expect(wrapper.find('input[name="username"]').exists()).toBe(false);
  });
});