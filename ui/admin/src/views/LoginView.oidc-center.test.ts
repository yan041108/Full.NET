import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';

vi.mock('../api/oauth-providers', () => ({ listPublicOAuthProviders: vi.fn().mockResolvedValue([]) }));
vi.mock('../config/identity-auth', () => ({
  adminIdentityAuthMode: 'oidc-center',
  resolveAdminOidcClientId: () => 'admin-spa'
}));
vi.mock('../auth/oidc-center-login', () => ({
  beginAdminOidcCenterLogin: vi.fn()
}));

import LoginView from './LoginView.vue';
import { beginAdminOidcCenterLogin } from '../auth/oidc-center-login';
import { useAdminI18n } from '../i18n/adminI18n';

describe('Vue 登录页 oidc-center 模式', () => {
  beforeEach(() => {
    localStorage.clear();
    useAdminI18n().setLocale('zh-CN');
    vi.mocked(beginAdminOidcCenterLogin).mockReset();
    vi.mocked(beginAdminOidcCenterLogin).mockResolvedValue(undefined);
  });

  it('展示身份中心登录入口并隐藏 legacy 表单', () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const wrapper = mount(LoginView, { global: { plugins: [pinia] } });

    expect(wrapper.get('[data-testid="login-oidc-center"]').text())
      .toContain('前往身份中心登录');
    expect(wrapper.find('input[name="username"]').exists()).toBe(false);
    expect(wrapper.find('input[name="password"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('使用身份中心完成单点登录');
  });

  it('点击身份中心登录按钮时启动 OIDC 授权流程', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const wrapper = mount(LoginView, { global: { plugins: [pinia] } });

    await wrapper.get('[data-testid="login-oidc-center"]').trigger('click');
    await flushPromises();

    expect(beginAdminOidcCenterLogin).toHaveBeenCalledOnce();
    expect(wrapper.find('[role="alert"]').exists()).toBe(false);
  });

  it('启动失败时展示错误提示', async () => {
    vi.mocked(beginAdminOidcCenterLogin).mockRejectedValueOnce(new Error('network'));
    const pinia = createPinia();
    setActivePinia(pinia);
    const wrapper = mount(LoginView, { global: { plugins: [pinia] } });

    await wrapper.get('[data-testid="login-oidc-center"]').trigger('click');
    await flushPromises();

    const alert = wrapper.get('[role="alert"]');
    expect(alert.text()).toContain('client.oidc_login_failed');
    expect(alert.text()).toContain('无法启动身份中心登录');
  });
});