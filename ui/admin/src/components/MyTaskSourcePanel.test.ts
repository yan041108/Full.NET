import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import MyTaskSourcePanel from './MyTaskSourcePanel.vue';
import { useSessionStore } from '../auth/session';
const api = vi.hoisted(() => ({ listMyImportTasks: vi.fn(), listMyReportingTasks: vi.fn(), listMyAgentRuns: vi.fn(), push: vi.fn(),
  getImportExportTask: vi.fn(), executeImportExportTask: vi.fn(), resumeImportExportTask: vi.fn(), retryImportExportTask: vi.fn(),
  downloadImportExportTaskErrorReceipt: vi.fn(), getReportingExportTask: vi.fn(), downloadReportingExportTask: vi.fn() }));
vi.mock('../api/my-tasks', () => api);
vi.mock('../api/import-export-tasks', () => api);
vi.mock('../api/reporting-export-tasks', () => api);
vi.mock('vue-router', async importOriginal => ({ ...await importOriginal<typeof import('vue-router')>(), useRouter: () => ({ push: api.push }) }));
const wrappers: VueWrapper[] = [];
const imported = (statusKey = 'preview_succeeded') => ({ id: 'i', requestedByUserId: 'user', tenantId: 'tenant', sourceFileName: 'Orders.xlsx',
  schemaKey: 'orders', statusKey, validRowCount: 2, processedRowCount: 0 });
