import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import NotificationDeliveriesView from './NotificationDeliveriesView.vue';
import { useSessionStore } from '../auth/session';
import {
  getNotificationDelivery,
  listNotificationDeliveries,
  retryNotificationDelivery
} from '../api/notification-platform';

vi.mock('../api/notification-platform', () => ({
  getNotificationDelivery: vi.fn(),
  listNotificationDeliveries: vi.fn(),
  retryNotificationDelivery: vi.fn()
}));

const listMock = vi.mocked(listNotificationDeliveries);
const getMock = vi.mocked(getNotificationDelivery);

function delivery(statusKey: string, idSuffix = '03') {
  return {
    id: `0198f36e-f7a7-7c52-9cbb-774e674112${idSuffix}`,
    intentId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
    recipientId: '0198f36e-f7a7-7c52-9cbb-774e67411205',
    channelKey: 'test',
    providerProfileVersionId: null,
    bindingVersionId: null,
    statusKey,
    revision: 2,
    nextAttemptAtUtc: null,
    createdAtUtc: '2026-08-31T00:00:00Z',
    updatedAtUtc: null,
    attempts: statusKey === 'failed'
      ? [{
        id: '0198f36e-f7a7-7c52-9cbb-774e67411207',
        attemptNumber: 1,
        statusKey: 'failed',
        resultCategoryKey: 'permanent',
        providerMessageId: null,
        errorCode: 'permanent',
        startedAtUtc: '2026-08-31T00:00:00Z',
        finishedAtUtc: '2026-08-31T00:00:01Z'
      }]
      : []
  };
}

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
  return mount(NotificationDeliveriesView, { global: { plugins: [pinia] } });
}

describe('Vue 投递运维页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [delivery('unknown', '03'), delivery('delivered', '08')],
      page: 1,
      pageSize: 20,
      total: 2
    });
    getMock.mockReset().mockResolvedValue(delivery('failed'));
    vi.mocked(retryNotificationDelivery).mockReset();
  });

  it('Unknown 不使用成功色，且只读时不显示重试', async () => {
    const wrapper = mountWithPermissions(['notifications.deliveries.read']);
    await flushPromises();

    const unknown = wrapper.get('[data-testid="notification-deliveries-status-unknown"]');
    expect(unknown.classes()).toContain('delivery-status--unknown');
    expect(unknown.classes().join(' ')).not.toContain('success');
    expect(wrapper.find('[data-testid="notification-deliveries-retry"]').exists()).toBe(false);
  });

  it('failed 且拥有 retry 时显示理由输入', async () => {
    const wrapper = mountWithPermissions([
      'notifications.deliveries.read',
      'notifications.deliveries.retry'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="notification-deliveries-load"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="notification-deliveries-retry-reason"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="notification-deliveries-retry"]').exists()).toBe(true);
  });
});
