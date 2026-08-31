import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import NotificationProviderProfilesView from './NotificationProviderProfilesView.vue';
import { useSessionStore } from '../auth/session';
import {
  createNotificationProviderProfile,
  disableNotificationProviderProfile,
  enableNotificationProviderProfile,
  listNotificationProviderProfiles,
  listNotificationProviderTypes,
  publishNotificationProviderProfile,
  updateNotificationProviderProfile
} from '../api/notification-platform';

vi.mock('../api/notification-platform', () => ({
  buildNonSecretConfig: vi.fn((_descriptor, values) => values),
  parseNonSecretConfigJson: vi.fn(() => ({ endpointBaseUrl: 'https://example.test' })),
  createNotificationProviderProfile: vi.fn(),
  disableNotificationProviderProfile: vi.fn(),
  enableNotificationProviderProfile: vi.fn(),
  listNotificationProviderProfiles: vi.fn(),
  listNotificationProviderTypes: vi.fn(),
  publishNotificationProviderProfile: vi.fn(),
  updateNotificationProviderProfile: vi.fn()
}));

const typesMock = vi.mocked(listNotificationProviderTypes);
const listMock = vi.mocked(listNotificationProviderProfiles);

const descriptor = {
  providerTypeKey: 'test.notification',
  adapterVersion: '1.0.0',
  supportedChannelKeys: ['test'],
  nonSecretFields: [
    { name: 'endpointBaseUrl', typeKey: 'string', required: true }
  ],
  secretFieldKeys: ['apiToken'],
  supportsNativeAot: true,
  receiptModeKey: 'signed'
};

const profile = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411202',
  profileKey: 'primary',
  providerTypeKey: 'test.notification',
  nonSecretConfigJson: '{"endpointBaseUrl":"https://example.test"}',
  secretStatus: 'configured',
  isEnabled: false,
  draftRevision: 1,
  latestPublishedVersionId: null,
  latestPublishedVersionNumber: null,
  latestAdapterVersion: '1.0.0',
  createdAtUtc: '2026-08-31T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

function mountWithPermissions(permissions: string[]) {
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
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(NotificationProviderProfilesView, { global: { plugins: [pinia] } });
}

describe('Vue 渠道配置页', () => {
  beforeEach(() => {
    typesMock.mockReset().mockResolvedValue([descriptor]);
    listMock.mockReset().mockResolvedValue({
      items: [profile],
      page: 1,
      pageSize: 20,
      total: 1
    });
    vi.mocked(createNotificationProviderProfile).mockReset();
    vi.mocked(updateNotificationProviderProfile).mockReset();
    vi.mocked(publishNotificationProviderProfile).mockReset();
    vi.mocked(enableNotificationProviderProfile).mockReset();
    vi.mocked(disableNotificationProviderProfile).mockReset();
  });

  it('目录为空时显示尚未安装 Provider 且不渲染创建表单', async () => {
    typesMock.mockResolvedValueOnce([]);
    listMock.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0 });
    const wrapper = mountWithPermissions([
      'notifications.provider_profiles.read',
      'notifications.provider_profiles.create'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="notification-profiles-empty-catalog"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="notification-profiles-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="notification-profiles-type"] option').exists()).toBe(false);
  });

  it('仅有 read 时不显示写入操作，且不回显密钥引用', async () => {
    const wrapper = mountWithPermissions(['notifications.provider_profiles.read']);
    await flushPromises();
    await wrapper.get('[data-testid="notification-profiles-load"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="notification-profiles-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="notification-profiles-save"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="notification-profiles-enable"]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="notification-profiles-secret-status"]').text()).toContain('configured');
    expect(wrapper.html()).not.toContain('vault://');
    expect(wrapper.html()).not.toContain('apiToken');
  });

  it('编辑权限不能隐式获得发布权限', async () => {
    const wrapper = mountWithPermissions([
      'notifications.provider_profiles.read',
      'notifications.provider_profiles.update'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="notification-profiles-load"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="notification-profiles-save"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="notification-profiles-publish"]').exists()).toBe(false);
  });

  it('启用需确认且文案声明不会自动多发', async () => {
    const wrapper = mountWithPermissions([
      'notifications.provider_profiles.read',
      'notifications.provider_profiles.enable'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="notification-profiles-load"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="notification-profiles-enable"]').trigger('click');
    await flushPromises();

    const confirm = wrapper.get('[data-testid="notification-profiles-confirm"]');
    expect(confirm.text()).toContain('不会自动');
    expect(wrapper.find('[data-testid="notification-profiles-confirm-yes"]').exists()).toBe(true);
  });
});
