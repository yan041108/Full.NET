import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DocumentTagsView from './DocumentTagsView.vue';
import { useSessionStore } from '../auth/session';
import {
  createHostDocumentTag,
  deleteHostDocumentTag,
  listHostDocumentTags,
  updateHostDocumentTag
} from '../api/host-document-tags';

vi.mock('../api/host-document-tags', () => ({
  createHostDocumentTag: vi.fn(),
  deleteHostDocumentTag: vi.fn(),
  listHostDocumentTags: vi.fn(),
  updateHostDocumentTag: vi.fn()
}));

const listMock = vi.mocked(listHostDocumentTags);
const tag = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
  name: 'Release',
  createdAtUtc: '2026-07-30T08:00:00Z',
  createdByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
  updatedAtUtc: null,
  updatedByUserId: null,
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
  return mount(DocumentTagsView, { global: { plugins: [pinia] } });
}

describe('Vue 文档标签页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue([tag]);
    vi.mocked(createHostDocumentTag).mockReset();
    vi.mocked(updateHostDocumentTag).mockReset();
    vi.mocked(deleteHostDocumentTag).mockReset();
  });

  it('仅有 read 时不显示写入操作', async () => {
    const wrapper = mountWithPermissions(['document.tags.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-tag-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-tag-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-tag-delete"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('Release');
  });

  it('create-only 只显示创建按钮', async () => {
    const wrapper = mountWithPermissions(['document.tags.read', 'document.tags.create']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-tag-create"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="document-tag-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-tag-delete"]').exists()).toBe(false);
  });

  it('update-only 只显示编辑按钮', async () => {
    const wrapper = mountWithPermissions(['document.tags.read', 'document.tags.update']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-tag-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-tag-edit"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="document-tag-delete"]').exists()).toBe(false);
  });

  it('delete-only 只显示删除按钮', async () => {
    const wrapper = mountWithPermissions(['document.tags.read', 'document.tags.delete']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-tag-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-tag-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-tag-delete"]').exists()).toBe(true);
  });
});
