import { beforeEach, describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import LocaleSelector from './LocaleSelector.vue';
import { useAdminI18n } from './adminI18n';
import { useSessionStore } from '../auth/session';

describe('Vue 语言选择器', () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    useAdminI18n().setLocale('zh-CN');
  });

  it('认证保存失败时恢复原选择并给出本地化可访问提示', async () => {
    const session = useSessionStore();
    session.$patch({
      state: 'authenticated',
      currentUser: {
        id: 'user-id', username: 'admin', displayName: '系统管理员',
        tenantId: null, actorScope: 'host', scope: 'host', permissions: [],
        sessionId: 'session-id', preferredLocale: 'zh-CN', profileVersion: 1
      }
    });
    session.changeLocale = vi.fn().mockRejectedValue({
      status: 409,
      code: 'identity.profile_version_conflict'
    });
    const wrapper = mount(LocaleSelector);

    await wrapper.get('select').setValue('en-US');
    await vi.waitFor(() => expect(session.changeLocale).toHaveBeenCalledWith('en-US'));

    expect(useAdminI18n().locale.value).toBe('zh-CN');
    expect(wrapper.get('select').element.value).toBe('zh-CN');
    expect(wrapper.get('[role="alert"]').text()).toContain('已保留原语言');
  });

  it('保存期间禁用选择器并公开忙碌状态', async () => {
    const session = useSessionStore();
    let resolveSave!: () => void;
    const pending = new Promise<void>(resolve => {
      resolveSave = resolve;
    });
    session.changeLocale = vi.fn().mockReturnValue(pending);
    const wrapper = mount(LocaleSelector);

    const change = wrapper.get('select').setValue('en-US');
    await vi.waitFor(() => expect(wrapper.get('select').attributes('aria-busy')).toBe('true'));
    expect(wrapper.get('select').attributes()).toHaveProperty('disabled');
    resolveSave();
    await change;
  });
});
