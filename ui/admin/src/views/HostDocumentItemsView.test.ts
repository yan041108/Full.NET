import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import HostDocumentItemsView from './HostDocumentItemsView.vue';
import { getDocumentPreviewCapabilities } from '../api/document-preview-tasks';
import { useSessionStore } from '../auth/session';
import {
  createDocumentItem,
  deleteDocumentItem,
  downloadDocumentContent,
  listDocumentItems,
  openDocumentBlob,
  restoreDocumentItem,
  updateDocumentItem,
  uploadDocumentVersion
} from '../api/host-document-items';

vi.mock('../api/document-preview-tasks', () => ({ getDocumentPreviewCapabilities: vi.fn().mockResolvedValue({ enabled: true, allowedMimeTypes: ['application/msword'], maxInputBytes: 52428800 }), createDocumentPreviewTask: vi.fn() }));
vi.mock('../api/host-document-categories', () => ({
  listDocumentCategories: vi.fn().mockResolvedValue([])
}));

vi.mock('../api/host-document-items', () => ({
  createDocumentItem: vi.fn(),
  deleteDocumentItem: vi.fn(),
  downloadDocumentContent: vi.fn(),
  listDocumentItems: vi.fn(),
  openDocumentBlob: vi.fn(),
  restoreDocumentItem: vi.fn(),
  updateDocumentItem: vi.fn(),
  uploadDocumentVersion: vi.fn()
}));

