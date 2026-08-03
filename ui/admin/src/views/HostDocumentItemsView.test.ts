import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import HostDocumentItemsView from './HostDocumentItemsView.vue';
import { useSessionStore } from '../auth/session';
import {
  createHostDocumentItem,
  deleteHostDocumentItem,
  downloadHostDocumentContent,
  listHostDocumentItems,
  openHostDocumentBlob,
  restoreHostDocumentItem,
  updateHostDocumentItem,
  uploadHostDocumentVersion
} from '../api/host-document-items';

vi.mock('../api/host-document-items', () => ({
  createHostDocumentItem: vi.fn(),
  deleteHostDocumentItem: vi.fn(),
  downloadHostDocumentContent: vi.fn(),
  listHostDocumentItems: vi.fn(),
  openHostDocumentBlob: vi.fn(),
  restoreHostDocumentItem: vi.fn(),
  updateHostDocumentItem: vi.fn(),
  uploadHostDocumentVersion: vi.fn()
}));

const listMock = vi.mocked(listHostDocumentItems);
const downloadMock = vi.mocked(downloadHostDocumentContent);
const openBlobMock = vi.mocked(openHostDocumentBlob);
const item = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
  title: 'Spec',
  description: 'integration',
  categoryId: null,
  currentVersion: {
    id: '0198f36e-f7a7-7c52-9cbb-774e67411206',
    versionNumber: 1,
    fileId: '0198f36e-f7a7-7c52-9cbb-774e67411207',
    contentHash: null,
    sizeBytes: 8,
    createdAtUtc: '2026-07-30T08:00:00Z',
    uploadedByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204'
  },
  createdByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
  version: 2,
  createdAtUtc: '2026-07-30T08:00:00Z',
  updatedAtUtc: null,
  updatedByUserId: null
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
  return mount(HostDocumentItemsView, { global: { plugins: [pinia] } });
}

describe('Vue Host 文档库页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [item],
      page: 1,
      pageSize: 20,
      total: 1
    });
    downloadMock.mockReset().mockResolvedValue(new Blob(['document']));
    openBlobMock.mockReset();
  });

  it('仅有 read 时不显示写入操作', async () => {
    const wrapper = mountWithPermissions(['document.host_documents.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="host-document-item-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-document-item-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-document-item-upload-version"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-document-item-delete"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-document-item-restore"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-document-item-download"]').exists()).toBe(false);
  });

  it('具备 Document 下载权限时使用认证请求下载文档', async () => {
    const wrapper = mountWithPermissions([
      'document.host_documents.read',
      'document.host_documents.download'
    ]);
    await flushPromises();

    await wrapper.get('[data-testid="host-document-item-download"]').trigger('click');
    await flushPromises();

    expect(downloadMock).toHaveBeenCalledWith(item.id);
    expect(openBlobMock).toHaveBeenCalledOnce();
  });

  it('create-only 只显示创建按钮', async () => {
    const wrapper = mountWithPermissions(['document.host_documents.read', 'document.host_documents.create']);
    await flushPromises();
    expect(wrapper.find('[data-testid="host-document-item-create"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-document-item-edit"]').exists()).toBe(false);
  });

  it('update-only 只显示编辑按钮', async () => {
    const wrapper = mountWithPermissions(['document.host_documents.read', 'document.host_documents.update']);
    await flushPromises();
    expect(wrapper.find('[data-testid="host-document-item-edit"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-document-item-create"]').exists()).toBe(false);
  });

  it('add_version 权限即可显示上传新版本按钮', async () => {
    const wrapper = mountWithPermissions([
      'document.host_documents.read',
      'document.host_documents.add_version'
    ]);
    await flushPromises();
    expect(wrapper.find('[data-testid="host-document-item-upload-version"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-document-item-create"]').exists()).toBe(false);
  });

  it('delete-only 只显示删除按钮', async () => {
    const wrapper = mountWithPermissions(['document.host_documents.read', 'document.host_documents.delete']);
    await flushPromises();
    expect(wrapper.find('[data-testid="host-document-item-delete"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-document-item-restore"]').exists()).toBe(false);
  });
});