import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import NotificationPreferencesView from './NotificationPreferencesView.vue';
import { useSessionStore } from '../auth/session';
import {
  createMyRecipientEndpoint,
  deleteMyRecipientEndpoint,
  listMyRecipientEndpoints,
  listNotificationProviderProfiles,
  sendMyRecipientEndpointVerification,
  verifyMyRecipientEndpoint
} from '../api/notification-platform';

vi.mock('../api/notification-platform', () => ({
  createMyRecipientEndpoint: vi.fn(),
  deleteMyRecipientEndpoint: vi.fn(),
  listMyRecipientEndpoints: vi.fn(),
  listNotificationProviderProfiles: vi.fn(),
  sendMyRecipientEndpointVerification: vi.fn(),
  verifyMyRecipientEndpoint: vi.fn()
}));

const profile = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411202',
  profileKey: 'qq-smtp',
  providerTypeKey: 'email.smtp',
  nonSecretConfigJson: '{"host":"smtp.qq.com"}',
  secretStatus: 'configured',
  isEnabled: true,
  draftRevision: 1,
  latestPublishedVersionId: '0198f36e-f7a7-7c52-9cbb-774e67411203',
  latestPublishedVersionNumber: 1,
  latestAdapterVersion: '1.0.0',
  createdAtUtc: '2026-09-01T00:00:00Z',
  updatedAtUtc: null,
  version: 2
};

const endpoint = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411204',
  userId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
  providerProfileVersionId: profile.latestPublishedVersionId,
  endpointKindKey: 'email',
  maskedValue: 'r***@example.test',
  verificationStatusKey: 'pending',
  createdAtUtc: '2026-09-01T00:00:00Z'
};

/** 按指定权限创建通知偏好页，确保写入口只由当前会话精确权限控制。 */
function mountView(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: endpoint.userId,
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(NotificationPreferencesView, { global: { plugins: [pinia] } });
}

describe('Vue 通知偏好页', () => {
  beforeEach(() => {
    vi.mocked(listNotificationProviderProfiles).mockReset().mockResolvedValue({
      items: [profile],
      page: 1,
      pageSize: 100,
      total: 1
    });
    vi.mocked(listMyRecipientEndpoints).mockReset().mockResolvedValue([endpoint]);
    vi.mocked(createMyRecipientEndpoint).mockReset().mockResolvedValue(endpoint);
    vi.mocked(deleteMyRecipientEndpoint).mockReset().mockResolvedValue(undefined);
  });

  it('只读用户只看到脱敏端点和待验证状态', async () => {
    const wrapper = mountView(['notifications.preferences.read']);
    await flushPromises();

    expect(wrapper.get('[data-testid="notification-preferences-endpoint-list"]').text())
      .toContain(endpoint.maskedValue);
    expect(wrapper.text()).toContain('待验证');
    expect(wrapper.html()).not.toContain('recipient@example.test');
    expect(wrapper.find('[data-testid="notification-preferences-save"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="notification-preferences-delete"]').exists()).toBe(false);
  });

  it('登记请求只发送 Profile 版本、端点类型和原值', async () => {
    const wrapper = mountView([
      'notifications.preferences.read',
      'notifications.preferences.update'
    ]);
    await flushPromises();

    const rawValue = 'recipient@example.test';
    await wrapper.get('[data-testid="notification-preferences-email"]').setValue(rawValue);
    await wrapper.get('[data-testid="notification-preferences-save"]').trigger('click');
    await flushPromises();

    expect(createMyRecipientEndpoint).toHaveBeenCalledWith({
      providerProfileVersionId: profile.latestPublishedVersionId,
      endpointKindKey: 'email',
      rawValue
    });
    expect(wrapper.get<HTMLInputElement>('[data-testid="notification-preferences-email"]')
      .element.value).toBe('');
  });

  it('未发布或停用的 SMTP Profile 不提供登记入口', async () => {
    vi.mocked(listNotificationProviderProfiles).mockResolvedValueOnce({
      items: [{ ...profile, isEnabled: false }],
      page: 1,
      pageSize: 100,
      total: 1
    });
    const wrapper = mountView([
      'notifications.preferences.read',
      'notifications.preferences.update'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="notification-preferences-no-profile"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="notification-preferences-save"]').exists()).toBe(false);
  });

  it('待验证端点可发送并校验验证码', async () => {
    vi.mocked(sendMyRecipientEndpointVerification).mockResolvedValue({
      expiresAtUtc: '2026-09-01T00:15:00Z',
      resendAvailableAtUtc: '2026-09-01T00:01:00Z'
    });
    vi.mocked(verifyMyRecipientEndpoint).mockResolvedValue({
      ...endpoint,
      verificationStatusKey: 'verified'
    });
    const wrapper = mountView([
      'notifications.preferences.read',
      'notifications.preferences.update'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="notification-preferences-send-code"]').trigger('click');
    await flushPromises();
    expect(sendMyRecipientEndpointVerification).toHaveBeenCalledWith(endpoint.id);
    await wrapper.get('[data-testid="notification-preferences-code"]').setValue('123456');
    await wrapper.get('[data-testid="notification-preferences-verify"]').trigger('click');
    await flushPromises();
    expect(verifyMyRecipientEndpoint).toHaveBeenCalledWith(endpoint.id, '123456');
    expect(wrapper.text()).toContain('已验证');
  });
});
