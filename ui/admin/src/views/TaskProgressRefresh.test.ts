import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import ImportExportTasksView from './ImportExportTasksView.vue';
import ReportingExportTasksView from './ReportingExportTasksView.vue';
import DocumentPreviewTasksView from './DocumentPreviewTasksView.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import * as imports from '../api/import-export-tasks';
import * as exports from '../api/reporting-export-tasks';
import * as previews from '../api/document-preview-tasks';
import { createOutputSession, deferred, exportTask } from '../test/data-output-fixtures';
import { importTask, documentPreviewTask } from '../test/task-lifecycle-fixtures';

vi.mock('../api/import-export-tasks', () => ({
  listImportExportTasks: vi.fn(), getImportExportTask: vi.fn(), executeImportExportTask: vi.fn(),
  resumeImportExportTask: vi.fn(), retryImportExportTask: vi.fn(), downloadImportExportTaskErrorReceipt: vi.fn(),
  listStaticImportSchemas: vi.fn(), createImportExportTask: vi.fn(), downloadStaticImportTemplate: vi.fn()
}));
vi.mock('../api/reporting-definitions', () => ({ listReportingDefinitions: vi.fn() }));
vi.mock('../api/reporting-export-tasks', () => ({ listReportingExportTasks: vi.fn(), createReportingExportTask: vi.fn(), downloadReportingExportTask: vi.fn() }));
vi.mock('../api/document-preview-tasks', () => ({ listDocumentPreviewTasks: vi.fn(), createDocumentPreviewTask: vi.fn(), openDocumentPreviewTaskContent: vi.fn() }));

let wrapper: VueWrapper | undefined;
const cases = [
  { name: '导入', view: ImportExportTasksView, permission: 'import_export.import_tasks.read', list: vi.mocked(imports.listImportExportTasks), task: importTask, pending: 'queued' },
  { name: '报表导出', view: ReportingExportTasksView, permission: 'reporting.export_tasks.read', list: vi.mocked(exports.listReportingExportTasks), task: exportTask, pending: 'processing' },
  { name: '文档预览', view: DocumentPreviewTasksView, permission: 'document.host_preview_tasks.read', list: vi.mocked(previews.listDocumentPreviewTasks), task: documentPreviewTask, pending: 'pending' }
] as const;
const page = (task: typeof importTask | typeof exportTask | typeof documentPreviewTask) => ({ items: [task], page: 1, pageSize: 20, total: 1 });
const tick = async (milliseconds = 5_000) => { await vi.advanceTimersByTimeAsync(milliseconds); await flushPromises(); };
beforeEach(() => {
  vi.clearAllMocks();
  for (const api of [imports, exports, previews]) for (const operation of Object.values(api)) {
    if (vi.isMockFunction(operation)) operation.mockReset();
  }
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }); vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); vi.useRealTimers(); });
async function setup(entry: typeof cases[number]) {
  const context = createOutputSession([entry.permission]);
  wrapper = mount(entry.view, { global: { plugins: [context.pinia], directives: { loading: {} }, stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot /><slot name="footer" /></div>' }
  } } });
  await flushPromises(); return context.session;
}

