import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElPopconfirm } from 'element-plus';
import ImportExportTasksView from './ImportExportTasksView.vue';
import { useSessionStore } from '../auth/session';
import {
  executeImportExportTask,
  getImportExportTask,
  listImportExportTasks,
  cancelImportExportTask
} from '../api/import-export-tasks';

vi.mock('../api/import-export-tasks', () => ({
  listImportExportTasks: vi.fn(),
  listStaticImportSchemas: vi.fn(),
  getImportExportTask: vi.fn(),
  createImportExportTask: vi.fn(),
  executeImportExportTask: vi.fn(),
  resumeImportExportTask: vi.fn(),
  retryImportExportTask: vi.fn(),
  cancelImportExportTask: vi.fn(),
  downloadImportExportTaskErrorReceipt: vi.fn(),
  downloadStaticImportTemplate: vi.fn()
}));

const listMock = vi.mocked(listImportExportTasks);
const getMock = vi.mocked(getImportExportTask);
const executeMock = vi.mocked(executeImportExportTask);

const baseTask = {
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
  processedRowCount: 0,
  succeededRowCount: 0,
  executionFailedRowCount: 0,
  nextLineNumber: 0,
  executionStartedAtUtc: null,
  executionCompletedAtUtc: null,
  hasErrorReceipt: false,
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
    tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    actorScope: 'tenant',
    scope: 'tenant',
    isSuperAdministrator: false,
    passwordChangeRequired: false,
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
      items: [baseTask],
      page: 1,
      pageSize: 20,
      total: 1
    });
    getMock.mockReset().mockResolvedValue({
      ...baseTask,
      previewRows: []
    });
    executeMock.mockReset();
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
      'import_export.import_tasks.create',
      'import_export.static_schemas.read'
    ]);
    await flushPromises();
    expect(wrapper.find('[data-testid="import-export-task-create"]').exists()).toBe(true);
  });

  it('execute 权限在详情抽屉显示执行按钮', async () => {
    const wrapper = mountWithPermissions([
      'import_export.import_tasks.read',
      'import_export.import_tasks.execute'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="import-export-task-detail"]').trigger('click');
    await flushPromises();
    expect(wrapper.find('[data-testid="import-export-task-execute"]').exists()).toBe(true);
  });

  it('会话切换取消详情请求并忽略旧租户迟到结果', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof getImportExportTask>>) => void;
    getMock.mockImplementation(() => new Promise(done => { resolve = done; }));
    const wrapper = mountWithPermissions(['import_export.import_tasks.read', 'import_export.import_tasks.execute']);
    await flushPromises(); await wrapper.get('[data-testid="import-export-task-detail"]').trigger('click');
    const signal = getMock.mock.calls[0]![1]!;
    useSessionStore().switching = true; await flushPromises();
    expect(signal.aborted).toBe(true);
    resolve({ ...baseTask, previewRows: [] }); await flushPromises();
    expect(wrapper.find('[data-testid="import-export-task-execute"]').exists()).toBe(false);
    expect(wrapper.text()).not.toContain('租户职位'); wrapper.unmount();
  });

  it('具有通用执行权限也不能显示他人的排队操作', async () => {
    getMock.mockResolvedValue({ ...baseTask, requestedByUserId: '019bc2b1-2a40-7cc3-8992-a80de51bf299', previewRows: [] });
    const wrapper = mountWithPermissions(['import_export.import_tasks.read', 'import_export.import_tasks.execute']);
    await flushPromises(); await wrapper.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="import-export-task-execute"]').exists()).toBe(false); wrapper.unmount();
  });

  it('本人取消使用独立权限和详情版本，保留进度并停止后续操作', async () => {
    getMock.mockResolvedValue({ ...baseTask, statusKey: 'queued', processedRowCount: 1, succeededRowCount: 1, previewRows: [], version: 4 });
    vi.mocked(cancelImportExportTask).mockResolvedValue({ ...baseTask, statusKey: 'canceled', processedRowCount: 1, succeededRowCount: 1, previewRows: [], version: 5 });
    const wrapper = mountWithPermissions(['import_export.import_tasks.read', 'import_export.import_tasks.cancel']);
    await flushPromises(); await wrapper.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="import-export-task-cancel"]').exists()).toBe(true);
    wrapper.findComponent(ElPopconfirm).vm.$emit('confirm'); await flushPromises();
    expect(cancelImportExportTask).toHaveBeenCalledWith(baseTask.id, 4, expect.any(AbortSignal));
    expect(wrapper.text()).toContain('任务已取消'); expect(wrapper.text()).toContain('执行：1 成功');
    expect(wrapper.find('[data-testid="import-export-task-cancel"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="import-export-task-execute"]').exists()).toBe(false); wrapper.unmount();
  });

  it('执行中不允许取消，并说明等待批次结束及不回滚语义', async () => {
    getMock.mockResolvedValue({ ...baseTask, statusKey: 'executing', previewRows: [] });
    const wrapper = mountWithPermissions(['import_export.import_tasks.read', 'import_export.import_tasks.cancel']);
    await flushPromises(); await wrapper.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="import-export-task-cancel"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('当前批次正在执行'); wrapper.unmount();
  });

  it('未知取消结果先显式重读，不能用旧版本再提交', async () => {
    getMock.mockResolvedValue({ ...baseTask, previewRows: [], version: 7 }); vi.mocked(cancelImportExportTask).mockRejectedValue(new Error('timeout'));
    const wrapper = mountWithPermissions(['import_export.import_tasks.read', 'import_export.import_tasks.cancel', 'import_export.import_tasks.execute']);
    await flushPromises(); await wrapper.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises();
    wrapper.findComponent(ElPopconfirm).vm.$emit('confirm'); await flushPromises();
    expect(wrapper.get('[data-testid="import-export-task-cancel"]').attributes('disabled')).toBeDefined();
    expect(wrapper.get('[data-testid="import-export-task-execute"]').attributes('disabled')).toBeDefined();
    getMock.mockResolvedValue({ ...baseTask, statusKey: 'canceled', previewRows: [], version: 8 });
    await wrapper.get('[data-testid="import-export-task-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('任务已取消'); expect(cancelImportExportTask).toHaveBeenCalledTimes(1); wrapper.unmount();
  });

  it('异步执行排队后自动呈现终态并同步列表', async () => {
    vi.useFakeTimers();
    const wrapper = mountWithPermissions(['import_export.import_tasks.read', 'import_export.import_tasks.execute']);
    try {
      vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
      await flushPromises(); await wrapper.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises();
      executeMock.mockResolvedValue({ ...baseTask, statusKey: 'queued', previewRows: [] });
      getMock.mockResolvedValue({ ...baseTask, statusKey: 'execution_succeeded', succeededRowCount: 1, previewRows: [] });
      await wrapper.get('[data-testid="import-export-task-execute"]').trigger('click'); await flushPromises();
      expect(wrapper.text()).toContain('排队中');
      await vi.advanceTimersByTimeAsync(2000); await flushPromises();
      expect(wrapper.text()).toContain('执行成功'); expect(getMock).toHaveBeenCalledTimes(2);
      await vi.advanceTimersByTimeAsync(6000); expect(getMock).toHaveBeenCalledTimes(2);
    } finally { wrapper.unmount(); vi.restoreAllMocks(); vi.useRealTimers(); }
  });
});