const reported = (statusKey = 'succeeded') => ({ id: 'r', requestedByUserId: 'user', definitionName: 'Report', statusKey });
const page = (items: unknown[] = [], nextCursor: string | null = null) => ({ items, nextCursor, hasMore: nextCursor !== null });
function render(source: 'imports' | 'reports' | 'agents' = 'imports', permissions = ['platform.my_tasks.read', 'import_export.import_tasks.read', 'import_export.import_tasks.execute', 'reporting.export_tasks.read', 'reporting.export_tasks.download']) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.state = 'authenticated';
  session.currentUser = { id: 'user', tenantId: 'tenant', sessionId: 'session', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(MyTaskSourcePanel, { props: { source }, global: { plugins: [pinia] } }); wrappers.push(wrapper); return { wrapper, session };
}
beforeEach(() => { vi.resetAllMocks(); api.listMyImportTasks.mockResolvedValue(page()); api.listMyReportingTasks.mockResolvedValue(page()); api.listMyAgentRuns.mockResolvedValue(page()); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); vi.useRealTimers(); });
describe('来源独立任务面板', () => {
  it('AI 等待态只链接原详情，撤权取消独立读取并清空摘要', async () => {
    api.listMyAgentRuns.mockResolvedValue(page([{ id: 'run', definitionKey: 'rename-workflow', statusKey: 'awaiting_approval' }]));
    const { wrapper, session } = render('agents', ['platform.my_tasks.read', 'ai.agent_runs.read', 'import_export.import_tasks.execute']);
    await flushPromises(); expect(wrapper.text()).toContain('等待审批');
    expect(wrapper.findAll('[data-testid^="my-tasks-"]').filter(item => ['execute', 'resume', 'retry', 'cancel', 'download'].some(action => item.attributes('data-testid') === `my-tasks-${action}`))).toHaveLength(0);
    await wrapper.get('.el-table__body-wrapper [data-testid="my-tasks-detail"]').trigger('click');
    expect(api.push).toHaveBeenCalledWith({ name: 'ai-agent-runs', query: { taskId: 'run' } });
    let resolve!: (value: unknown) => void;
    api.listMyAgentRuns.mockImplementation(() => new Promise(done => { resolve = done; }));
    await wrapper.get('[data-testid="my-tasks-refresh"]').trigger('click');
    const signal = api.listMyAgentRuns.mock.lastCall![2] as AbortSignal;
    session.currentUser!.permissions = ['platform.my_tasks.read']; await flushPromises();
    expect(signal.aborted).toBe(true);
    resolve(page([{ id: 'late', definitionKey: 'late-private', statusKey: 'completed' }])); await flushPromises();
    expect(wrapper.text()).not.toContain('rename-workflow'); expect(wrapper.text()).not.toContain('late-private');
  });
  it('取消跳转原详情确认，不在聚合页直接写入或使用旧版本执行', async () => {
    api.listMyImportTasks.mockResolvedValue(page([imported('queued')]));
    const { wrapper } = render('imports', ['platform.my_tasks.read', 'import_export.import_tasks.read', 'import_export.import_tasks.cancel']);
    await flushPromises(); await wrapper.get('[data-testid="my-tasks-cancel"]').trigger('click'); await flushPromises();
    expect(api.push).toHaveBeenCalledWith({ name: 'import-export-tasks', query: { taskId: 'i' } });
    expect(api.getImportExportTask).not.toHaveBeenCalled(); expect(api.executeImportExportTask).not.toHaveBeenCalled();
  });
  it('两来源独立分页，上一页复用原位置，刷新回到首屏', async () => {
    api.listMyImportTasks.mockResolvedValueOnce(page([imported()], 'next')).mockResolvedValue(page([imported()]));
    const imports = render(), reports = render('reports'); await flushPromises();
    await imports.wrapper.get('[data-testid="my-tasks-next"]').trigger('click'); await flushPromises();
    expect(api.listMyImportTasks.mock.calls[1]![0]).toBe('next'); expect(api.listMyReportingTasks).toHaveBeenCalledTimes(1);
    await imports.wrapper.get('[data-testid="my-tasks-previous"]').trigger('click'); await flushPromises();
    expect(api.listMyImportTasks.mock.calls[2]![0]).toBeUndefined();
    await imports.wrapper.get('[data-testid="my-tasks-refresh"]').trigger('click'); await flushPromises();
    expect(imports.wrapper.text()).toContain('第 1 页'); expect(reports.wrapper.text()).not.toContain('Orders.xlsx');
  });
  it('状态筛选不继承旧游标', async () => {
    api.listMyImportTasks.mockResolvedValue(page([imported()], 'next')); const { wrapper } = render(); await flushPromises();
    await wrapper.get('[data-testid="my-tasks-next"]').trigger('click'); await flushPromises();
    const select = wrapper.getComponent('[data-testid="my-tasks-status"]') as VueWrapper; select.vm.$emit('update:modelValue', 'execution_failed'); select.vm.$emit('change'); await flushPromises();
    expect(api.listMyImportTasks.mock.lastCall?.slice(0, 2)).toEqual([undefined, 'execution_failed']);
    expect(wrapper.text()).toContain('第 1 页');
  });
  it('单来源失败清空该来源，另一个来源继续显示', async () => {
    api.listMyImportTasks.mockRejectedValue(new Error('offline')); api.listMyReportingTasks.mockResolvedValue(page([reported()]));
    const imports = render(), reports = render('reports'); await flushPromises();
    expect(imports.wrapper.text()).toContain('此来源暂时无法读取'); expect(reports.wrapper.text()).toContain('Report');
  });
  it('切换上下文取消在途读取并丢弃迟到摘要', async () => {
    let resolve!: (value: unknown) => void; api.listMyImportTasks.mockImplementation(() => new Promise(done => { resolve = done; }));
    const { wrapper, session } = render(); const signal = api.listMyImportTasks.mock.calls[0]![2] as AbortSignal;
    session.switching = true; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(page([imported()])); await flushPromises(); expect(wrapper.text()).not.toContain('Orders.xlsx');
  });
  it('操作前状态已变化时不重放执行，详情只跳固定来源路由', async () => {
    api.listMyImportTasks.mockResolvedValue(page([imported()])); api.getImportExportTask.mockResolvedValue(imported('executing'));
    const { wrapper } = render(); await flushPromises();
    await wrapper.get('[data-testid="my-tasks-execute"]').trigger('click'); await flushPromises();
    expect(api.executeImportExportTask).not.toHaveBeenCalled();
    await wrapper.get('.el-table__body .el-table__row [data-testid="my-tasks-detail"]').trigger('click'); await flushPromises();
    expect(api.push).toHaveBeenCalledWith({ name: 'import-export-tasks', query: { taskId: 'i' } });
  });
  it('下载迟到时撤权丢弃文件，且无写入重试', async () => {
    api.listMyReportingTasks.mockResolvedValue(page([reported()])); api.getReportingExportTask.mockResolvedValue(reported());
    let resolve!: (value: Blob) => void; api.downloadReportingExportTask.mockImplementation(() => new Promise(done => { resolve = done; }));
    const { wrapper, session } = render('reports'); await flushPromises(); const createUrl = vi.spyOn(URL, 'createObjectURL');
    await wrapper.get('[data-testid="my-tasks-download"]').trigger('click'); await flushPromises();
    const signal = api.downloadReportingExportTask.mock.calls[0]![1] as AbortSignal;
    session.currentUser!.permissions = ['platform.my_tasks.read', 'reporting.export_tasks.read']; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(new Blob(['private'])); await flushPromises(); expect(createUrl).not.toHaveBeenCalled(); expect(api.downloadReportingExportTask).toHaveBeenCalledTimes(1);
  });
  it('排队来源刷新直到终态，隐藏后暂停，超时提供来源错误', async () => {
    vi.useFakeTimers(); api.listMyReportingTasks.mockResolvedValueOnce(page([reported('queued')])).mockResolvedValue(page([reported()]));
    const { wrapper } = render('reports'); await flushPromises(); await vi.advanceTimersByTimeAsync(5000); await flushPromises();
    expect(wrapper.text()).toContain('已成功'); await vi.advanceTimersByTimeAsync(15000); expect(api.listMyReportingTasks).toHaveBeenCalledTimes(2);
    api.listMyReportingTasks.mockImplementation(() => new Promise(() => {}));
    await wrapper.get('[data-testid="my-tasks-refresh"]').trigger('click'); await vi.advanceTimersByTimeAsync(30000);
    expect((api.listMyReportingTasks.mock.lastCall![2] as AbortSignal).aborted).toBe(true);
    expect(wrapper.text()).toContain('此来源暂时无法读取');
  });
});
