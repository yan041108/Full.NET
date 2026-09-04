import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import HostAnnouncementsView from './HostAnnouncementsView.vue';
import { useSessionStore } from '../auth/session';
import {
  createHostAnnouncement,
  listHostAnnouncements,
  publishHostAnnouncement,
  retractHostAnnouncement,
  updateHostAnnouncement
} from '../api/host-announcements';

vi.mock('../api/host-announcements', () => ({
  createHostAnnouncement: vi.fn(),
  listHostAnnouncements: vi.fn(),
  publishHostAnnouncement: vi.fn(),
  retractHostAnnouncement: vi.fn(),
  updateHostAnnouncement: vi.fn()
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

vi.mock('../api/tenants', () => ({
  listHostTenants: vi.fn().mockResolvedValue({
    items: [
      {
        id: '01912345-6789-7abc-8def-0123456789ae',
        identifier: 'dev',
        name: 'Development',
        domain: 'dev.local',
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

vi.mock('../api/host-user-organization-reference', () => ({
  getHostUserOrganizationReference: vi.fn().mockResolvedValue({
    units: [
      {
        id: '01912345-6789-7abc-8def-0123456789af',
        code: 'root',
        name: 'Root',
        parentId: null,
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-07-26T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }
    ],
    positions: [],
    userUnits: [],
    userPositions: []
  })
}));

vi.mock('../notifications/realtime', () => ({
  useNotificationsRealtime: () => ({
    announcementRevision: { value: 0 }
  })
}));

const listMock = vi.mocked(listHostAnnouncements);

const draftAnnouncement = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  title: 'draft-title',
  content: 'draft-content',
  kind: 'announcement' as const,
  audienceKind: 'all' as const,
  status: 'draft' as const,
  publishedAtUtc: null,
  publishedByUserId: null,
  retractedAtUtc: null,
  retractedByUserId: null,
  targetUserIds: [],
  targetOrganizations: [],
  createdAtUtc: '2026-07-26T00:00:00Z',
  updatedAtUtc: '2026-07-26T00:00:00Z',
  version: 1
};

const publishedAnnouncement = {
  ...draftAnnouncement,
  status: 'published' as const,
  publishedAtUtc: '2026-07-26T01:00:00Z',
  publishedByUserId: '01912345-6789-7abc-8def-0123456789ac',
  version: 2
};

function mountWithPermissions(permissions: string[], items = [draftAnnouncement]) {
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
  listMock.mockResolvedValue({
    items,
    page: 1,
    pageSize: 20,
    total: items.length
  });
  return mount(HostAnnouncementsView, { global: { plugins: [pinia] } });
}

describe('Vue Host \u516c\u544a\u7ba1\u7406\u9875', () => {
  beforeEach(() => {
    listMock.mockReset();
  });

  it('\u4ec5\u6709 read \u65f6\u4e0d\u663e\u793a\u521b\u5efa\u8868\u5355\u4e0e\u884c\u5185\u64cd\u4f5c', async () => {
    const wrapper = mountWithPermissions(['notifications.announcements.read']);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-announcements-action-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-announcements-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-announcements-publish"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-announcements-retract"]').exists()).toBe(false);
  });

  it('create-only \u53ea\u663e\u793a\u521b\u5efa\u8868\u5355', async () => {
    const wrapper = mountWithPermissions([
      'notifications.announcements.read',
      'notifications.announcements.create'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-announcements-action-create"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-announcements-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-announcements-publish"]').exists()).toBe(false);
  });

  it('update-only \u53ea\u663e\u793a\u7f16\u8f91\u6309\u94ae', async () => {
    const wrapper = mountWithPermissions([
      'notifications.announcements.read',
      'notifications.announcements.update'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-announcements-action-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-announcements-edit"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-announcements-publish"]').exists()).toBe(false);
  });

  it('publish-only \u53ea\u663e\u793a\u53d1\u5e03\u6309\u94ae', async () => {
    const wrapper = mountWithPermissions([
      'notifications.announcements.read',
      'notifications.announcements.publish'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-announcements-action-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-announcements-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-announcements-publish"]').exists()).toBe(true);
  });

  it('retract-only \u53ea\u663e\u793a\u64a4\u56de\u6309\u94ae', async () => {
    const wrapper = mountWithPermissions(
      ['notifications.announcements.read', 'notifications.announcements.retract'],
      [publishedAnnouncement]
    );
    await flushPromises();

    expect(wrapper.find('[data-testid="host-announcements-action-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-announcements-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-announcements-publish"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-announcements-retract"]').exists()).toBe(true);
  });

  it('users \u53d7\u4f17\u5217\u8868\u663e\u793a\u6458\u8981', async () => {
    const usersAnnouncement = {
      ...draftAnnouncement,
      audienceKind: 'users' as const,
      targetUserIds: ['01912345-6789-7abc-8def-0123456789ad']
    };
    const wrapper = mountWithPermissions(['notifications.announcements.read'], [usersAnnouncement]);
    await flushPromises();

    expect(wrapper.text()).toContain('1 \u4f4d\u7528\u6237');
  });
});
