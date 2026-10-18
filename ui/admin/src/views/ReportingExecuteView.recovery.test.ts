import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElDialog, ElInput, ElPagination } from 'element-plus';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import View from './ReportingExecuteView.vue';
import { listReportingPublishedDefinitions } from '../api/reporting-definitions';
import { executeReportingDefinition } from '../api/reporting-executions';
import { createReportingExportTask } from '../api/reporting-export-tasks';
import { createOutputSession, deferred, exportTask, publishedReportDefinition, reportResult } from '../test/data-output-fixtures';
vi.mock('../api/reporting-definitions', () => ({ listReportingPublishedDefinitions: vi.fn() }));
vi.mock('../api/reporting-executions', () => ({ executeReportingDefinition: vi.fn() }));
vi.mock('../api/reporting-export-tasks', () => ({ createReportingExportTask: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showSuccess: vi.fn() }));
const definition = { ...publishedReportDefinition, parameterSchema: [{ parameterKey: 'filter', displayName: '筛选', dataTypeKey: 'string', isRequired: false, defaultValue: 'original' }] };
let wrapper: VueWrapper, router: Router;
interface State { parameterValues: Map<string, string>; selectedDefinitionVersionKey: string; result?: typeof reportResult; page: number; receipt?: { outcome: string; taskId?: string }; loadDefinitions: () => Promise<void>; runExecute: () => Promise<void>; exportCurrent: () => Promise<void> }
const state = () => wrapper.vm as unknown as State;
const button = (id: string) => wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === id)!;
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([definition]); vi.mocked(executeReportingDefinition).mockResolvedValue(reportResult); vi.mocked(createReportingExportTask).mockResolvedValue({ ...exportTask, parameters: [{ parameterKey: 'filter', value: 'original' }] });
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup(prepare?: (session: ReturnType<typeof createOutputSession>['session']) => void) {
  const context = createOutputSession(['reporting.executions.run', 'reporting.export_tasks.read', 'reporting.export_tasks.create']); prepare?.(context.session);
  router = createRouter({ history: createMemoryHistory(), routes: [{ name: 'reporting-execute', path: '/run', component: { render: () => null } }, { name: 'reporting-export-tasks', path: '/exports', component: { render: () => null } }, { name: 'other', path: '/other', component: { render: () => null } }] }); await router.push({ name: 'reporting-execute' });
  wrapper = mount(View, { global: { plugins: [context.pinia, router], directives: { loading: () => {} }, stubs: { ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' } } } }); await flushPromises(); return context;
}
describe('报表查询与导出恢复', () => {
  it('目录首次失败有独立错误和可见重试入口', async () => {
    vi.mocked(listReportingPublishedDefinitions).mockRejectedValueOnce(new Error('offline')); await setup();
    expect(wrapper.find('[data-testid="reporting-execute-definitions-problem"]').exists()).toBe(true);
    await wrapper.get('[data-testid="reporting-execute-definitions-refresh"]').trigger('click'); await flushPromises(); expect(state().selectedDefinitionVersionKey).toBe(definition.definitionId + ':1');
  });
  it('相同目录重读保留手工参数和版本，目录失败不抹除已确认结果', async () => {
    await setup(); state().parameterValues.set('filter', 'edited'); await state().runExecute(); await state().loadDefinitions(); expect(state().parameterValues.get('filter')).toBe('edited'); expect(state().result).toEqual(reportResult);
    vi.mocked(listReportingPublishedDefinitions).mockRejectedValueOnce(new Error('offline')); await state().loadDefinitions(); expect(state().result).toEqual(reportResult);
  });
  it.each([{ definitionId: '00000000-0000-0000-0000-000000000000' }, { versionNumber: 0 }, { parameterSchema: [{ ...definition.parameterSchema[0]!, parameterKey: '' }] }])('目录拒绝不合法身份或参数 %j', async change => {
    vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([{ ...definition, ...change }]); await setup(); expect(state().selectedDefinitionVersionKey).toBe(''); expect(wrapper.find('[data-testid="reporting-execute-definitions-problem"]').exists()).toBe(true);
  });
  it('大小写等价的重复发布资源不得静默去重', async () => {
    vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([definition, { ...definition, definitionId: definition.definitionId.toUpperCase() }]); await setup(); expect(state().selectedDefinitionVersionKey).toBe('');
  });
  it('参数变化立即清空旧结果且下次查询从第一页开始', async () => {
    await setup(); await state().runExecute(); state().page = 3; await wrapper.getComponent(ElInput).setValue('changed'); expect(state().result).toBeUndefined(); expect(state().page).toBe(1);
  });
  it('旧参数的执行回调不能提交新参数', async () => {
    await setup(); const old = button('reporting-execute-run').vm.$.vnode.props!.onClick as () => void; await wrapper.getComponent(ElInput).setValue('new'); old(); await flushPromises(); expect(executeReportingDefinition).not.toHaveBeenCalled();
  });
  it.each(['filter', '__proto__', '__v_isRef', '__isVue'])('参数%s直接改值取消查询并拒绝迟到结果', async parameterKey => {
    vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([{ ...definition, parameterSchema: [{ ...definition.parameterSchema[0]!, parameterKey }] }]); await setup();
    const pending = deferred<typeof reportResult>(); vi.mocked(executeReportingDefinition).mockReturnValueOnce(pending.promise); const reading = state().runExecute(), signal = vi.mocked(executeReportingDefinition).mock.calls[0]![4]!; state().parameterValues.set(parameterKey, 'changed'); expect(signal.aborted).toBe(true); pending.resolve(reportResult); await reading; expect(state().result).toBeUndefined();
  });
  it('分页只接收自身页码，拒绝旧分页回调', async () => {
    await setup(); vi.mocked(executeReportingDefinition).mockResolvedValueOnce({ ...reportResult, hasMore: true, totalRows: 120 }); await state().runExecute(); await flushPromises();
    const paging = wrapper.getComponent(ElPagination), old = paging.vm.$.vnode.props!.onChange as (page: number, size: number) => void; paging.vm.$emit('change', 2, 50); await flushPromises(); expect(state().result).toBeUndefined();
    old(3, 50); await flushPromises(); expect(executeReportingDefinition).toHaveBeenCalledTimes(2);
  });
  it('受控分页重复同页事件只查询一次', async () => {
    await setup(); vi.mocked(executeReportingDefinition).mockResolvedValueOnce({ ...reportResult, hasMore: true, totalRows: 120 }).mockResolvedValueOnce({ ...reportResult, page: 2 }); await state().runExecute(); await flushPromises();
    const paging = wrapper.getComponent(ElPagination); paging.vm.$emit('change', 2, 50); paging.vm.$emit('change', 2, 50); await flushPromises(); expect(executeReportingDefinition).toHaveBeenCalledTimes(2);
  });
  it('真实分页渲染，点击下一页先更新受控模型再仅执行一次', async () => {
    await setup(); vi.mocked(executeReportingDefinition).mockImplementation(async (_id, _body, page = 1, pageSize = 50) => ({ ...reportResult, page, pageSize, totalRows: 120, hasMore: page < 3 })); await state().runExecute(); await flushPromises(); const pager = wrapper.getComponent(ElPagination); expect(pager.find('.el-pagination').exists()).toBe(true); await pager.get('.btn-next').trigger('click'); await flushPromises(); expect(vi.mocked(executeReportingDefinition).mock.calls.at(-1)?.slice(2, 4)).toEqual([2, 50]); expect(executeReportingDefinition).toHaveBeenCalledTimes(2);
  });
  it.each([0, 302, 408, 503])('导出状态%s不确定时跨选择保留阻断，确认核对后仅允许手动重试', async status => {
    await setup(); vi.mocked(createReportingExportTask).mockRejectedValueOnce({ status, code: 'export.unknown', title: 'unknown' }); await state().exportCurrent(); expect(state().receipt?.outcome).toBe('unconfirmed');
    state().parameterValues.set('filter', 'another'); await state().runExecute(); state().parameterValues.set('filter', 'original'); await state().exportCurrent(); expect(createReportingExportTask).toHaveBeenCalledOnce();
    await wrapper.get('[data-testid="reporting-execute-export-recovery"]').trigger('click'); await flushPromises(); const dialog = wrapper.getComponent(ElDialog); expect(dialog.props('modelValue')).toBe(true);
    await wrapper.get('[data-testid="reporting-execute-export-recovery-confirm"]').trigger('click'); await flushPromises(); expect(createReportingExportTask).toHaveBeenCalledOnce(); await state().exportCurrent(); expect(createReportingExportTask).toHaveBeenCalledTimes(2);
  });
  it('明确409拒绝允许手工重试', async () => {
    await setup(); vi.mocked(createReportingExportTask).mockRejectedValueOnce({ status: 409, code: 'export.conflict', title: 'conflict' }); await state().exportCurrent(); expect(state().receipt?.outcome).toBe('failed'); await state().exportCurrent(); expect(createReportingExportTask).toHaveBeenCalledTimes(2);
  });
  it('在途导出参数直接变化标记结果不确定，旧完成不导航', async () => {
    await setup(); const pending = deferred<typeof exportTask>(); vi.mocked(createReportingExportTask).mockReturnValueOnce(pending.promise); const creating = state().exportCurrent(), signal = vi.mocked(createReportingExportTask).mock.calls[0]![1]!;
    state().parameterValues.set('filter', 'other'); expect(signal.aborted).toBe(true); expect(state().receipt?.outcome).toBe('unconfirmed'); pending.resolve(exportTask); await creating; expect(router.currentRoute.value.name).toBe('reporting-execute');
  });
  it('创建成功但跳转失败保留成功回执和打开任务入口，不归类为创建失败', async () => {
    await setup(); const navigate = vi.spyOn(router, 'push').mockRejectedValueOnce(new Error('navigation')); await state().exportCurrent(); await flushPromises(); expect(state().receipt).toMatchObject({ outcome: 'succeeded', taskId: exportTask.id }); expect(wrapper.find('[data-testid="reporting-execute-navigation-problem"]').exists()).toBe(true);
    await state().exportCurrent(); expect(createReportingExportTask).toHaveBeenCalledOnce(); await wrapper.get('[data-testid="reporting-execute-open-export"]').trigger('click'); await flushPromises(); expect(navigate).toHaveBeenCalledTimes(2); expect(router.currentRoute.value.query.taskId).toBe(exportTask.id);
  });
  it('错误请求者返回不得导航且结果保持不确定', async () => {
    await setup(); vi.mocked(createReportingExportTask).mockResolvedValue({ ...exportTask, requestedByUserId: '019bc2b1-2a40-7cc3-8992-a80de51bf298' }); await state().exportCurrent(); expect(state().receipt?.outcome).toBe('unconfirmed'); expect(router.currentRoute.value.name).toBe('reporting-execute');
  });
  it('旧恢复确认不解除新意图的重复提交阻断', async () => {
    await setup(); vi.mocked(createReportingExportTask).mockRejectedValue(new Error('offline')); await state().exportCurrent(); await wrapper.get('[data-testid="reporting-execute-export-recovery"]').trigger('click'); await flushPromises(); const old = button('reporting-execute-export-recovery-confirm').vm.$.vnode.props!.onClick as () => void;
    state().parameterValues.set('filter', 'other'); await state().exportCurrent(); old(); await state().exportCurrent(); expect(createReportingExportTask).toHaveBeenCalledTimes(2);
  });
  it('无可信有效作用域不读取目录，Host账号切入有效租户仍可用', async () => {
    await setup(session => { session.currentUser!.scope = 'tenant:mismatch'; }); expect(listReportingPublishedDefinitions).not.toHaveBeenCalled(); wrapper.unmount(); await setup(session => { session.currentUser!.actorScope = 'host'; }); expect(listReportingPublishedDefinitions).toHaveBeenCalledOnce();
  });
  it('路由离开后旧执行和重读回调无效，返回恢复目录', async () => {
    await setup(); const oldRun = button('reporting-execute-run').vm.$.vnode.props!.onClick as () => void, oldReload = button('reporting-execute-definitions-refresh').vm.$.vnode.props!.onClick as () => void; await router.push({ name: 'other' }); await router.push({ name: 'reporting-execute' }); await flushPromises(); oldRun(); oldReload(); await flushPromises(); expect(executeReportingDefinition).not.toHaveBeenCalled(); expect(listReportingPublishedDefinitions).toHaveBeenCalledTimes(2);
  });
  it('结果表格与分页共用自适应容器，标题位于容器外', async () => {
    await setup(); await state().runExecute(); await flushPromises(); const main = wrapper.get('.art-crud-table-main'); expect(main.find('.el-table').exists()).toBe(true); expect(main.find('[data-testid="reporting-execute-result-header"]').exists()).toBe(false); expect(wrapper.get('.result-card').classes()).toContain('art-table-card');
  });
});
