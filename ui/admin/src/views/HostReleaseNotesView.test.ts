import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { HostReleaseNote } from '@fullnet/client-contracts';
import HostReleaseNotesView from './HostReleaseNotesView.vue';
import { useSessionStore } from '../auth/session';
import { listHostReleaseNotes } from '../api/host-release-notes';

vi.mock('../api/host-release-notes', () => ({
  createHostReleaseNote: vi.fn(),
  listHostReleaseNotes: vi.fn(),
  publishHostReleaseNote: vi.fn(),
  retractHostReleaseNote: vi.fn(),
  updateHostReleaseNote: vi.fn(),
  deleteHostReleaseNote: vi.fn()
}));

const listMock = vi.mocked(listHostReleaseNotes);

const draftNote: HostReleaseNote = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  versionLabel: '1.0.0',
  versionSortKey: 1000000,
  title: 'Initial release',
  content: 'First changelog entry',
  status: 'draft',
  publishedAtUtc: null,
  publishedByUserId: null,
  retractedAtUtc: null,
  retractedByUserId: null,
  createdAtUtc: '2026-09-06T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

function mountWithPermissions(permissions: string[], items = [draftNote]) {
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
  listMock.mockResolvedValue({
    items,
    page: 1,
    pageSize: 20,
    total: items.length
  });
  return mount(HostReleaseNotesView, { global: { plugins: [pinia] } });
}

describe('HostReleaseNotesView', () => {
  beforeEach(() => {
    listMock.mockReset();
  });

  it('hides mutating actions without permissions', async () => {
    const wrapper = mountWithPermissions(['platform.release_notes.read']);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-release-notes-action-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-release-notes-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-release-notes-publish"]').exists()).toBe(false);
  });

  it('shows create action when create permission is granted', async () => {
    const wrapper = mountWithPermissions([
      'platform.release_notes.read',
      'platform.release_notes.create'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-release-notes-action-create"]').exists()).toBe(true);
  });
});