const listMock = vi.mocked(listDocumentItems);
const downloadMock = vi.mocked(downloadDocumentContent);
const openBlobMock = vi.mocked(openDocumentBlob);
const item = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
  visibilityMode: 'host' as const,
  documentNo: 'DOC-000001',
  title: 'Spec',
  description: 'integration',
  categoryId: null,
  categoryName: null,
  categoryColor: null,
  categoryIcon: null,
  categoryCode: null,
  documentType: 99,
  sizeKb: 0,
  thumbnail: null,
  status: 1,
  accessCount: 0,
  sort: 0,
  lastAccessTime: null,
  currentVersion: {
    id: '0198f36e-f7a7-7c52-9cbb-774e67411206',
    versionNumber: 1,
    fileId: '0198f36e-f7a7-7c52-9cbb-774e67411207',
    contentHash: null,
    sizeBytes: 8,
    changeDescription: null,
    createdAtUtc: '2026-07-30T08:00:00Z',
    uploadedByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204'
  },
  tags: [],
  shareCode: null,
  hasPassword: false,
  permissionLevel: null,
  permissions: [],
  createdAtUtc: '2026-07-30T08:00:00Z',
  createdByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
  updatedAtUtc: null,
  updatedByUserId: null,
  deletedAtUtc: null,
  deletedByUserId: null,
  version: 2
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
    passwordChangeRequired: false,
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

  it('归档文档隐藏编辑、追加版本与分享，只展示恢复编辑命令', async () => {
    listMock.mockResolvedValueOnce({ items: [{ ...item, status: 2 }], page: 1, pageSize: 20, total: 1 });
    const wrapper = mountWithPermissions(['document.host_documents.read', 'document.host_documents.update',
      'document.host_documents.add_version', 'document.host_shares.create', 'document.host_documents.unarchive']);
    await flushPromises();
    for (const action of ['edit', 'upload-version', 'share'])
      expect(wrapper.find(`[data-testid="host-document-item-${action}"]`).exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-document-item-unarchive"]').exists()).toBe(true);
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

  it('share create 权限显示分享按钮', async () => {
    const wrapper = mountWithPermissions([
      'document.host_documents.read',
      'document.host_shares.create'
    ]);
    await flushPromises();
    expect(wrapper.find('[data-testid="host-document-item-share"]').exists()).toBe(true);
  });

  it('delete-only 只显示删除按钮', async () => {
    const wrapper = mountWithPermissions(['document.host_documents.read', 'document.host_documents.delete']);
    await flushPromises();
    expect(wrapper.find('[data-testid="host-document-item-delete"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-document-item-restore"]').exists()).toBe(false);
  });
  it('使用服务端总数分页，组合筛选会发送新请求并回到第一页', async () => {
    listMock.mockResolvedValue({ items: [item], page: 1, pageSize: 20, total: 123 });
    const wrapper = mountWithPermissions(['document.host_documents.read']);
    await flushPromises();
    const pagination = wrapper.findComponent({ name: 'ElPagination' });
    expect(pagination.props('total')).toBe(123);
    pagination.vm.$emit('update:current-page', 2);
    pagination.vm.$emit('current-change', 2);
    await flushPromises();
    expect(listMock).toHaveBeenLastCalledWith(2, 20, expect.any(Object), expect.any(AbortSignal));
    wrapper.findComponent({ name: 'ArtSearchBar' }).vm.$emit('search', { title: '第 101 条', status: '0', documentType: '2' });
    await flushPromises();
    expect(listMock).toHaveBeenLastCalledWith(1, 20,
      expect.objectContaining({ keyword: '第 101 条', status: 0, documentType: 2 }), expect.any(AbortSignal));
  });

  it('晚返回的旧查询不能覆盖新筛选结果', async () => {
    let resolveOld!: (value: Awaited<ReturnType<typeof listDocumentItems>>) => void;
    listMock.mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }));
    const wrapper = mountWithPermissions(['document.host_documents.read']);
    await flushPromises();
    listMock.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0 });
    wrapper.findComponent({ name: 'ArtSearchBar' }).vm.$emit('search', { title: 'empty' });
    await flushPromises();
    resolveOld({ items: [item], page: 1, pageSize: 20, total: 99 });
    await flushPromises();
    expect(wrapper.findComponent({ name: 'ElPagination' }).props('total')).toBe(0);
    expect(wrapper.findComponent({ name: 'ElTable' }).props('data')).toEqual([]);
  });

  it('编辑支持显式清空分类和缩略图，描述及标签为空时提交清空值', async () => {
    const wrapper = mountWithPermissions(['document.host_documents.read', 'document.host_documents.update']);
    await flushPromises();
    await wrapper.get('[data-testid="host-document-item-edit"]').trigger('click');
    await flushPromises();
    wrapper.findComponent({ name: 'ArtFormDialog' }).findComponent({ name: 'ElSelect' }).vm.$emit('update:modelValue', null);
    const inputs = wrapper.findComponent({ name: 'ArtFormDialog' }).findAllComponents({ name: 'ElInput' });
    inputs.find(input => input.props('type') === 'textarea')!.vm.$emit('update:modelValue', '');
    inputs.find(input => input.props('clearable'))!.vm.$emit('update:modelValue', '');
    await flushPromises();
    wrapper.findComponent({ name: 'ArtFormDialog' }).vm.$emit('confirm');
    await flushPromises();
    expect(updateDocumentItem).toHaveBeenLastCalledWith(item, 'Spec', null,
      { tagIds: [], categoryId: null, thumbnail: null, sort: 0 });
  });

  it.each([
    ['APPLICATION/MSWORD; charset=utf-8', true, 8, true],
    ['application/pdf', true, 8, false],
    ['application/vnd.ms-excel', true, 8, false],
    ['application/msword', false, 8, false],
    ['application/msword', true, 52428801, false]
  ])('Office 入口依据服务端 MIME、启用状态和大小：%s/%s/%s', async (mimeType, enabled, sizeBytes, visible) => {
    vi.mocked(getDocumentPreviewCapabilities).mockResolvedValueOnce({ enabled: enabled as boolean, allowedMimeTypes: ['application/msword'], maxInputBytes: 52428800, maxAttempts: 3, providerKey: 'external_http' });
    listMock.mockResolvedValueOnce({ items: [{ ...item, mimeType: mimeType as string, currentVersion: { ...item.currentVersion, sizeBytes: sizeBytes as number } }], page: 1, pageSize: 20, total: 1 });
    const wrapper = mountWithPermissions(['document.host_documents.read', 'document.host_preview_tasks.create']); await flushPromises();
    expect(wrapper.find('[data-testid="host-document-item-create-preview-task"]').exists()).toBe(visible);
    wrapper.unmount();
  });

});
