import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createOutputSession, exportTask, outputId } from '../test/data-output-fixtures';
import ReportingExportTasksView from './ReportingExportTasksView.vue';
import { useSessionStore } from '../auth/session';
const api = vi.hoisted(() => ({ listReportingPublishedDefinitions: vi.fn(), listReportingExportTasks: vi.fn(),
  createReportingExportTask: vi.fn(), downloadReportingExportTask: vi.fn(), getReportingExportTask: vi.fn(), cancelReportingExportTask: vi.fn() }));
vi.mock('../api/reporting-publications', () => ({ listReportingPublishedDefinitions: api.listReportingPublishedDefinitions }));
vi.mock('../api/reporting-export-tasks', () => api);
const wrappers: VueWrapper[] = [];
const all = ['reporting.export_tasks.read', 'reporting.export_tasks.create', 'reporting.export_tasks.download', 'reporting.published_definitions.read'];
const task = (statusKey = 'queued') => ({ ...exportTask, version: 4, definitionName: 'Visible report', definitionKey: 'visible', statusKey });
const page = (items: unknown[] = []) => ({ items, page: 1, pageSize: 20, total: items.length });
function render(permissions = all) {
  const { pinia, session } = createOutputSession(permissions);
  const wrapper = mount(ReportingExportTasksView, { global: { plugins: [pinia], stubs: {
    ElDialog: { props: ['modelValue'], template: '<section v-if="modelValue"><slot/><slot name="footer"/></section>' },
    ElDrawer: { props: ['modelValue'], template: '<aside v-if="modelValue"><slot/></aside>' }
  } } }); wrappers.push(wrapper); return { wrapper, session };
}
beforeEach(() => { vi.resetAllMocks(); api.listReportingPublishedDefinitions.mockResolvedValue(page()); api.listReportingExportTasks.mockResolvedValue(page());
  api.getReportingExportTask.mockResolvedValue({ ...task(), version: 4, parameters: [], outputFileId: null }); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); vi.useRealTimers(); });
