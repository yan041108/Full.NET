import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElMessage } from 'element-plus';

const identityAuth = vi.hoisted(() => ({ mode: 'oidc-center' as 'legacy' | 'oidc-center' }));
const replaceMock = vi.hoisted(() => vi.fn().mockResolvedValue(undefined));

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: { code: 'auth-code', state: 'expected-state' } }),
  useRouter: () => ({ replace: replaceMock })
}));
vi.mock('../config/identity-auth', () => ({
  get adminIdentityAuthMode() {
    return identityAuth.mode;
  },
  resolveAdminOidcClientId: () => 'admin-spa'
}));
vi.mock('../auth/oidc-center-login', () => ({
  completeAdminOidcCallback: vi.fn()
}));

import OidcCallbackView from './OidcCallbackView.vue';
import { completeAdminOidcCallback } from '../auth/oidc-center-login';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';

const tokenResponse = {
  accessToken: 'oidc-access-token',
  tokenType: 'Bearer' as const,
  expiresAtUtc: '2026-07-17T04:00:00Z'
};

describe('OidcCallbackView', () => {
  beforeEach(() => {
    identityAuth.mode = 'oidc-center';
    localStorage.clear();
    useAdminI18n().setLocale('zh-CN');
    replaceMock.mockClear();
    vi.mocked(completeAdminOidcCallback).mockReset();
    vi.mocked(completeAdminOidcCallback).mockResolvedValue(tokenResponse);
    vi.spyOn(ElMessage, 'success').mockImplementation(() => undefined);
    vi.spyOn(ElMessage, 'error').mockImplementation(() => undefined);
  });

  it('legacy 模式仅重定向首页', async () => {
    identityAuth.mode = 'legacy';
    const pinia = createPinia();
    setActivePinia(pinia);
    const completeSpy = vi.spyOn(useSessionStore(), 'completeOidcAuthorization');

    mount(OidcCallbackView, { global: { plugins: [pinia] } });
    await flushPromises();

    expect(completeAdminOidcCallback).not.toHaveBeenCalled();
    expect(completeSpy).not.toHaveBeenCalled();
    expect(replaceMock).toHaveBeenCalledWith('/');
  });

  it('oidc-center 成功时建立会话并提示成功', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const completeSpy = vi.spyOn(useSessionStore(), 'completeOidcAuthorization')
      .mockResolvedValue(undefined);

    const wrapper = mount(OidcCallbackView, { global: { plugins: [pinia] } });
    await flushPromises();

    expect(completeAdminOidcCallback).toHaveBeenCalledOnce();
    expect(completeSpy).toHaveBeenCalledWith(tokenResponse);
    expect(ElMessage.success).toHaveBeenCalledWith('身份中心登录成功');
    expect(replaceMock).toHaveBeenCalledWith('/');
    expect(wrapper.text()).toContain('正在返回控制台');
  });

  it('oidc-center 失败时展示翻译后的错误并返回首页', async () => {
    vi.mocked(completeAdminOidcCallback).mockRejectedValueOnce(new Error('oidc_invalid_state'));
    const pinia = createPinia();
    setActivePinia(pinia);

    mount(OidcCallbackView, { global: { plugins: [pinia] } });
    await flushPromises();

    expect(ElMessage.error).toHaveBeenCalledWith('授权状态无效或已过期');
    expect(replaceMock).toHaveBeenCalledWith('/');
  });
});