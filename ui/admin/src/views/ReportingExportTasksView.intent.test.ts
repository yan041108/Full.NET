import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, KeepAlive, reactive, ref } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElDialog, ElDrawer, ElMessage, ElPagination, ElSelect } from 'element-plus';
import View from './ReportingExportTasksView.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import * as api from '../api/reporting-export-tasks';
import { listReportingPublishedDefinitions } from '../api/reporting-definitions';
import { createOutputSession, deferred, exportTask, outputId, publishedReportDefinition } from '../test/data-output-fixtures';

vi.mock('../api/reporting-export-tasks', () => ({ getReportingExportTask: vi.fn(), listReportingExportTasks: vi.fn(), createReportingExportTask: vi.fn(), cancelReportingExportTask: vi.fn(), downloadReportingExportTask: vi.fn() }));
vi.mock('../api/reporting-definitions', () => ({ listReportingPublishedDefinitions: vi.fn() }));
const read = 'reporting.export_tasks.read', create = 'reporting.export_tasks.create', run = 'reporting.executions.run', download = 'reporting.export_tasks.download', cancel = 'reporting.export_tasks.cancel';
const all = [read, create, run, download, cancel];
const other = '01980000-0000-7000-8000-000000000003';
let wrapper: VueWrapper | undefined;
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.listReportingExportTasks).mockResolvedValue({ items: [{ ...exportTask }], page: 1, pageSize: 20, total: 100 });
  vi.mocked(api.getReportingExportTask).mockResolvedValue({ ...exportTask });
  vi.mocked(api.createReportingExportTask).mockResolvedValue({ ...exportTask });
  vi.mocked(api.cancelReportingExportTask).mockResolvedValue({ ...exportTask, statusKey: 'cancelled' });
  vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([{ ...publishedReportDefinition }]);
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
async function setup(permissions = all, keepAlive = false, withRoute = false) {
  const context = createOutputSession(permissions); const active = ref(true);
  const route = reactive({ name: 'reporting-export-tasks', query: {} as { taskId?: string } });
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => h(active.value ? View : blank) }) });
  wrapper = mount(keepAlive ? root : View, { global: { plugins: [context.pinia], provide: withRoute ? { [routeLocationKey as symbol]: route } : {}, directives: { loading: () => {} }, stubs: {
    ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' },
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' }
  } } }); await flushPromises(); return { ...context, active, route };
}
const click = (id: string) => wrapper!.get(`[data-testid="${id}"]`).trigger('click');
const queuedRows = () => vi.mocked(api.listReportingExportTasks).mockResolvedValue({ items: [{ ...exportTask, statusKey: 'queued' }], page: 1, pageSize: 20, total: 1 });
const commands = () => wrapper!.getComponent(View).vm as unknown as { openCreateDialog(): void; submitCreate(): Promise<void>; downloadTask(task: typeof exportTask): Promise<void>; requestCancellation(task: typeof exportTask): void; openDetail(id: string): Promise<void> };
const refresh = async () => { wrapper!.getComponent(ArtTableHeader).vm.$emit('refresh'); await flushPromises(); };

