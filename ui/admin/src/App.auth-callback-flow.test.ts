import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory } from 'vue-router';
import { ElMessage } from 'element-plus';
const auth = vi.hoisted(() => ({ mode: 'legacy' as 'legacy' | 'oidc-center' }));
vi.mock('./config/identity-auth', () => ({ get adminIdentityAuthMode() { return auth.mode; }, resolveAdminOidcClientId: () => 'admin-spa' }));
vi.mock('./auth/oidc-center-login', async importOriginal => ({ ...await importOriginal<typeof import('./auth/oidc-center-login')>(), completeAdminOidcCallback: vi.fn() }));
import App from './App.vue';
// 预加载真实回调模块，行为计时不包含动态页面编译。
import './views/OAuthCallbackView.vue';
import './views/OidcCallbackView.vue';
import { createAppRouter } from './router';
import { useSessionStore } from './auth/session';
import { configureAuthentication } from './api/http';
import { useAdminI18n } from './i18n/adminI18n';
import { completeAdminOidcCallback } from './auth/oidc-center-login';
import { readOidcRefreshCredentialRevision } from './auth/oidc-session-credentials';
const id = '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60';
const token = { accessToken: 'callback-access', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z' };
const user = { id, username: 'admin', displayName: 'Admin', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false, permissions: ['platform.dashboard.read'], sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 };
const navigation = [{ id: 'overview', parentId: null, routeName: 'overview', path: '/', componentKey: 'overview', title: 'Overview', caption: '', icon: 'dashboard', order: 10, requiredPermission: 'platform.dashboard.read', children: [] }];
const json = (value: unknown) => new Response(JSON.stringify(value), { status: 200, headers: { 'content-type': 'application/json' } });
describe('真实 App 回调生命周期', () => {
  beforeEach(() => { vi.restoreAllMocks(); setActivePinia(createPinia()); useAdminI18n().setLocale('zh-CN'); vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close: vi.fn() })); vi.spyOn(ElMessage, 'error').mockImplementation(() => ({ close: vi.fn() })); vi.mocked(completeAdminOidcCallback).mockReset(); });
  afterEach(() => { configureAuthentication(); vi.unstubAllGlobals(); });
  it.each(['legacy', 'oidc-center'] as const)('%s：认证状态转换不重挂回调且只消费一次', async mode => {
    auth.mode = mode; let refreshes = 0;
    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      if (url.endsWith('/auth/refresh')) { refreshes++; if (refreshes > 1) return new Promise<Response>(() => {}); return json(token); }
      if (url.endsWith('/me')) return json(user);
      if (url.endsWith('/navigation')) return json(navigation);
      return new Response(null, { status: 404 });
    }));
    vi.mocked(completeAdminOidcCallback).mockImplementation(async (_query, _signal, handoff) => { handoff?.(readOidcRefreshCredentialRevision()); return token; });
    const pinia = createPinia(); setActivePinia(pinia); useSessionStore().state = 'anonymous';
    const router = createAppRouter(createMemoryHistory(), pinia);
    await router.push(mode === 'legacy' ? '/oauth/callback?oauth=success' : '/identity/oidc/callback?code=code&state=state'); await router.isReady();
    const wrapper = mount(App, { global: { plugins: [pinia, router] } });
    try {
      await flushPromises();
      await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/'), { timeout: 1200 });
      expect(useSessionStore().state).toBe('authenticated'); expect(ElMessage.success).toHaveBeenCalledOnce(); expect(ElMessage.error).not.toHaveBeenCalled();
      if (mode === 'legacy') expect(refreshes).toBe(1); else expect(completeAdminOidcCallback).toHaveBeenCalledOnce();
    } finally { wrapper.unmount(); useSessionStore().invalidateLocalSession(); }
  });
});
