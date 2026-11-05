import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ImportExportTasksView from './ImportExportTasksView.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  executeImportExportTask,
  getImportExportTask,
  listImportExportTasks,
  cancelImportExportTask,
  downloadImportExportTaskErrorReceipt
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
const downloadMock = vi.mocked(downloadImportExportTaskErrorReceipt);

const baseTask = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
  tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
  schemaKey: 'organization.tenant_positions',
  schemaDisplayName: '租户职位',
  worksheetKey: 'positions',
  sourceFileId: '0198f36e-f7a7-7c52-9cbb-774e67411206',
  sourceFileName: 'tenant-positions-import.xlsx',
  sourceRetention: { expiresAtUtc: '2027-01-01T00:00:00Z', stateKey: 'available' as const, releasedAtUtc: null },
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
  session.state = 'authenticated';
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    actorScope: 'tenant:019bc2b12a407cc38992a80de51bf297',
    scope: 'tenant:019bc2b12a407cc38992a80de51bf297',
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
    downloadMock.mockReset();
  });

  it.each(['expired', 'releasing', 'released'] as const)('来源 %s 保留原失败详情和到期时间并阻止再次执行', async stateKey => {
    getMock.mockResolvedValue({ ...baseTask, statusKey: 'execution_failed', executionFailedRowCount: 1, previewRows: [],
      sourceRetention: { expiresAtUtc: '2026-10-01T00:00:00Z', stateKey, releasedAtUtc: stateKey === 'released' ? '2026-10-02T00:00:00Z' : null } });
    const wrapper = mountWithPermissions(['import_export.import_tasks.read', 'import_export.import_tasks.execute']);
    await flushPromises(); await wrapper.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="import-source-expiry"]').attributes('datetime')).toBe('2026-10-01T00:00:00Z');
    expect(wrapper.get('[data-testid="import-source-unavailable"]').text()).toContain('源工作簿已到期');
    expect(wrapper.get('[data-testid="import-export-task-retry"]').attributes('disabled')).toBeDefined();
    expect(executeMock).not.toHaveBeenCalled(); wrapper.unmount();
  });

  it('队列中的过期来源继续保留且本人仍能取消', async () => {
    getMock.mockResolvedValue({ ...baseTask, statusKey: 'queued', previewRows: [],
      sourceRetention: { expiresAtUtc: '2026-10-01T00:00:00Z', stateKey: 'retained_for_execution', releasedAtUtc: null } });
    const wrapper = mountWithPermissions(['import_export.import_tasks.read', 'import_export.import_tasks.cancel', 'import_export.import_tasks.execute']);
    await flushPromises(); await wrapper.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="import-source-state"]').text()).toContain('执行期间继续保留');
    expect(wrapper.get('[data-testid="import-export-task-cancel"]').attributes('disabled')).toBeUndefined();
    expect(wrapper.find('[data-testid="import-source-unavailable"]').exists()).toBe(false); wrapper.unmount();
  });

  it('服务器确认到期后锁住原按钮，不自动重新排队或重建任务', async () => {
    executeMock.mockRejectedValue({ status: 409, code: 'import_export.source.expired', title: 'Expired' });
    const wrapper = mountWithPermissions(['import_export.import_tasks.read', 'import_export.import_tasks.execute']);
    await flushPromises(); await wrapper.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="import-export-task-execute"]').trigger('click'); await flushPromises();
    expect(executeMock).toHaveBeenCalledTimes(1);
    expect(wrapper.get('[data-testid="import-source-unavailable"]').text()).toContain('源工作簿已到期');
    expect(wrapper.get('[data-testid="import-source-state"]').text()).toContain('已到期');
    await wrapper.get('[data-testid="import-export-task-execute"]').trigger('click'); await flushPromises();
    expect(executeMock).toHaveBeenCalledTimes(1); wrapper.unmount();
  });

  it('只读用户到期后仍能读取回执，语言切换不重新读取任务', async () => {
    getMock.mockResolvedValue({ ...baseTask, statusKey: 'execution_failed', executionFailedRowCount: 1, hasErrorReceipt: true, previewRows: [],
      sourceRetention: { expiresAtUtc: '2026-10-01T00:00:00Z', stateKey: 'released', releasedAtUtc: '2026-10-02T00:00:00Z' } });
    downloadMock.mockImplementation((_id, signal) => new Promise<Blob>((_resolve, reject) =>
      signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))));
    const wrapper = mountWithPermissions(['import_export.import_tasks.read']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises();
      useAdminI18n().setLocale('en-US'); await flushPromises();
      expect(wrapper.get('[data-testid="import-source-state"]').text()).toBe('Source workbook cleaned');
      expect(getMock).toHaveBeenCalledTimes(1);
      const button = wrapper.get('[data-testid="import-export-task-error-receipt"]');
      expect(button.attributes('disabled')).toBeUndefined(); await button.trigger('click'); await flushPromises();
      expect(downloadMock).toHaveBeenCalledTimes(1);
      expect(executeMock).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); await flushPromises(); useAdminI18n().setLocale('zh-CN'); }
    expect(downloadMock.mock.calls[0]?.[1]?.aborted).toBe(true);
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
    await wrapper.get('[data-testid="import-export-task-cancel"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="import-cancel-confirm"]').trigger('click'); await flushPromises();
    expect(cancelImportExportTask).toHaveBeenCalledWith(baseTask.id, 4, expect.any(AbortSignal));
    expect(wrapper.text()).toContain('已取消'); expect(wrapper.text()).toContain('执行：1 成功');
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
    await wrapper.get('[data-testid="import-export-task-cancel"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="import-cancel-confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="import-export-task-cancel"]').attributes('disabled')).toBeDefined();
    expect(wrapper.get('[data-testid="import-export-task-execute"]').attributes('disabled')).toBeDefined();
    getMock.mockResolvedValue({ ...baseTask, statusKey: 'canceled', previewRows: [], version: 8 });
    await wrapper.get('[data-testid="import-export-task-detail-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('已取消'); expect(cancelImportExportTask).toHaveBeenCalledTimes(1); wrapper.unmount();
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
      await vi.advanceTimersByTimeAsync(5000); await flushPromises();
      expect(wrapper.text()).toContain('执行成功'); expect(getMock).toHaveBeenCalledTimes(2);
      await vi.advanceTimersByTimeAsync(6000); expect(getMock).toHaveBeenCalledTimes(2);
    } finally { wrapper.unmount(); vi.restoreAllMocks(); vi.useRealTimers(); }
  });
});
