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
import { readOidcRefreshCredential, readOidcRefreshCredentialRevision, writeOidcRefreshCredential } from '../auth/oidc-session-credentials';

const tokenResponse = {
  accessToken: 'oidc-access-token',
  tokenType: 'Bearer' as const,
  expiresAtUtc: '2026-07-17T04:00:00Z'
};

describe('OidcCallbackView', () => {
  beforeEach(() => {
    identityAuth.mode = 'oidc-center';
    localStorage.clear();
    sessionStorage.clear();
    useAdminI18n().setLocale('zh-CN');
    replaceMock.mockClear();
    vi.mocked(completeAdminOidcCallback).mockReset();
    vi.mocked(completeAdminOidcCallback).mockImplementation(async (_query, _signal, handoff) => {
      handoff?.(readOidcRefreshCredentialRevision()); return tokenResponse;
    });
    vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close: vi.fn() }));
    vi.spyOn(ElMessage, 'error').mockImplementation(() => ({ close: vi.fn() }));
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
      .mockImplementation(async () => { useSessionStore().state = 'authenticated'; });

    const wrapper = mount(OidcCallbackView, { global: { plugins: [pinia] } });
    await flushPromises();

    expect(completeAdminOidcCallback).toHaveBeenCalledOnce();
    expect(completeSpy).toHaveBeenCalledWith(tokenResponse, expect.any(AbortSignal));
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

  it('建立会话被取消时不能提示成功', async () => {
    setActivePinia(createPinia());
    vi.spyOn(useSessionStore(), 'completeOidcAuthorization').mockImplementation(async () => { useSessionStore().state = 'anonymous'; });
    mount(OidcCallbackView); await flushPromises();
    expect(ElMessage.success).not.toHaveBeenCalled();
    expect(ElMessage.error).toHaveBeenCalledWith('OIDC 回调处理失败');
  });

  it('离开页面后的令牌响应不能建立会话或重定向', async () => {
    setActivePinia(createPinia());
    let finish!: (value: typeof tokenResponse) => void;
    vi.mocked(completeAdminOidcCallback).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    const complete = vi.spyOn(useSessionStore(), 'completeOidcAuthorization');
    const wrapper = mount(OidcCallbackView); wrapper.unmount(); finish(tokenResponse); await flushPromises();
    expect(complete).not.toHaveBeenCalled(); expect(ElMessage.success).not.toHaveBeenCalled(); expect(replaceMock).not.toHaveBeenCalled();
  });

  it('兑换后快照加载被取消只清理本次 refresh 凭据', async () => {
    setActivePinia(createPinia());
    vi.mocked(completeAdminOidcCallback).mockImplementation(async (_query, _signal, handoff) => {
      writeOidcRefreshCredential({ refreshToken: 'callback-refresh', clientId: 'admin-spa' }); handoff?.(readOidcRefreshCredentialRevision()); return tokenResponse;
    });
    let finish!: () => void;
    vi.spyOn(useSessionStore(), 'completeOidcAuthorization').mockImplementation(() => new Promise<void>(resolve => { finish = resolve; }));
    const wrapper = mount(OidcCallbackView); await flushPromises(); wrapper.unmount(); finish(); await flushPromises();
    expect(readOidcRefreshCredential()).toBeUndefined(); expect(ElMessage.success).not.toHaveBeenCalled();
  });

  it('凭据写入与页面接收结果之间取消也能清理所属凭据', async () => {
    setActivePinia(createPinia()); let wrapper: ReturnType<typeof mount>;
    vi.mocked(completeAdminOidcCallback).mockImplementation(async (_query, _signal, handoff) => {
      await Promise.resolve(); writeOidcRefreshCredential({ refreshToken: 'callback-refresh', clientId: 'admin-spa' });
      handoff?.(readOidcRefreshCredentialRevision()); queueMicrotask(() => wrapper.unmount()); return tokenResponse;
    });
    wrapper = mount(OidcCallbackView); await flushPromises();
    expect(readOidcRefreshCredential()).toBeUndefined(); expect(ElMessage.success).not.toHaveBeenCalled();
  });

  it('交接后的旧令牌不能接入新凭据代次', async () => {
    setActivePinia(createPinia());
    vi.mocked(completeAdminOidcCallback).mockImplementation(async (_query, _signal, handoff) => {
      handoff?.(readOidcRefreshCredentialRevision());
      queueMicrotask(() => writeOidcRefreshCredential({ refreshToken: 'new-session-refresh', clientId: 'admin-spa' })); return tokenResponse;
    });
    const complete = vi.spyOn(useSessionStore(), 'completeOidcAuthorization').mockResolvedValue(undefined);
    mount(OidcCallbackView); await flushPromises();
    expect(complete).not.toHaveBeenCalled(); expect(readOidcRefreshCredential()?.refreshToken).toBe('new-session-refresh');
  });
});
