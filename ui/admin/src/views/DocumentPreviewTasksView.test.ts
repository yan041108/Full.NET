import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DocumentPreviewTasksView from './DocumentPreviewTasksView.vue';
import { useSessionStore } from '../auth/session';
import { listDocumentPreviewTasks } from '../api/document-preview-tasks';

vi.mock('../api/document-preview-tasks', () => ({
  listDocumentPreviewTasks: vi.fn(),
  createDocumentPreviewTask: vi.fn(),
  openDocumentPreviewTaskContent: vi.fn()
}));

const listMock = vi.mocked(listDocumentPreviewTasks);

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
  return mount(DocumentPreviewTasksView, { global: { plugins: [pinia] } });
}

describe('Vue 文档预览任务页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [{
        id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
        documentItemId: '0198f36e-f7a7-7c52-9cbb-774e67411206',
        documentTitle: 'Quarterly report',
        versionId: null,
        sourceFileId: '0198f36e-f7a7-7c52-9cbb-774e67411207',
        outputFileId: null,
        statusKey: 'pending',
        providerKey: 'disabled',
        errorCode: null,
        requestedByUserId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
        createdAtUtc: '2026-09-06T12:00:00Z',
        startedAtUtc: null,
        completedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  });

  it('仅有 read 时不显示创建按钮', async () => {
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-preview-task-create"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('Quarterly report');
  });

  it('create 权限显示提交按钮', async () => {
    const wrapper = mountWithPermissions([
      'document.host_preview_tasks.read',
      'document.host_preview_tasks.create'
    ]);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-preview-task-create"]').exists()).toBe(true);
  });
});