describe('ReportingExportTasksView', () => {
  it('本人排队任务使用详情并发版本取消，不要求创建或下载权限', async () => {
    api.listReportingExportTasks.mockResolvedValue(page([task()]));
    api.cancelReportingExportTask.mockResolvedValue({ ...task('canceled'), version: 5, parameters: [], outputFileId: null });
    const { wrapper } = render(['reporting.export_tasks.read', 'reporting.export_tasks.cancel']); await flushPromises();
    await wrapper.get('[data-testid="reporting-export-detail"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="reporting-export-detail-cancel"]').exists()).toBe(true);
    await wrapper.get('[data-testid="reporting-export-detail-cancel"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-cancel-confirm"]').trigger('click'); await flushPromises();
    expect(api.cancelReportingExportTask).toHaveBeenCalledWith(outputId, 4, expect.any(AbortSignal));
    expect(wrapper.text()).toContain('任务已取消'); expect(wrapper.find('[data-testid="reporting-export-detail-cancel"]').exists()).toBe(false);
  });
  it('处理中及他人的任务不显示取消，保留来源状态说明', async () => {
    api.listReportingExportTasks.mockResolvedValue(page([task('processing')]));
    api.getReportingExportTask.mockResolvedValue({ ...task('processing'), version: 4, parameters: [], outputFileId: null });
    const { wrapper } = render(['reporting.export_tasks.read', 'reporting.export_tasks.cancel']); await flushPromises();
    await wrapper.get('[data-testid="reporting-export-detail"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('文件正在生成'); expect(wrapper.find('[data-testid="reporting-export-detail-cancel"]').exists()).toBe(false);
    api.getReportingExportTask.mockResolvedValue({ ...task(), requestedByUserId: 'other', version: 4, parameters: [], outputFileId: null });
    await wrapper.get('[data-testid="reporting-export-detail"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="reporting-export-detail-cancel"]').exists()).toBe(false);
  });
  it('未知写入结果保持锁定，显式重读确认取消终态', async () => {
    api.listReportingExportTasks.mockResolvedValue(page([task()])); api.cancelReportingExportTask.mockRejectedValue(new Error('timeout'));
    const { wrapper } = render(['reporting.export_tasks.read', 'reporting.export_tasks.cancel']); await flushPromises();
    await wrapper.get('[data-testid="reporting-export-detail"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-export-detail-cancel"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-cancel-confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="reporting-export-detail-cancel"]').attributes('disabled')).toBeDefined();
    api.getReportingExportTask.mockResolvedValue({ ...task('canceled'), version: 5, parameters: [], outputFileId: null });
    await wrapper.get('[data-testid="reporting-export-detail-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('任务已取消'); expect(api.cancelReportingExportTask).toHaveBeenCalledTimes(1);
  });
  it('只读任务页面独立加载，不读取 Host 定义或发布目录', async () => {
    const { wrapper } = render(['reporting.export_tasks.read']); await flushPromises();
    expect(api.listReportingExportTasks).toHaveBeenCalledTimes(1); expect(api.listReportingPublishedDefinitions).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="reporting-export-create"]').exists()).toBe(false);
  });
  it('创建同时要求创建权限和租户发布目录读取权限', async () => {
    const { wrapper } = render(['reporting.export_tasks.read', 'reporting.export_tasks.create']); await flushPromises();
    expect(wrapper.get('[data-testid="reporting-export-create"]').attributes('disabled')).toBeDefined();
  });
  it('只在打开创建对话框时读取最小发布目录', async () => {
    const { wrapper } = render(); await flushPromises();
    expect(api.listReportingPublishedDefinitions).not.toHaveBeenCalled();
    await wrapper.get('[data-testid="reporting-export-create"]').trigger('click'); await flushPromises();
    expect(api.listReportingPublishedDefinitions).toHaveBeenCalledTimes(1);
  });
  it('排队与执行任务自动更新，终态停止轮询', async () => {
    vi.useFakeTimers(); api.listReportingExportTasks.mockResolvedValueOnce(page([task()])).mockResolvedValue(page([task('succeeded')]));
    const { wrapper } = render(); await flushPromises(); expect(wrapper.text()).toContain('排队中');
    await vi.advanceTimersByTimeAsync(5000); await flushPromises(); expect(wrapper.text()).toContain('已成功');
    await vi.advanceTimersByTimeAsync(15000); expect(api.listReportingExportTasks).toHaveBeenCalledTimes(2);
  });
  it('切换租户时取消旧读取，迟到页面结果不能进入新上下文', async () => {
    let resolve!: (value: unknown) => void; api.listReportingExportTasks.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const { wrapper, session } = render(); await flushPromises(); const signal = api.listReportingExportTasks.mock.calls[0]![3] as AbortSignal;
    session.switching = true; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(page([task('succeeded')])); await flushPromises(); expect(wrapper.text()).not.toContain('Visible report');
    expect(wrapper.find('section').exists()).toBe(false);
  });
  it('会话失效后丢弃迟到下载，不生成旧文件链接', async () => {
    api.listReportingExportTasks.mockResolvedValue(page([task('succeeded')]));
    let resolve!: (value: Blob) => void; api.downloadReportingExportTask.mockImplementation(() => new Promise(done => { resolve = done; }));
    const { wrapper, session } = render(); await flushPromises(); const createUrl = vi.spyOn(URL, 'createObjectURL');
    await wrapper.get('[data-testid="reporting-export-download"]').trigger('click');
    const signal = api.downloadReportingExportTask.mock.calls[0]![1] as AbortSignal;
    session.switching = true; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(new Blob(['sensitive'])); await flushPromises(); expect(createUrl).not.toHaveBeenCalled();
  });
  it('同租户其他创建者的任务不显示下载按钮', async () => {
    api.listReportingExportTasks.mockResolvedValue(page([{ ...task('succeeded'), requestedByUserId: 'other' }]));
    const { wrapper } = render(); await flushPromises(); expect(wrapper.find('[data-testid="reporting-export-download"]').exists()).toBe(false);
  });
  it('发布目录故障保留已读取历史任务，不阻断独立读取', async () => {
    api.listReportingExportTasks.mockResolvedValue(page([task('failed')])); api.listReportingPublishedDefinitions.mockRejectedValue(new Error('Unavailable'));
    const { wrapper } = render(); await flushPromises(); await wrapper.get('[data-testid="reporting-export-create"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('Visible report'); expect(api.listReportingExportTasks).toHaveBeenCalledTimes(1);
  });
  it('搜索取消上一请求，旧关键词的迟到结果不得替换新选项', async () => {
    let resolve!: (value: unknown) => void; api.listReportingPublishedDefinitions.mockImplementationOnce(() => new Promise(done => { resolve = done; }))
      .mockResolvedValueOnce(page([{ id: outputId, name: 'New', versionNumber: 2, parameterSchema: [] }]));
    const { wrapper } = render(); await flushPromises(); await wrapper.get('[data-testid="reporting-export-create"]').trigger('click');
    const oldSignal = api.listReportingPublishedDefinitions.mock.calls[0]![1] as AbortSignal;
    const definitionSelect = wrapper.getComponent('[data-testid="reporting-export-definition"]') as VueWrapper;
    const search = (definitionSelect.props() as { remoteMethod: (keyword: string) => Promise<void> }).remoteMethod;
    await search('New'); expect(oldSignal.aborted).toBe(true);
    resolve(page([{ id: '019bc2b1-2a40-7cc3-8992-a80de51bf298', name: 'Old', versionNumber: 1, parameterSchema: [] }])); await flushPromises();
    expect(definitionSelect.findAllComponents({ name: 'ElOption' }).map(option => option.props('label'))).toEqual(['New']);
  });
  it('创建失败保留参数输入，允许修正后重试', async () => {
    api.listReportingPublishedDefinitions.mockResolvedValue(page([{ id: outputId, name: 'Published', versionNumber: 2,
      parameterSchema: [{ parameterKey: 'topN', defaultValue: '20', displayName: 'Limit', dataTypeKey: 'string', isRequired: false }] }]));
    api.createReportingExportTask.mockRejectedValue(new Error('Invalid parameter'));
    const { wrapper } = render(); await flushPromises(); await wrapper.get('[data-testid="reporting-export-create"]').trigger('click'); await flushPromises();
    const select = wrapper.getComponent('[data-testid="reporting-export-definition"]') as VueWrapper; select.vm.$emit('update:modelValue', outputId + ':2'); select.vm.$emit('change', outputId + ':2'); await flushPromises();
    await wrapper.get('section .el-input__inner').setValue('30'); await wrapper.get('[data-testid="reporting-export-submit"]').trigger('click'); await flushPromises();
    expect(wrapper.find('section').exists()).toBe(true); expect((wrapper.get('section .el-input__inner').element as HTMLInputElement).value).toBe('30');
    expect(api.createReportingExportTask.mock.calls[0]![0]).toEqual({ definitionId: outputId, versionNumber: 2, formatKey: 'excel', parameters: [{ parameterKey: 'topN', value: '30' }] });
  });
});
