import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElDialog, ElDrawer, ElMessage, ElPagination } from 'element-plus';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import View from './ReportingExportTasksView.vue';
import * as api from '../api/reporting-export-tasks';
import { listReportingPublishedDefinitions } from '../api/reporting-definitions';
import { createOutputSession, deferred, exportTask, publishedReportDefinition } from '../test/data-output-fixtures';
vi.mock('../api/reporting-export-tasks', () => ({ getReportingExportTask: vi.fn(), listReportingExportTasks: vi.fn(), createReportingExportTask: vi.fn(), cancelReportingExportTask: vi.fn(), downloadReportingExportTask: vi.fn() }));
vi.mock('../api/reporting-definitions', () => ({ listReportingPublishedDefinitions: vi.fn() }));
const read = 'reporting.export_tasks.read', create = 'reporting.export_tasks.create', run = 'reporting.executions.run', cancel = 'reporting.export_tasks.cancel';
const otherId = '019bc2b1-2a40-7cc3-8992-a80de51bf298', queued = { ...exportTask, statusKey: 'queued', outputFileId: null, outputFileName: null, completedAtUtc: null };
let wrapper: VueWrapper;
interface State { items: typeof exportTask[]; page: number; pageSize: number; selectedTask?: typeof exportTask; detailVisible: boolean; createDialogVisible: boolean; selectedDefinitionVersionKey: string; parameterValues: Map<string, string>; receipt?: { taskId?: string; kind: string; outcome: string }; load: () => Promise<boolean>; loadDefinitions: () => Promise<void>; openCreateDialog: () => void; submitCreate: () => Promise<void>; openDetail: (id: string) => Promise<void>; refreshDetail: (id: string) => Promise<void>; requestCancellation: (task: typeof exportTask) => void; confirmCancellation: () => Promise<void>; acknowledgeCreate: () => void }
const state = () => wrapper.vm as unknown as State;
const button = (id: string) => wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === id)!;
beforeEach(() => {
  vi.resetAllMocks(); vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
  vi.mocked(api.listReportingExportTasks).mockResolvedValue({ items: [{ ...queued }], page: 1, pageSize: 20, total: 1 }); vi.mocked(api.getReportingExportTask).mockResolvedValue({ ...queued }); vi.mocked(api.createReportingExportTask).mockResolvedValue({ ...queued }); vi.mocked(api.cancelReportingExportTask).mockResolvedValue({ ...queued, statusKey: 'cancelled' });
  vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([{ ...publishedReportDefinition }]);
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup() {
  const context = createOutputSession([read, create, run, cancel]), route = reactive({ name: 'reporting-export-tasks', query: {} });
  wrapper = mount(View, { global: { plugins: [context.pinia], provide: { [routeLocationKey as symbol]: route }, directives: { loading: () => {} }, stubs: {
    ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }, ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' }
  } } }); await flushPromises(); return { ...context, route };
}
describe('报表导出目录与任务恢复', () => {
  it('定义首次失败独立显示，列表刷新不能吞掉目录错误，可见重试可恢复', async () => {
    vi.mocked(listReportingPublishedDefinitions).mockRejectedValueOnce(new Error('offline')); await setup(); state().openCreateDialog(); await flushPromises(); await state().load(); await flushPromises();
    expect(wrapper.find('[data-testid="reporting-export-definitions-problem"]').exists()).toBe(true); await wrapper.get('[data-testid="reporting-export-definitions-refresh"]').trigger('click'); await flushPromises(); expect(state().selectedDefinitionVersionKey).toBe(exportTask.definitionId + ':1');
  });
  it('相同定义目录重读保留所选版本和手工参数', async () => {
    const rows = [{ ...publishedReportDefinition, parameterSchema: [{ parameterKey: 'year', displayName: '年', dataTypeKey: 'string', isRequired: false, defaultValue: '2025', minimum: null, maximum: null }] }, { ...publishedReportDefinition, versionNumber: 2 }];
    vi.mocked(listReportingPublishedDefinitions).mockResolvedValue(rows); await setup(); state().parameterValues.set('year', '2026'); await state().loadDefinitions(); expect(state().parameterValues.get('year')).toBe('2026');
    state().selectedDefinitionVersionKey = exportTask.definitionId + ':2'; await state().loadDefinitions(); expect(state().selectedDefinitionVersionKey).toBe(exportTask.definitionId + ':2');
  });
  it('同查询失败保留行，新页在读取前清空旧行', async () => {
    await setup(); vi.mocked(api.listReportingExportTasks).mockRejectedValueOnce(new Error('offline')); await state().load(); expect(state().items).toHaveLength(1);
    state().page = 2; const pending = deferred<Awaited<ReturnType<typeof api.listReportingExportTasks>>>(); vi.mocked(api.listReportingExportTasks).mockReturnValueOnce(pending.promise); const reading = state().load(); expect(state().items).toEqual([]); pending.reject(new Error('offline')); await reading;
  });
  it('特殊参数机器键作为Map键保留，不改变存储对象原型', async () => {
    vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([{ ...publishedReportDefinition, parameterSchema: [{ parameterKey: '__proto__', displayName: '参数', dataTypeKey: 'string', isRequired: false, defaultValue: 'safe', minimum: null, maximum: null }] }]);
    await setup(); expect(state().parameterValues.has('__proto__')).toBe(true); expect(state().parameterValues.get('__proto__')).toBe('safe'); expect(Object.getPrototypeOf(state().parameterValues)).toBe(Map.prototype);
  });
  it.each(['__proto__', '__v_isRef', '__isVue'])('特殊参数%s改值使旧回调失效并取消在途创建', async parameterKey => {
    vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([{ ...publishedReportDefinition, parameterSchema: [{ parameterKey, displayName: '参数', dataTypeKey: 'string', isRequired: false, defaultValue: 'safe', minimum: null, maximum: null }] }]);
    await setup(); state().openCreateDialog(); await flushPromises(); const submit = button('reporting-export-submit').vm.$.vnode.props!.onClick as () => void;
    const oldIntent = (wrapper.vm as unknown as { createIntent: string }).createIntent; state().parameterValues.set(parameterKey, 'changed'); expect((wrapper.vm as unknown as { createIntent: string }).createIntent).not.toBe(oldIntent); submit(); expect(api.createReportingExportTask).not.toHaveBeenCalled();
    const pending = deferred<typeof exportTask>(); vi.mocked(api.createReportingExportTask).mockReturnValueOnce(pending.promise); const creating = state().submitCreate(); state().parameterValues.set(parameterKey, 'later'); expect(vi.mocked(api.createReportingExportTask).mock.calls[0]![1]!.aborted).toBe(true); pending.resolve(queued); await creating; expect(state().receipt?.outcome).toBe('unconfirmed'); expect(ElMessage.success).not.toHaveBeenCalled();
  });
  it('错误页回包不能改写请求页或接入旧行', async () => { await setup(); state().page = 2; await state().load(); expect(state().page).toBe(2); expect(state().items).toEqual([]); });
  it('页大小变化回第一页，重复受控change不再读取', async () => {
    await setup(); const pager = wrapper.getComponent(ElPagination); pager.vm.$emit('change', 1, 20); await flushPromises(); expect(api.listReportingExportTasks).toHaveBeenCalledOnce();
    vi.mocked(api.listReportingExportTasks).mockResolvedValueOnce({ items: [], page: 1, pageSize: 50, total: 0 }); pager.vm.$emit('update:pageSize', 50); pager.vm.$emit('update:currentPage', 3); pager.vm.$emit('change', 3, 50); await flushPromises(); expect(api.listReportingExportTasks).toHaveBeenLastCalledWith(1, 50, undefined, expect.any(AbortSignal));
  });
  it('空越界页最多补读一次末页', async () => {
    await setup(); state().page = 3; vi.mocked(api.listReportingExportTasks).mockResolvedValueOnce({ items: [], page: 3, pageSize: 20, total: 21 }).mockResolvedValueOnce({ items: [], page: 2, pageSize: 20, total: 0 }); await state().load(); expect(api.listReportingExportTasks).toHaveBeenCalledTimes(3); expect(state().page).toBe(2);
  });
  it('详情首次失败保留抽屉重试入口，补读成功后可继续', async () => {
    await setup(); vi.mocked(api.getReportingExportTask).mockRejectedValueOnce(new Error('offline')); await state().openDetail(exportTask.id); expect(state().detailVisible).toBe(true);
    await wrapper.get('[data-testid="reporting-export-detail-refresh"]').trigger('click'); await flushPromises(); expect(state().selectedTask?.id).toBe(exportTask.id);
  });
  it('详情补读失败保留确认内容，目录刷新不清详情错误', async () => {
    await setup(); await state().openDetail(exportTask.id); vi.mocked(api.getReportingExportTask).mockRejectedValueOnce(new Error('offline')); await state().refreshDetail(exportTask.id); await state().load(); await flushPromises(); expect(state().selectedTask?.id).toBe(exportTask.id); expect(wrapper.find('[data-testid="reporting-export-detail-problem"]').exists()).toBe(true);
  });
  it('旧抽屉关闭回调不能关闭下一任务', async () => {
    await setup(); await state().openDetail(exportTask.id); const close = wrapper.getComponent(ElDrawer).vm.$.vnode.props!['onUpdate:modelValue'] as (open: boolean) => void; vi.mocked(api.getReportingExportTask).mockResolvedValueOnce({ ...queued, id: otherId }); await state().openDetail(otherId); close(false); expect(state().detailVisible).toBe(true);
  });
  it('旧创建提交和关闭回调不能操作新窗口', async () => {
    await setup(); state().openCreateDialog(); await flushPromises(); const submit = button('reporting-export-submit').vm.$.vnode.props!.onClick as () => void, close = wrapper.getComponent(ElDialog).vm.$.vnode.props!['onUpdate:modelValue'] as (open: boolean) => void;
    state().createDialogVisible = false; state().openCreateDialog(); await flushPromises(); submit(); close(false); expect(api.createReportingExportTask).not.toHaveBeenCalled(); expect(state().createDialogVisible).toBe(true);
  });
  it('创建成功后目录失败仍保留创建回执和权威详情，不额外GET', async () => {
    await setup(); state().openCreateDialog(); vi.mocked(api.listReportingExportTasks).mockRejectedValueOnce(new Error('offline')); await state().submitCreate(); expect(state().receipt).toMatchObject({ kind: 'create', taskId: exportTask.id, outcome: 'succeeded' }); expect(state().selectedTask?.id).toBe(exportTask.id); expect(api.getReportingExportTask).not.toHaveBeenCalled(); expect(state().createDialogVisible).toBe(false);
  });
  it('直接更换发布版本取消在途创建，旧结果只保留未知事实', async () => {
    vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([{ ...publishedReportDefinition }, { ...publishedReportDefinition, versionNumber: 2 }]); await setup(); state().openCreateDialog(); const pending = deferred<typeof exportTask>(); vi.mocked(api.createReportingExportTask).mockReturnValueOnce(pending.promise); const creating = state().submitCreate(); state().selectedDefinitionVersionKey = exportTask.definitionId + ':2'; expect(vi.mocked(api.createReportingExportTask).mock.calls[0]![1]!.aborted).toBe(true); pending.resolve(queued); await creating; expect(state().receipt?.outcome).toBe('unconfirmed'); expect(ElMessage.success).not.toHaveBeenCalled();
  });
  it('未知创建跨关闭和参数切换保留，核对确认只解锁人工提交', async () => {
    await setup(); state().openCreateDialog(); vi.mocked(api.createReportingExportTask).mockRejectedValueOnce(new Error('lost')); await state().submitCreate(); state().createDialogVisible = false; state().openCreateDialog(); await state().submitCreate(); expect(api.createReportingExportTask).toHaveBeenCalledOnce(); state().acknowledgeCreate(); await flushPromises(); await wrapper.get('[data-testid="reporting-export-recovery-confirm"]').trigger('click'); await flushPromises(); expect(api.createReportingExportTask).toHaveBeenCalledOnce(); await state().submitCreate(); expect(api.createReportingExportTask).toHaveBeenCalledTimes(2);
  });
  it.each([0, 302, 408, 503])('创建状态%s不能解除未知重发门槛', async status => {
    await setup(); state().openCreateDialog(); vi.mocked(api.createReportingExportTask).mockRejectedValueOnce({ status, code: 'gateway.failure' }); await state().submitCreate(); await state().submitCreate(); expect(api.createReportingExportTask).toHaveBeenCalledOnce(); expect(state().receipt?.outcome).toBe('unconfirmed');
  });
  it('明确409创建拒绝可人工重试', async () => {
    await setup(); state().openCreateDialog(); vi.mocked(api.createReportingExportTask).mockRejectedValueOnce({ status: 409, code: 'reporting.export.rejected' }); await state().submitCreate(); expect(state().receipt?.outcome).toBe('failed'); await state().submitCreate(); expect(api.createReportingExportTask).toHaveBeenCalledTimes(2);
  });
  it('取消确认后补读失败保留终态和回执', async () => {
    await setup(); state().requestCancellation(queued); vi.mocked(api.listReportingExportTasks).mockRejectedValueOnce(new Error('offline')); await state().confirmCancellation(); expect(state().items[0]?.statusKey).toBe('cancelled'); expect(state().receipt).toMatchObject({ kind: 'cancel', taskId: exportTask.id, outcome: 'succeeded' });
  });
  it('早发列表与后来旧排队状态不能恢复已确认取消', async () => {
    await setup(); state().requestCancellation(queued); const pending = deferred<typeof exportTask>(); vi.mocked(api.cancelReportingExportTask).mockReturnValueOnce(pending.promise); const cancelling = state().confirmCancellation(); const old = deferred<Awaited<ReturnType<typeof api.listReportingExportTasks>>>(); vi.mocked(api.listReportingExportTasks).mockReturnValueOnce(old.promise); const reading = state().load(); pending.resolve({ ...queued, statusKey: 'cancelled' }); await cancelling; old.resolve({ items: [queued], page: 1, pageSize: 20, total: 1 }); await reading; expect(state().items[0]?.statusKey).toBe('cancelled');
  });
  it('未知取消不因目录读取解除，详情核对同一来源后才可手动再取消', async () => {
    await setup(); state().requestCancellation(queued); vi.mocked(api.cancelReportingExportTask).mockRejectedValueOnce(new Error('lost')); await state().confirmCancellation(); await state().load(); state().requestCancellation(queued); await state().confirmCancellation(); expect(api.cancelReportingExportTask).toHaveBeenCalledOnce(); await state().openDetail(exportTask.id); state().requestCancellation(queued); await state().confirmCancellation(); expect(api.cancelReportingExportTask).toHaveBeenCalledTimes(2);
  });
  it('取消结果来源定义错配不能记录成功', async () => {
    await setup(); state().requestCancellation(queued); vi.mocked(api.cancelReportingExportTask).mockResolvedValueOnce({ ...queued, statusKey: 'cancelled', definitionId: otherId }); await state().confirmCancellation(); expect(state().items[0]?.statusKey).toBe('queued'); expect(state().receipt?.outcome).toBe('unconfirmed');
  });
  it('详情关闭保留取消未知来源，重开错定义不能解除', async () => {
    await setup(); await state().openDetail(exportTask.id); state().requestCancellation(queued); const pending = deferred<typeof exportTask>(); vi.mocked(api.cancelReportingExportTask).mockReturnValueOnce(pending.promise); const cancelling = state().confirmCancellation(); state().detailVisible = false; pending.reject(new Error('lost')); await cancelling; vi.mocked(api.getReportingExportTask).mockResolvedValueOnce({ ...queued, definitionId: otherId }); await state().openDetail(exportTask.id); expect(state().selectedTask).toBeUndefined(); expect((wrapper.vm as unknown as { cancellationBlocks: Map<string, unknown> }).cancellationBlocks.has(exportTask.id)).toBe(true);
  });
  it('新页下旧行回调不能发起取消确认', async () => {
    await setup(); const row = (wrapper.vm as unknown as { tableRows: { cancel: () => void }[] }).tableRows[0]!; state().page = 2; vi.mocked(api.listReportingExportTasks).mockResolvedValueOnce({ items: [queued], page: 2, pageSize: 20, total: 21 }); await state().load(); row.cancel(); await flushPromises(); expect(wrapper.find('[data-testid="reporting-cancel-confirm"]').exists()).toBe(false);
  });
  it('可信作用域不匹配时不再读取或提供导出', async () => {
    const { session } = await setup(); session.currentUser!.scope = 'host'; await flushPromises(); await state().load(); state().openCreateDialog(); expect(state().items).toEqual([]); expect(api.listReportingExportTasks).toHaveBeenCalledOnce(); expect(state().createDialogVisible).toBe(false);
  });
  it('表头和错误在测量容器外，分页位于底部直接子项', async () => { await setup(); expect(wrapper.get('.art-crud-table-main > .art-table-pagination').exists()).toBe(true); expect(wrapper.find('.art-crud-table-main .art-table-header').exists()).toBe(false); });
});
