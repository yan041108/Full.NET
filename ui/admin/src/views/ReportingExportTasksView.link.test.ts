import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElDrawer } from 'element-plus';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router, type LocationQueryRaw } from 'vue-router';
import ReportingExportTasksView from './ReportingExportTasksView.vue';
import * as api from '../api/reporting-export-tasks';
import { createOutputSession, deferred, exportTask, outputId } from '../test/data-output-fixtures';
vi.mock('../api/reporting-export-tasks', () => ({ getReportingExportTask: vi.fn(), listReportingExportTasks: vi.fn(),
  createReportingExportTask: vi.fn(), cancelReportingExportTask: vi.fn(), downloadReportingExportTask: vi.fn() }));
vi.mock('../api/reporting-definitions', () => ({ listReportingPublishedDefinitions: vi.fn() }));
let wrapper: VueWrapper | undefined; let router: Router; let visible = ref(true);
const otherId = '01980000-0000-7000-8000-000000000003';
beforeEach(() => {
  vi.resetAllMocks(); vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
  vi.mocked(api.listReportingExportTasks).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  vi.mocked(api.getReportingExportTask).mockImplementation(async id => ({ ...exportTask, id, definitionName: id === otherId ? '新导出' : '链接导出' }));
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); vi.useRealTimers(); });
async function setup(query: LocationQueryRaw = { taskId: outputId }, permissions = ['reporting.export_tasks.read'], keepAlive = false) {
  const context = createOutputSession(permissions); visible = ref(true);
  router = createRouter({ history: createMemoryHistory(), routes: [
    { name: 'reporting-export-tasks', path: '/exports', component: { render: () => null } },
    { name: 'elsewhere', path: '/elsewhere', component: { render: () => null } }
  ] }); await router.push({ name: 'reporting-export-tasks', query });
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => visible.value ? h(ReportingExportTasksView) : h(blank) }) });
  wrapper = mount(keepAlive ? root : ReportingExportTasksView, { global: { plugins: [context.pinia, router], directives: { loading: {} }, stubs: {
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' },
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); await flushPromises(); return context.session;
}
describe('报表导出详情与结果入口', () => {
  it('可读取不在列表页的任务，不自动下载或取消', async () => {
    await setup(); expect(api.getReportingExportTask).toHaveBeenCalledExactlyOnceWith(outputId, expect.any(AbortSignal));
    expect(wrapper!.text()).toContain('链接导出'); expect(api.downloadReportingExportTask).not.toHaveBeenCalled(); expect(api.cancelReportingExportTask).not.toHaveBeenCalled();
  });
  it.each(['bad', ['01980000-0000-7000-8000-000000000003'], '00000000-0000-0000-0000-000000000000'])('非法参数 %j 不读取', async taskId => {
    await setup({ taskId }); expect(api.getReportingExportTask).not.toHaveBeenCalled();
  });
  it('无读取权限不读取链接', async () => { await setup(undefined, []); expect(api.getReportingExportTask).not.toHaveBeenCalled(); });
  it('记录变化中止旧详情并忽略迟到内容', async () => {
    const pending = deferred<typeof exportTask>(); vi.mocked(api.getReportingExportTask).mockReturnValueOnce(pending.promise); await setup();
    const signal = vi.mocked(api.getReportingExportTask).mock.calls[0]![1]!;
    await router.replace({ query: { taskId: otherId } }); await flushPromises(); pending.resolve({ ...exportTask, definitionName: '旧敏感内容' }); await flushPromises();
    expect(signal.aborted).toBe(true); expect(wrapper!.text()).toContain('新导出'); expect(wrapper!.text()).not.toContain('旧敏感内容');
  });
  it('详情关闭取消下载，迟到文件不触发浏览器下载', async () => {
    const pending = deferred<Blob>(); vi.mocked(api.downloadReportingExportTask).mockReturnValueOnce(pending.promise);
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test'); await setup(undefined, ['reporting.export_tasks.read', 'reporting.export_tasks.download']);
    await wrapper!.get('[data-testid="reporting-export-detail-download"]').trigger('click');
    const signal = vi.mocked(api.downloadReportingExportTask).mock.calls[0]![1]!;
    wrapper!.getComponent(ElDrawer).vm.$emit('update:modelValue', false); await flushPromises(); pending.resolve(new Blob(['private'])); await flushPromises();
    expect(signal.aborted).toBe(true); expect(createUrl).not.toHaveBeenCalled();
  });
  it('详情取消使用独立权限和确认，列表没有该任务也可以取消', async () => {
    vi.mocked(api.getReportingExportTask).mockResolvedValue({ ...exportTask, statusKey: 'queued' });
    vi.mocked(api.cancelReportingExportTask).mockResolvedValue({ ...exportTask, statusKey: 'cancelled' });
    await setup(undefined, ['reporting.export_tasks.read', 'reporting.export_tasks.cancel']);
    await wrapper!.get('[data-testid="reporting-export-detail-cancel"]').trigger('click'); await flushPromises();
    expect(api.cancelReportingExportTask).not.toHaveBeenCalled(); await wrapper!.get('[data-testid="reporting-cancel-confirm"]').trigger('click'); await flushPromises();
    expect(api.cancelReportingExportTask).toHaveBeenCalledExactlyOnceWith(outputId, expect.any(AbortSignal));
  });
  it('详情在途任务与列表共用轮询，终态停止读取', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    vi.mocked(api.getReportingExportTask).mockResolvedValueOnce({ ...exportTask, statusKey: 'processing' }).mockResolvedValue(exportTask);
    await setup(); await vi.advanceTimersByTimeAsync(5000); await flushPromises();
    expect(api.getReportingExportTask).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(10000); await flushPromises(); expect(api.getReportingExportTask).toHaveBeenCalledTimes(2);
  });
  it('租户变化与 KeepAlive 恢复不重放旧链接，显式新链接可读取', async () => {
    const session = await setup(undefined, undefined, true); session.currentUser = { ...session.currentUser!, tenantId: otherId, scope: `tenant:${otherId.replaceAll('-', '')}` }; await flushPromises();
    visible.value = false; await flushPromises(); visible.value = true; await flushPromises();
    expect(api.getReportingExportTask).toHaveBeenCalledOnce(); expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(false);
    await router.replace({ query: { taskId: otherId } }); await flushPromises(); expect(wrapper!.text()).toContain('新导出');
  });
});
