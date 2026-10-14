import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElDialog, ElMessage, ElOption, ElSelect } from 'element-plus';
import ReportingExportTasksView from './ReportingExportTasksView.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { listReportingPublishedDefinitions } from '../api/reporting-definitions';
import { cancelReportingExportTask, createReportingExportTask, downloadReportingExportTask, listReportingExportTasks } from '../api/reporting-export-tasks';
import { createOutputSession, deferred, exportTask, publishedReportDefinition } from '../test/data-output-fixtures';
vi.mock('../api/reporting-definitions', () => ({ listReportingPublishedDefinitions: vi.fn() }));
vi.mock('../api/reporting-export-tasks', () => ({ cancelReportingExportTask: vi.fn(), createReportingExportTask: vi.fn(), listReportingExportTasks: vi.fn(), downloadReportingExportTask: vi.fn() }));
let wrapper: VueWrapper | undefined;
beforeEach(() => { vi.resetAllMocks(); vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([publishedReportDefinition]); vi.mocked(listReportingExportTasks).mockResolvedValue({ items: [exportTask], page: 1, pageSize: 20, total: 1 }); });
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
const mountView = (permissions: string[]) => { const context = createOutputSession(permissions); wrapper = mount(ReportingExportTasksView, { global: { plugins: [context.pinia] } }); return context.session; };
describe('报表导出授权与生命周期', () => {
  const queued = { ...exportTask, statusKey: 'queued' };
  const queue = (task = queued) => vi.mocked(listReportingExportTasks).mockResolvedValue({ items: [task], page: 1, pageSize: 20, total: 1 });
  it('创建权限不授予取消，取消不要求执行或下载权限', async () => {
    queue(); const session = mountView(['reporting.export_tasks.read', 'reporting.export_tasks.create']); await flushPromises();
    expect(wrapper!.find('[data-testid="reporting-export-cancel"]').exists()).toBe(false);
    session.currentUser!.permissions = ['reporting.export_tasks.read', 'reporting.export_tasks.cancel']; await flushPromises();
    expect(wrapper!.find('[data-testid="reporting-export-cancel"]').exists()).toBe(true);
  });
  it.each(['processing', 'succeeded', 'failed', 'cancelled'])('状态%s不显示取消入口', async statusKey => {
    queue({ ...queued, statusKey }); mountView(['reporting.export_tasks.read', 'reporting.export_tasks.cancel']); await flushPromises();
    expect(wrapper!.find('[data-testid="reporting-export-cancel"]').exists()).toBe(false);
  });
  it('其他创建人的排队任务不显示取消入口', async () => {
    queue({ ...queued, requestedByUserId: '019bc2b1-2a40-7cc3-8992-a80de51bf299' });
    mountView(['reporting.export_tasks.read', 'reporting.export_tasks.cancel']); await flushPromises();
    expect(wrapper!.find('[data-testid="reporting-export-cancel"]').exists()).toBe(false);
  });
  it('取消须确认，放弃不发送 HTTP', async () => {
    queue(); mountView(['reporting.export_tasks.read', 'reporting.export_tasks.cancel']); await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-cancel"]').trigger('click'); await flushPromises();
    expect(cancelReportingExportTask).not.toHaveBeenCalled();
    await wrapper!.get('[data-testid="reporting-cancel-dismiss"]').trigger('click'); await flushPromises();
    expect(cancelReportingExportTask).not.toHaveBeenCalled();
  });
  it('确认后互斥提交，成功关闭确认并刷新列表', async () => {
    queue(); const pending = deferred<typeof exportTask>(); vi.mocked(cancelReportingExportTask).mockReturnValue(pending.promise);
    mountView(['reporting.export_tasks.read', 'reporting.export_tasks.cancel']); await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-cancel"]').trigger('click'); await flushPromises();
    await wrapper!.get('[data-testid="reporting-cancel-confirm"]').trigger('click');
    expect(cancelReportingExportTask).toHaveBeenCalledTimes(1);
    pending.resolve({ ...exportTask, statusKey: 'cancelled' }); await flushPromises();
    expect(listReportingExportTasks).toHaveBeenCalledTimes(2);
    expect(wrapper!.find('[data-testid="reporting-cancel-confirm"]').exists()).toBe(false);
  });
  it('确认期间撤权同步关闭确认且不发送取消', async () => {
    queue(); const session = mountView(['reporting.export_tasks.read', 'reporting.export_tasks.cancel']); await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-cancel"]').trigger('click'); await flushPromises();
    session.currentUser!.permissions = ['reporting.export_tasks.read']; await flushPromises();
    expect(wrapper!.find('[data-testid="reporting-cancel-confirm"]').exists()).toBe(false);
    expect(cancelReportingExportTask).not.toHaveBeenCalled();
  });
  it('取消在途切租户中止接入，迟到成功不污染新列表或通知', async () => {
    queue(); const pending = deferred<typeof exportTask>(); vi.mocked(cancelReportingExportTask).mockReturnValue(pending.promise);
    const success = vi.spyOn(ElMessage, 'success'); const session = mountView(['reporting.export_tasks.read', 'reporting.export_tasks.cancel']); await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-cancel"]').trigger('click'); await flushPromises();
    await wrapper!.get('[data-testid="reporting-cancel-confirm"]').trigger('click');
    session.currentUser!.tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
    expect(vi.mocked(cancelReportingExportTask).mock.calls[0]?.[1]?.aborted).toBe(true);
    pending.resolve({ ...exportTask, statusKey: 'cancelled' }); await flushPromises();
    expect(success).not.toHaveBeenCalled();
  });
  it('明确文件404显示不可用并禁用对应下载入口', async () => {
    vi.mocked(downloadReportingExportTask).mockRejectedValue({ title: 'missing', status: 404, code: 'files.file.not_found' });
    mountView(['reporting.export_tasks.read','reporting.export_tasks.download']); await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-download"]').trigger('click'); await flushPromises();
    expect(wrapper!.get('[data-testid="reporting-export-download"]').attributes('disabled')).toBeDefined();
    expect(wrapper!.find('[data-testid="reporting-result-unavailable"]').exists()).toBe(true);
    wrapper!.getComponent(ArtTableHeader).vm.$emit('refresh'); await flushPromises();
    expect(wrapper!.get('[data-testid="reporting-export-download"]').attributes('disabled')).toBeUndefined();
    expect(wrapper!.find('[data-testid="reporting-result-unavailable"]').exists()).toBe(false);
  });
  it.each([403,500])('下载%s不误判为文件已清理', async status => {
    vi.mocked(downloadReportingExportTask).mockRejectedValue({ title: 'failure', status, code: 'files.file.not_found' });
    mountView(['reporting.export_tasks.read','reporting.export_tasks.download']); await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-download"]').trigger('click'); await flushPromises();
    expect(wrapper!.get('[data-testid="reporting-export-download"]').attributes('disabled')).toBeUndefined();
    expect(wrapper!.find('[data-testid="reporting-result-unavailable"]').exists()).toBe(false);
  });
  it('关闭等待创建的弹窗立即取消，迟到任务不刷新或写回弹窗', async () => {
    const pending=deferred<typeof exportTask>();vi.mocked(createReportingExportTask).mockReturnValue(pending.promise);
    mountView(['reporting.export_tasks.read','reporting.export_tasks.create','reporting.executions.run']);await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-create"]').trigger('click');await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-submit"]').trigger('click');
    const signal=vi.mocked(createReportingExportTask).mock.calls[0]![1]!;
    wrapper!.getComponent(ElDialog).vm.$emit('update:modelValue',false);await flushPromises();
    expect(signal.aborted).toBe(true);pending.resolve(exportTask);await flushPromises();
    expect(listReportingExportTasks).toHaveBeenCalledOnce();
  });
  it('同一报表的多个版本可独立选择并创建指定旧版导出', async () => {
    vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([{...publishedReportDefinition,versionNumber:2},publishedReportDefinition]);
    vi.mocked(createReportingExportTask).mockResolvedValue(exportTask);
    mountView(['reporting.export_tasks.read','reporting.export_tasks.create','reporting.executions.run']);await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-create"]').trigger('click');await flushPromises();
    const select=wrapper!.findAllComponents(ElSelect).find(item=>item.attributes('data-testid')==='reporting-export-definition')!;
    expect(select.findAllComponents(ElOption).map(option=>option.props('value'))).toEqual([publishedReportDefinition.definitionId+':2',publishedReportDefinition.definitionId+':1']);
    select.vm.$emit('update:modelValue',publishedReportDefinition.definitionId+':1');select.vm.$emit('change');await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-submit"]').trigger('click');await flushPromises();
    expect(createReportingExportTask).toHaveBeenCalledWith({definitionId:publishedReportDefinition.definitionId,versionNumber:1,formatKey:'excel',parameters:[]},expect.any(AbortSignal));
    expect(listReportingExportTasks).toHaveBeenCalledTimes(2);
  });
  it.each([{definitionId:'another'},{versionNumber:2},{formatKey:'pdf'}])('导出响应不匹配时不关闭或刷新 %j', async (change) => {
    vi.mocked(createReportingExportTask).mockResolvedValue({...exportTask,...change});
    mountView(['reporting.export_tasks.read','reporting.export_tasks.create','reporting.executions.run']);await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-create"]').trigger('click');await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-submit"]').trigger('click');await flushPromises();
    expect(listReportingExportTasks).toHaveBeenCalledOnce();
    expect(wrapper!.find('[data-testid="reporting-export-submit"]').exists()).toBe(true);
  });
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
    mountView(['reporting.export_tasks.read', 'reporting.export_tasks.create', 'reporting.executions.run']); await flushPromises();
    await wrapper!.get('[data-testid="reporting-export-create"]').trigger('click'); await flushPromises(); const button = wrapper!.get('[data-testid="reporting-export-submit"]');
    await button.trigger('click'); await button.trigger('click'); expect(createReportingExportTask).toHaveBeenCalledTimes(1);
    pending.reject(new Error('failed')); await flushPromises(); await button.trigger('click'); await flushPromises();
    expect(createReportingExportTask).toHaveBeenCalledTimes(2); expect(listReportingExportTasks).toHaveBeenCalledTimes(2);
  });
  it('只读列表不请求创建专用定义目录', async () => { mountView(['reporting.export_tasks.read']); await flushPromises(); expect(listReportingPublishedDefinitions).not.toHaveBeenCalled(); expect(listReportingExportTasks).toHaveBeenCalledTimes(1); });
  it('下载等待期间撤权，不创建文件 URL，重复点击不重复请求', async () => {
    const pending = deferred<Blob>(); vi.mocked(downloadReportingExportTask).mockReturnValue(pending.promise); const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fixture');
    const session = mountView(['reporting.export_tasks.read', 'reporting.export_tasks.download']); await flushPromises(); const button = wrapper!.get('[data-testid="reporting-export-download"]');
    await button.trigger('click'); await button.trigger('click'); const signal = vi.mocked(downloadReportingExportTask).mock.calls[0]?.[1];
    session.currentUser!.permissions = ['reporting.export_tasks.read']; await flushPromises(); pending.resolve(new Blob(['private'])); await flushPromises();
    expect(downloadReportingExportTask).toHaveBeenCalledTimes(1); expect(signal?.aborted).toBe(true); expect(createUrl).not.toHaveBeenCalled();
  });
  it('已打开弹窗撤销创建权限后不再显示提交入口', async () => {
    const session = mountView(['reporting.export_tasks.read', 'reporting.export_tasks.create', 'reporting.executions.run']); await flushPromises(); await wrapper!.get('[data-testid="reporting-export-create"]').trigger('click'); await flushPromises();
    session.currentUser!.permissions = ['reporting.export_tasks.read']; await flushPromises(); expect(wrapper!.find('[data-testid="reporting-export-submit"]').exists()).toBe(false); expect(createReportingExportTask).not.toHaveBeenCalled();
  });
});