describe('报表导出任务操作意图', () => {
  it('缺少父页面read时不加载创建目录或提供任何任务操作', async () => {
    await setup([create, run, download, cancel]);
    expect(listReportingPublishedDefinitions).not.toHaveBeenCalled(); expect(api.listReportingExportTasks).not.toHaveBeenCalled(); expect(wrapper!.find('[data-testid="reporting-export-create"]').exists()).toBe(false);
    commands().openCreateDialog(); await commands().submitCreate(); await commands().downloadTask(exportTask); commands().requestCancellation({ ...exportTask, statusKey: 'queued' }); await flushPromises();
    expect(api.createReportingExportTask).not.toHaveBeenCalled(); expect(api.downloadReportingExportTask).not.toHaveBeenCalled(); expect(wrapper!.find('[data-testid="reporting-cancel-confirm"]').exists()).toBe(false);
  });
  it('关闭创建后迟到提交不能重新创建', async () => {
    await setup(); await click('reporting-export-create'); wrapper!.getComponent(ElDialog).vm.$emit('update:modelValue', false); await flushPromises();
    await commands().submitCreate(); expect(api.createReportingExportTask).not.toHaveBeenCalled();
  });
  it('无执行目录权限不读取定义，也不能直接提交', async () => {
    await setup([read, create]); await click('reporting-export-create'); await commands().submitCreate();
    expect(listReportingPublishedDefinitions).not.toHaveBeenCalled(); expect(api.createReportingExportTask).not.toHaveBeenCalled();
  });
  it('创建成功展示本次回包详情，不额外请求详情或自动下载', async () => {
    await setup(); await click('reporting-export-create'); await click('reporting-export-submit'); await flushPromises();
    expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(true); expect(wrapper!.text()).toContain(exportTask.definitionName);
    expect(api.getReportingExportTask).not.toHaveBeenCalled(); expect(api.downloadReportingExportTask).not.toHaveBeenCalled(); expect(api.listReportingExportTasks).toHaveBeenCalledTimes(2);
  });
  it('创建响应UUID大小写等价可以接入', async () => {
    vi.mocked(api.createReportingExportTask).mockResolvedValue({ ...exportTask, definitionId: outputId.toUpperCase() });
    await setup(); await click('reporting-export-create'); await click('reporting-export-submit'); await flushPromises();
    expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(true);
  });
  it('程序修改选择不能改变在途创建的发布版本', async () => {
    vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([{ ...publishedReportDefinition }, { ...publishedReportDefinition, versionNumber: 2 }]);
    const pending = deferred<typeof exportTask>(); vi.mocked(api.createReportingExportTask).mockReturnValueOnce(pending.promise);
    await setup(); await click('reporting-export-create'); await click('reporting-export-submit');
    wrapper!.getComponent(ElSelect).vm.$emit('update:modelValue', outputId + ':2');
    pending.resolve(exportTask); await flushPromises(); expect(vi.mocked(api.createReportingExportTask).mock.calls[0]?.[0].versionNumber).toBe(1);
    expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(true);
  });
  it.each([{ definitionId: other }, { versionNumber: 2 }, { formatKey: 'pdf' }, { requestedByUserId: other }])('错误创建回包 %j 不打开结果', async change => {
    vi.mocked(api.createReportingExportTask).mockResolvedValue({ ...exportTask, ...change });
    await setup(); await click('reporting-export-create'); await click('reporting-export-submit'); await flushPromises();
    expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(false); expect(api.listReportingExportTasks).toHaveBeenCalledOnce();
  });
  it('详情非法标识不发请求', async () => {
    await setup(); await commands().openDetail('bad'); await commands().openDetail('00000000-0000-0000-0000-000000000000'); expect(api.getReportingExportTask).not.toHaveBeenCalled();
  });
  it('不在当前列表或详情的下载对象不能触发请求', async () => {
    await setup(); await commands().downloadTask({ ...exportTask, id: other }); expect(api.downloadReportingExportTask).not.toHaveBeenCalled();
  });
  it('同标识旧结果文件快照不能下载当前文件', async () => {
    await setup(); await commands().downloadTask({ ...exportTask, outputFileId: other }); expect(api.downloadReportingExportTask).not.toHaveBeenCalled();
  });
  it('真实列表摘要没有文件标识时仍可与同任务详情共同使用下载', async () => {
    const { outputFileId: _file, parameters: _parameters, ...summary } = exportTask;
    vi.mocked(api.listReportingExportTasks).mockResolvedValue({ items: [summary], page: 1, pageSize: 20, total: 1 });
    vi.mocked(api.downloadReportingExportTask).mockResolvedValue(new Blob(['xlsx']));
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:summary'); vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {}); vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    await setup(); await commands().openDetail(outputId); await flushPromises(); await click('reporting-export-download'); await flushPromises();
    expect(api.downloadReportingExportTask).toHaveBeenCalledExactlyOnceWith(outputId, expect.any(AbortSignal));
  });
  it('读取期间目录结果变化后迟到Blob不触发下载', async () => {
    const pending = deferred<Blob>(); vi.mocked(api.downloadReportingExportTask).mockReturnValue(pending.promise);
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:late'); await setup(); await click('reporting-export-download');
    vi.mocked(api.listReportingExportTasks).mockResolvedValue({ items: [{ ...exportTask, outputFileName: 'new-result.xlsx' }], page: 1, pageSize: 20, total: 1 });
    wrapper!.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises(); pending.resolve(new Blob(['old'])); await flushPromises(); expect(createUrl).not.toHaveBeenCalled();
  });
  it('确认取消前任务已改变则不发送取消', async () => {
    queuedRows(); await setup(); await click('reporting-export-cancel');
    vi.mocked(api.listReportingExportTasks).mockResolvedValue({ items: [{ ...exportTask, statusKey: 'processing' }], page: 1, pageSize: 20, total: 1 });
    await refresh(); await click('reporting-cancel-confirm'); await flushPromises(); expect(api.cancelReportingExportTask).not.toHaveBeenCalled();
  });
  it.each([{ id: other, statusKey: 'cancelled' }, { statusKey: 'queued' }])('不匹配取消回包 %j 不提示成功', async change => {
    queuedRows(); vi.mocked(api.cancelReportingExportTask).mockResolvedValue({ ...exportTask, ...change }); const success = vi.spyOn(ElMessage, 'success');
    await setup(); await click('reporting-export-cancel'); await click('reporting-cancel-confirm'); await flushPromises();
    expect(success).not.toHaveBeenCalled(); expect(api.listReportingExportTasks).toHaveBeenCalledOnce();
  });
  it('旧取消确认的迟到点击不能取消新确认', async () => {
    queuedRows(); await setup(); await click('reporting-export-cancel');
    const oldButton = wrapper!.findAllComponents(ElButton).find(item => item.attributes('data-testid') === 'reporting-cancel-confirm')!;
    const accept = oldButton.vm.$.vnode.props!.onClick as () => void;
    await click('reporting-cancel-dismiss'); await click('reporting-export-cancel'); accept(); await flushPromises();
    expect(api.cancelReportingExportTask).not.toHaveBeenCalled(); await click('reporting-cancel-confirm'); await flushPromises(); expect(api.cancelReportingExportTask).toHaveBeenCalledOnce();
  });
  it('KeepAlive停用期间直接调用操作也不读取或写入，激活只恢复读取', async () => {
    const { active } = await setup(all, true); const retained = commands(); active.value = false; await flushPromises();
    retained.openCreateDialog(); await retained.submitCreate(); await retained.openDetail(outputId); await retained.downloadTask(exportTask); retained.requestCancellation({ ...exportTask, statusKey: 'queued' });
    expect(api.createReportingExportTask).not.toHaveBeenCalled(); expect(api.getReportingExportTask).not.toHaveBeenCalled(); expect(api.downloadReportingExportTask).not.toHaveBeenCalled();
    active.value = true; await flushPromises(); expect(api.listReportingExportTasks).toHaveBeenCalledTimes(2); expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(false);
  });
  it('路由离开取消目录读取且不接入晚结果，返回后重新读取', async () => {
    const pending = deferred<Awaited<ReturnType<typeof api.listReportingExportTasks>>>(); vi.mocked(api.listReportingExportTasks).mockReturnValueOnce(pending.promise);
    const { route } = await setup(all, false, true); const signal = vi.mocked(api.listReportingExportTasks).mock.calls[0]?.[3];
    route.name = 'elsewhere'; await flushPromises(); expect(signal?.aborted).toBe(true);
    pending.resolve({ items: [{ ...exportTask, definitionName: '旧路由结果' }], page: 1, pageSize: 20, total: 1 }); await flushPromises(); expect(wrapper!.text()).not.toContain('旧路由结果');
    route.name = 'reporting-export-tasks'; await flushPromises(); expect(api.listReportingExportTasks).toHaveBeenCalledTimes(2); expect(wrapper!.text()).toContain(exportTask.definitionName);
  });
});
