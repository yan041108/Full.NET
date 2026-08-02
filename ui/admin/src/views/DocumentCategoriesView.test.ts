import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DocumentCategoriesView from './DocumentCategoriesView.vue';
import { useSessionStore } from '../auth/session';
import {
  createHostDocumentCategory,
  deleteHostDocumentCategory,
  listHostDocumentCategories,
  updateHostDocumentCategory
} from '../api/host-document-categories';

vi.mock('../api/host-document-categories', () => ({
  createHostDocumentCategory: vi.fn(),
  deleteHostDocumentCategory: vi.fn(),
  listHostDocumentCategories: vi.fn(),
  updateHostDocumentCategory: vi.fn()
}));

const listMock = vi.mocked(listHostDocumentCategories);
const category = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
  name: 'Guides',
  parentId: null,
  sortOrder: 0,
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
  return mount(DocumentCategoriesView, { global: { plugins: [pinia] } });
}

describe('Vue 文档分类页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue([category]);
    vi.mocked(createHostDocumentCategory).mockReset();
    vi.mocked(updateHostDocumentCategory).mockReset();
    vi.mocked(deleteHostDocumentCategory).mockReset();
  });

  it('仅有 read 时不显示写入操作', async () => {
    const wrapper = mountWithPermissions(['document.categories.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-category-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-category-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-category-delete"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('Guides');
  });

  it('create-only 只显示创建按钮', async () => {
    const wrapper = mountWithPermissions(['document.categories.read', 'document.categories.create']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-category-create"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="document-category-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-category-delete"]').exists()).toBe(false);
  });

  it('update-only 只显示编辑按钮', async () => {
    const wrapper = mountWithPermissions(['document.categories.read', 'document.categories.update']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-category-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-category-edit"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="document-category-delete"]').exists()).toBe(false);
  });

  it('delete-only 只显示删除按钮', async () => {
    const wrapper = mountWithPermissions(['document.categories.read', 'document.categories.delete']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-category-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-category-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-category-delete"]').exists()).toBe(true);
  });
});