describe.each(cases)('$name任务进度', entry => {
  it('只有终态或未知状态不进行后台读取', async () => {
    entry.list.mockResolvedValue({ items: [entry.task, { ...entry.task, statusKey: 'unknown' }], page: 1, pageSize: 20, total: 2 } as never);
    await setup(entry); await tick(20_000); expect(entry.list).toHaveBeenCalledTimes(1);
  });
  it('有在途任务定期刷新，终态后停止', async () => {
    entry.list.mockResolvedValueOnce(page({ ...entry.task, statusKey: entry.pending }) as never).mockResolvedValue(page(entry.task) as never);
    await setup(entry); await tick(4_999); expect(entry.list).toHaveBeenCalledTimes(1);
    await tick(1); expect(entry.list).toHaveBeenCalledTimes(2);
    await tick(20_000); expect(entry.list).toHaveBeenCalledTimes(2);
  });
  it('在途请求不重叠，失败停止；手动刷新后可恢复', async () => {
    const pending = deferred<ReturnType<typeof page>>();
    entry.list.mockResolvedValueOnce(page({ ...entry.task, statusKey: entry.pending }) as never).mockReturnValueOnce(pending.promise as never);
    await setup(entry); await tick(); expect(entry.list).toHaveBeenCalledTimes(2);
    await tick(20_000); expect(entry.list).toHaveBeenCalledTimes(2);
    pending.reject(new Error('refresh failed')); await flushPromises(); await tick(20_000);
    expect(entry.list).toHaveBeenCalledTimes(2);
    entry.list.mockResolvedValue(page({ ...entry.task, statusKey: entry.pending }) as never);
    wrapper!.getComponent(ArtTableHeader).vm.$emit('refresh'); await flushPromises(); await tick();
    expect(entry.list).toHaveBeenCalledTimes(4);
  });
  it('隐藏暂停、返回继续；撤权及卸载不再请求', async () => {
    entry.list.mockResolvedValue(page({ ...entry.task, statusKey: entry.pending }) as never);
    const session = await setup(entry);
    vi.spyOn(document, 'hidden', 'get').mockReturnValue(true); document.dispatchEvent(new Event('visibilitychange'));
    await tick(20_000); expect(entry.list).toHaveBeenCalledTimes(1);
    vi.spyOn(document, 'hidden', 'get').mockReturnValue(false); document.dispatchEvent(new Event('visibilitychange'));
    await tick(); expect(entry.list).toHaveBeenCalledTimes(2);
    session.currentUser!.permissions = []; await flushPromises(); await tick(20_000);
    expect(entry.list).toHaveBeenCalledTimes(2); wrapper!.unmount(); wrapper = undefined;
    await tick(20_000); expect(entry.list).toHaveBeenCalledTimes(2);
  });
});

it('导入抽屉自动更新执行进度，终态后不再读取详情', async () => {
  const entry = cases[0]; entry.list.mockResolvedValue(page({ ...importTask, statusKey: 'executing' }) as never);
  vi.mocked(imports.getImportExportTask).mockResolvedValueOnce({ ...importTask, statusKey: 'executing' })
    .mockResolvedValue({ ...importTask, statusKey: 'execution_succeeded', processedRowCount: 1, succeededRowCount: 1 });
  await setup(entry); await wrapper!.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises();
  await tick(); expect(imports.getImportExportTask).toHaveBeenCalledTimes(2);
  expect(wrapper!.text()).toContain('执行成功');
  await tick(10_000); expect(imports.getImportExportTask).toHaveBeenCalledTimes(2);
});

it('导入轮询列表等待期间撤权，旧轮次不能继续请求详情或显示敏感内容', async () => {
  const entry = cases[0]; const waiting = deferred<ReturnType<typeof page>>();
  entry.list.mockResolvedValueOnce(page({ ...importTask, statusKey: 'executing' }) as never).mockReturnValueOnce(waiting.promise as never);
  vi.mocked(imports.getImportExportTask).mockResolvedValue({ ...importTask, statusKey: 'executing' });
  const session = await setup(entry); await wrapper!.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises(); await tick();
  session.currentUser!.permissions = []; await flushPromises();
  expect(entry.list.mock.calls[1]?.[3]?.aborted).toBe(true);
  waiting.resolve(page(importTask)); await flushPromises(); await tick(20_000);
  expect(imports.getImportExportTask).toHaveBeenCalledTimes(1); expect(wrapper!.find('[data-testid="import-task-preview-rows"]').exists()).toBe(false);
});

it('手动列表替换自动列表请求后，旧轮次不得继续读取详情', async () => {
  const entry = cases[0]; const oldRead = deferred<ReturnType<typeof page>>(); const manualRead = deferred<ReturnType<typeof page>>();
  entry.list.mockResolvedValueOnce(page({ ...importTask, statusKey: 'executing' }) as never)
    .mockReturnValueOnce(oldRead.promise as never).mockReturnValueOnce(manualRead.promise as never);
  vi.mocked(imports.getImportExportTask).mockResolvedValue({ ...importTask, statusKey: 'executing' });
  await setup(entry); await wrapper!.get('[data-testid="import-export-task-detail"]').trigger('click'); await flushPromises(); await tick();
  wrapper!.getComponent(ArtTableHeader).vm.$emit('refresh'); await flushPromises();
  expect(entry.list.mock.calls[1]?.[3]?.aborted).toBe(true);
  oldRead.resolve(page(importTask)); await flushPromises();
  expect(imports.getImportExportTask).toHaveBeenCalledTimes(1);
  manualRead.resolve(page({ ...importTask, statusKey: 'execution_succeeded' })); await flushPromises();
});
