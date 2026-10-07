import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import ReportingExportTasksView from './ReportingExportTasksView.vue';
import { listReportingDefinitions } from '../api/reporting-definitions';
import { createReportingExportTask, downloadReportingExportTask, listReportingExportTasks } from '../api/reporting-export-tasks';
import { createOutputSession, deferred, exportTask, reportDefinition } from '../test/data-output-fixtures';
vi.mock('../api/reporting-definitions', () => ({ listReportingDefinitions: vi.fn() }));
vi.mock('../api/reporting-export-tasks', () => ({ createReportingExportTask: vi.fn(), listReportingExportTasks: vi.fn(), downloadReportingExportTask: vi.fn() }));
let wrapper: VueWrapper | undefined;
beforeEach(() => { vi.resetAllMocks(); vi.mocked(listReportingDefinitions).mockResolvedValue([reportDefinition]); vi.mocked(listReportingExportTasks).mockResolvedValue({ items: [exportTask], page: 1, pageSize: 20, total: 1 }); });
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
const mountView = (permissions: string[]) => { const context = createOutputSession(permissions); wrapper = mount(ReportingExportTasksView, { global: { plugins: [context.pinia] } }); return context.session; };
describe('报表导出授权与生命周期', () => {
  it('正常下载及浏览器触发失败都释放文件 URL，失败后允许重试', async () => {
    vi.mocked(downloadReportingExportTask).mockResolvedValue(new Blob(['content']));
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fixture'); const revokeUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementationOnce(() => { throw new Error('blocked'); }).mockImplementation(() => {});
    mountView(['reporting.export_tasks.read', 'reporting.export_tasks.download']); await flushPromises();
    const button = wrapper!.get('[data-testid="reporting-export-download"]'); await button.trigger('click'); await flushPromises(); await button.trigger('click'); await flushPromises();
    expect(downloadReportingExportTask).toHaveBeenCalledTimes(2); expect(createUrl).toHaveBeenCalledTimes(2); expect(revokeUrl).toHaveBeenCalledTimes(2); expect(click).toHaveBeenCalledTimes(2);
  });
  it('创建请求互斥，失败后重试；成功才关闭弹窗并刷新列表', async () => {
    const pending = deferred<typeof exportTask>(); vi.mocked(createReportingExportTask).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(exportTask);
    mountView(['reporting.export_tasks.read', 'reporting.export_tasks.create', 'reporting.definitions.read']); await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-create"]').trigger('click'); await flushPromises(); const button = wrapper!.get('[data-testid="reporting-export-submit"]');
    await button.trigger('click'); await button.trigger('click'); expect(createReportingExportTask).toHaveBeenCalledTimes(1);
    pending.reject(new Error('failed')); await flushPromises(); await button.trigger('click'); await flushPromises();
    expect(createReportingExportTask).toHaveBeenCalledTimes(2); expect(listReportingExportTasks).toHaveBeenCalledTimes(2);
  });
  it('只读列表不请求创建专用定义目录', async () => { mountView(['reporting.export_tasks.read']); await flushPromises(); expect(listReportingDefinitions).not.toHaveBeenCalled(); expect(listReportingExportTasks).toHaveBeenCalledTimes(1); });
  it('下载等待期间撤权，不创建文件 URL，重复点击不重复请求', async () => {
    const pending = deferred<Blob>(); vi.mocked(downloadReportingExportTask).mockReturnValue(pending.promise); const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fixture');
    const session = mountView(['reporting.export_tasks.read', 'reporting.export_tasks.download']); await flushPromises(); const button = wrapper!.get('[data-testid="reporting-export-download"]');
    await button.trigger('click'); await button.trigger('click'); const signal = vi.mocked(downloadReportingExportTask).mock.calls[0]?.[1];
    session.currentUser!.permissions = ['reporting.export_tasks.read']; await flushPromises(); pending.resolve(new Blob(['private'])); await flushPromises();
    expect(downloadReportingExportTask).toHaveBeenCalledTimes(1); expect(signal?.aborted).toBe(true); expect(createUrl).not.toHaveBeenCalled();
  });
  it('已打开弹窗撤销创建权限后不再显示提交入口', async () => {
    const session = mountView(['reporting.export_tasks.read', 'reporting.export_tasks.create', 'reporting.definitions.read']); await flushPromises(); await wrapper!.get('[data-testid="reporting-export-create"]').trigger('click'); await flushPromises();
    session.currentUser!.permissions = ['reporting.export_tasks.read']; await flushPromises(); expect(wrapper!.find('[data-testid="reporting-export-submit"]').exists()).toBe(false); expect(createReportingExportTask).not.toHaveBeenCalled();
  });
});
