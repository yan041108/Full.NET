import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import InboxMessagesView from './InboxMessagesView.vue';
import { useSessionStore } from '../auth/session';
import {
  getInboxUnreadCount,
  listInboxMessages,
  markAllInboxMessagesRead,
  markInboxMessageRead,
  sendHostInboxMessage
} from '../api/inbox-messages';

vi.mock('../api/inbox-messages', () => ({
  getInboxUnreadCount: vi.fn(),
  listInboxMessages: vi.fn(),
  markAllInboxMessagesRead: vi.fn(),
  markInboxMessageRead: vi.fn(),
  sendHostInboxMessage: vi.fn()
}));

vi.mock('../api/users', () => ({
  listHostUsers: vi.fn().mockResolvedValue({
    items: [
      {
        id: '01912345-6789-7abc-8def-0123456789ad',
        username: 'alice',
        displayName: 'Alice',
        accountType: 'standard',
        isActive: true,
        createdAtUtc: '2026-07-26T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }
    ],
    page: 1,
    pageSize: 200,
    total: 1
  })
}));

vi.mock('../notifications/realtime', () => ({
  useNotificationsRealtime: () => ({
    inboxRevision: { value: 0 }
  })
}));

const listMock = vi.mocked(listInboxMessages);
const unreadMock = vi.mocked(getInboxUnreadCount);

const unreadMessage = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  title: 'unread-title',
  content: 'unread-content',
  status: 'unread' as const,
  readAtUtc: null,
  createdAtUtc: '2026-07-26T00:00:00Z',
  createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
};

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '\u7ba1\u7406\u5458',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(InboxMessagesView, { global: { plugins: [pinia] } });
}

describe('Vue Host \u6d88\u606f\u4e2d\u5fc3\u9875', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [unreadMessage],
      page: 1,
      pageSize: 20,
      total: 1
    });
    unreadMock.mockReset().mockResolvedValue({ unreadCount: 1 });
  });

  it('\u4ec5\u6709 read \u65f6\u4e0d\u663e\u793a\u53d1\u9001\u4e0e\u5df2\u8bfb\u64cd\u4f5c', async () => {
    const wrapper = mountWithPermissions(['notifications.inbox.read']);
    await flushPromises();

    expect(wrapper.find('[data-testid="inbox-messages-send"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="inbox-messages-mark-read"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="inbox-messages-mark-all-read"]').exists()).toBe(false);
  });

  it('send-only \u53ea\u663e\u793a\u53d1\u9001\u8868\u5355', async () => {
    const wrapper = mountWithPermissions([
      'notifications.inbox.read',
      'notifications.inbox.send'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="inbox-messages-send"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="inbox-messages-recipient"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="inbox-messages-mark-read"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="inbox-messages-mark-all-read"]').exists()).toBe(false);
  });

  it('mark-read-only \u53ea\u663e\u793a\u5355\u6761\u5df2\u8bfb\u6309\u94ae', async () => {
    const wrapper = mountWithPermissions([
      'notifications.inbox.read',
      'notifications.inbox.mark_read'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="inbox-messages-send"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="inbox-messages-mark-read"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="inbox-messages-mark-all-read"]').exists()).toBe(false);
  });

  it('mark-all-read-only \u53ea\u663e\u793a\u5168\u90e8\u5df2\u8bfb\u6309\u94ae', async () => {
    const wrapper = mountWithPermissions([
      'notifications.inbox.read',
      'notifications.inbox.mark_all_read'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="inbox-messages-send"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="inbox-messages-mark-read"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="inbox-messages-mark-all-read"]').exists()).toBe(true);
  });
});