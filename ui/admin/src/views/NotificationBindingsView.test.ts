import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import NotificationBindingsView from './NotificationBindingsView.vue';
import { useSessionStore } from '../auth/session';
import {
  createNotificationBinding,
  listNotificationBindings,
  listNotificationProviderProfiles,
  publishNotificationBinding,
  updateNotificationBinding
} from '../api/notification-platform';

vi.mock('../api/notification-platform', () => ({
  createNotificationBinding: vi.fn(),
  listNotificationBindings: vi.fn(),
  listNotificationProviderProfiles: vi.fn(),
  publishNotificationBinding: vi.fn(),
  updateNotificationBinding: vi.fn()
}));

const listMock = vi.mocked(listNotificationBindings);
const profilesMock = vi.mocked(listNotificationProviderProfiles);

const binding = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411206',
  bindingKey: 'order-shipped',
  draftDispatchModeKey: 'single',
  draftJson: '{"producerKey":"tests.orders","sceneKey":"order.shipped","channelKey":"test","targets":[{"profileKey":"primary","order":1}]}',
  draftRevision: 1,
  latestPublishedVersionId: null,
  latestPublishedVersionNumber: null,
  latestProducerKey: null,
  latestSceneKey: null,
  latestChannelKey: null,
  latestDispatchModeKey: null,
  latestBindingTargetsJson: null,
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
  return mount(NotificationBindingsView, { global: { plugins: [pinia] } });
}

describe('Vue 场景绑定页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [binding],
      page: 1,
      pageSize: 20,
      total: 1
    });
    profilesMock.mockReset().mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 100,
      total: 0
    });
    vi.mocked(createNotificationBinding).mockReset();
    vi.mocked(updateNotificationBinding).mockReset();
    vi.mocked(publishNotificationBinding).mockReset();
  });

  it('仅有 read 时不显示创建与发布', async () => {
    const wrapper = mountWithPermissions(['notifications.bindings.read']);
    await flushPromises();

    expect(wrapper.find('[data-testid="notification-bindings-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="notification-bindings-publish"]').exists()).toBe(false);
  });

  it('FanOut 必须显式确认后才能提交', async () => {
    const wrapper = mountWithPermissions([
      'notifications.bindings.read',
      'notifications.bindings.create'
    ]);
    await flushPromises();
    const modeSelect = wrapper.findAllComponents({ name: 'ElSelect' })[0];
    await modeSelect.setValue('fan_out');
    await flushPromises();

    expect(wrapper.find('[data-testid="notification-bindings-fanout"]').exists()).toBe(true);
    expect(wrapper.get('[data-testid="notification-bindings-create"]').attributes('disabled')).toBeDefined();
    const ack = wrapper.findAllComponents({ name: 'ElCheckbox' })[0];
    await ack.setValue(true);
    await flushPromises();
    expect(wrapper.get('[data-testid="notification-bindings-create"]').attributes('disabled')).toBeUndefined();
  });
});
