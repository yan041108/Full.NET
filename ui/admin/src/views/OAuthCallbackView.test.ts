import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElMessage } from 'element-plus';
const query = vi.hoisted(() => ({ value: { oauth: 'success' } as Record<string, string> }));
const replace = vi.hoisted(() => vi.fn().mockResolvedValue(undefined));
vi.mock('vue-router', () => ({ useRoute: () => ({ get query() { return query.value; } }), useRouter: () => ({ replace }) }));
import OAuthCallbackView from './OAuthCallbackView.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';

describe('OAuth 回调认证结果', () => {
  beforeEach(() => {
    vi.restoreAllMocks(); setActivePinia(createPinia()); useAdminI18n().setLocale('zh-CN');
    query.value = { oauth: 'success' }; replace.mockClear();
    vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close: vi.fn() }));
    vi.spyOn(ElMessage, 'error').mockImplementation(() => ({ close: vi.fn() }));
  });
  it('恢复为匿名不能提示成功', async () => {
    vi.spyOn(useSessionStore(), 'restore').mockImplementation(async () => { useSessionStore().state = 'anonymous'; return false; });
    mount(OAuthCallbackView); await flushPromises();
    expect(ElMessage.success).not.toHaveBeenCalled();
    expect(ElMessage.error).toHaveBeenCalledWith('会话恢复失败，请重新登录');
    expect(replace).toHaveBeenCalledWith('/');
  });
  it('仅在恢复认证状态后提示成功', async () => {
    vi.spyOn(useSessionStore(), 'restore').mockImplementation(async () => { useSessionStore().state = 'authenticated'; return true; });
    mount(OAuthCallbackView); await flushPromises();
    expect(ElMessage.success).toHaveBeenCalledWith('外部身份登录成功');
  });
  it('离开页面后不提示或重定向', async () => {
    let finish!: (value: boolean) => void;
    vi.spyOn(useSessionStore(), 'restore').mockImplementation(() => new Promise<boolean>(resolve => { finish = resolve; }));
    const wrapper = mount(OAuthCallbackView); wrapper.unmount(); finish(false); await flushPromises();
    expect(ElMessage.success).not.toHaveBeenCalled(); expect(ElMessage.error).not.toHaveBeenCalled(); expect(replace).not.toHaveBeenCalled();
  });
  it('未知外部错误不回显任意文本', async () => {
    query.value = { oauth_error: 'secret-in-query' };
    mount(OAuthCallbackView); await flushPromises();
    expect(ElMessage.error).toHaveBeenCalledWith('会话恢复失败，请重新登录');
  });
});
