import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import NotificationPreferencesView from './NotificationPreferencesView.vue';
import { useSessionStore } from '../auth/session';

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    permissions: ['notifications.preferences.read', 'notifications.preferences.update'],
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(NotificationPreferencesView, { global: { plugins: [pinia] } });
}

describe('Vue 通知偏好页', () => {
  it('即使拥有 update 也不提供编辑入口', () => {
    const wrapper = mountView();
    expect(wrapper.find('[data-testid="notification-preferences-unavailable"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="notification-preferences-save"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('首个真实 Provider');
  });
});
