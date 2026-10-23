import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import NotificationSendingPreferencesView from './NotificationSendingPreferencesView.vue';
import { useSessionStore } from '../auth/session';
import {
  listMyNotificationPreferences, listMyNotificationPreferenceChannels, updateMyNotificationPreference
} from '../api/notification-platform';
import { showProblem } from '../feedback/fullNetMessage';

vi.mock('../api/notification-platform', () => ({
  listMyNotificationPreferences: vi.fn(),
  listMyNotificationPreferenceChannels: vi.fn(),
  updateMyNotificationPreference: vi.fn()
}));
vi.mock('../feedback/fullNetMessage', () => ({
  showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn()
}));

const saved = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf299',
  userId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
  channelKey: 'email',
  channelOptedOut: true,
  marketingConsentGranted: false,
  quietHoursJson: null,
  updatedAtUtc: '2026-10-22T00:00:00Z',
  version: 3
};

function page(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  useSessionStore().currentUser = {
    id: saved.userId, username: 'admin', displayName: '管理员', tenantId: null,
    actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN', profileVersion: 1
  };
  return mount(NotificationSendingPreferencesView, { global: { plugins: [pinia] } });
}

describe('发送偏好页面', () => {
  beforeEach(() => {
    vi.mocked(listMyNotificationPreferences).mockReset().mockResolvedValue([saved]);
    vi.mocked(listMyNotificationPreferenceChannels).mockReset().mockResolvedValue(['email', 'inbox']);
    vi.mocked(updateMyNotificationPreference).mockReset().mockResolvedValue(saved);
  });

  it('无读取权限不发请求也不渲染保存', async () => {
    const wrapper = page([]);
    await flushPromises();
    expect(listMyNotificationPreferences).not.toHaveBeenCalled();
    expect(listMyNotificationPreferenceChannels).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="preference-save"]').exists()).toBe(false);
  });

  it('只读会话看见渠道与已保存偏好，不渲染写入口', async () => {
    const wrapper = page(['notifications.preferences.read']);
    await flushPromises();
    expect(wrapper.find('[data-channel="email"]').exists()).toBe(true);
    expect(wrapper.find('[data-channel="inbox"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="preference-save"]').exists()).toBe(false);
    expect(updateMyNotificationPreference).not.toHaveBeenCalled();
  });

  it('保存只发送当前渠道发送意愿，成功使用权威响应刷新', async () => {
    const wrapper = page(['notifications.preferences.read', 'notifications.preferences.update']);
    await flushPromises();
    await wrapper.get('[data-channel="email"] [data-testid="preference-save"]').trigger('click');
    await flushPromises();
    expect(updateMyNotificationPreference).toHaveBeenCalledWith('email', {
      channelKey: 'email', channelOptedOut: true, marketingConsentGranted: false, quietHoursJson: null
    });
  });

  it('服务端拒绝统一反馈，保留可重试表单', async () => {
    const problem = { status: 409, code: 'notifications.binding_concurrency_conflict', title: '偏好已变更' };
    vi.mocked(updateMyNotificationPreference).mockRejectedValueOnce(problem);
    const wrapper = page(['notifications.preferences.read', 'notifications.preferences.update']);
    await flushPromises();
    await wrapper.get('[data-channel="email"] [data-testid="preference-save"]').trigger('click');
    await flushPromises();
    expect(showProblem).toHaveBeenCalledWith(problem, '发送偏好保存未完成。');
    expect(wrapper.find('[data-channel="email"]').exists()).toBe(true);
  });

  it('加载失败不伪造空成功或保留可编辑旧数据', async () => {
    vi.mocked(listMyNotificationPreferences).mockRejectedValueOnce(Error('offline'));
    const wrapper = page(['notifications.preferences.read', 'notifications.preferences.update']);
    await flushPromises();
    expect(wrapper.text()).toContain('无法加载发送偏好');
    expect(wrapper.find('[data-testid="preference-save"]').exists()).toBe(false);
  });
});
