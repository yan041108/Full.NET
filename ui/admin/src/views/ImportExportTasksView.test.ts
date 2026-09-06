import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ImportExportTasksView from './ImportExportTasksView.vue';
import { useSessionStore } from '../auth/session';
import { listImportExportTasks } from '../api/import-export-tasks';

vi.mock('../api/import-export-tasks', () => ({
  listImportExportTasks: vi.fn(),
  listStaticImportSchemas: vi.fn(),
  getImportExportTask: vi.fn(),
  createImportExportTask: vi.fn()
}));

const listMock = vi.mocked(listImportExportTasks);

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    actorScope: 'tenant',
    scope: 'tenant',
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf298',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(ImportExportTasksView, { global: { plugins: [pinia] } });
}

describe('Vue 导入任务页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [{
        id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
        tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
        schemaKey: 'organization.tenant_positions',
        schemaDisplayName: '租户职位',
        worksheetKey: 'positions',
        sourceFileId: '0198f36e-f7a7-7c52-9cbb-774e67411206',
        sourceFileName: 'tenant-positions-import.xlsx',
        statusKey: 'preview_succeeded',
        totalRows: 1,
        validRowCount: 1,
        invalidRowCount: 0,
        errorCode: null,
        requestedByUserId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
        createdAtUtc: '2026-09-06T12:00:00Z',
        previewCompletedAtUtc: '2026-09-06T12:00:01Z',
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  });

  it('仅有 read 时不显示创建按钮', async () => {
    const wrapper = mountWithPermissions(['import_export.import_tasks.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="import-export-task-create"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('租户职位');
  });

  it('create 权限显示提交按钮', async () => {
    const wrapper = mountWithPermissions([
      'import_export.import_tasks.read',
      'import_export.import_tasks.create'
    ]);
    await flushPromises();
    expect(wrapper.find('[data-testid="import-export-task-create"]').exists()).toBe(true);
  });
});
