import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { ElButton, ElDrawer, ElMessage, ElPagination } from 'element-plus';
import ImportExportTasksView from './ImportExportTasksView.vue';
import * as api from '../api/import-export-tasks';
import { createOutputSession, deferred } from '../test/data-output-fixtures';
import { importTask } from '../test/task-lifecycle-fixtures';

vi.mock('../api/import-export-tasks', () => ({ listImportExportTasks: vi.fn(), getImportExportTask: vi.fn(), executeImportExportTask: vi.fn(), resumeImportExportTask: vi.fn(), retryImportExportTask: vi.fn(), cancelImportExportTask: vi.fn(), downloadImportExportTaskErrorReceipt: vi.fn(), listStaticImportSchemas: vi.fn(), createImportExportTask: vi.fn(), downloadStaticImportTemplate: vi.fn() }));
const read = 'import_export.import_tasks.read', execute = 'import_export.import_tasks.execute', cancel = 'import_export.import_tasks.cancel';
const other = { ...importTask, id: '019bc2b1-2a40-7cc3-8992-a80de51bf298', schemaDisplayName: '另一任务' };
let wrapper: VueWrapper;
interface State { items: typeof importTask[]; selectedTask?: typeof importTask; page: number; pageSize: number; total: number; drawerVisible: boolean; actionReceipt?: { taskId: string; outcome: string }; load: () => Promise<boolean>; openDetail: (id: string) => Promise<void>; refreshDetail: (id: string) => Promise<void>; runAction: (kind: 'execute' | 'resume' | 'retry' | 'cancel') => Promise<void>; requestCancellation: () => void; confirmCancellation: () => Promise<void> }
const state = () => wrapper.vm as unknown as State;
const button = (id: string) => wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === id)!;
beforeEach(() => {
  vi.resetAllMocks(); vi.spyOn(ElMessage, 'error').mockImplementation(() => ({ close() {} })); vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} })); vi.spyOn(ElMessage, 'warning').mockImplementation(() => ({ close() {} }));
  vi.mocked(api.listImportExportTasks).mockResolvedValue({ items: [importTask, other], page: 1, pageSize: 20, total: 2 });
  vi.mocked(api.getImportExportTask).mockImplementation(async id => ({ ...(id.toLowerCase() === other.id ? other : importTask) }));
  vi.mocked(api.executeImportExportTask).mockResolvedValue({ ...importTask, statusKey: 'queued', version: 2 });
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup(permissions = [read, execute, cancel]) {
  const context = createOutputSession(permissions), route = reactive({ name: 'import-export-tasks', query: {} });
  wrapper = mount(ImportExportTasksView, { global: { plugins: [context.pinia], provide: { [routeLocationKey as symbol]: route }, directives: { loading: {} }, stubs: {
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' },
    ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' },
    ImportTaskCreateDialog: true
  } } }); await flushPromises(); return { ...context, route };
}
describe('导入任务归属与失败恢复', () => {
  it('父读取权限缺失时直接调用也不读取或执行', async () => {
    await setup([execute, cancel]); await state().openDetail(importTask.id); await state().load(); await state().runAction('execute');
    expect(api.listImportExportTasks).not.toHaveBeenCalled(); expect(api.getImportExportTask).not.toHaveBeenCalled(); expect(api.executeImportExportTask).not.toHaveBeenCalled();
  });
  it('可信租户不匹配时撤销列表和详情入口', async () => {
    const { session } = await setup(); session.currentUser!.scope = 'host'; await flushPromises(); await state().load(); await state().openDetail(importTask.id);
    expect(state().items).toEqual([]); expect(api.getImportExportTask).not.toHaveBeenCalled(); expect(api.listImportExportTasks).toHaveBeenCalledOnce();
  });
  it.each(['bad-id', '00000000-0000-0000-0000-000000000000'])('非法详情身份 %s 不发请求', async id => {
    await setup(); await state().openDetail(id); expect(api.getImportExportTask).not.toHaveBeenCalled();
  });
  it('路由离开取消列表与在途写入，返回只读目录', async () => {
    const { route } = await setup(); await state().openDetail(importTask.id); const pending = deferred<typeof importTask>(); vi.mocked(api.executeImportExportTask).mockReturnValueOnce(pending.promise);
    const action = state().runAction('execute'), signal = vi.mocked(api.executeImportExportTask).mock.calls[0]![1]!; route.name = 'elsewhere'; await flushPromises();
    expect(signal.aborted).toBe(true); expect(state().items).toEqual([]); pending.resolve({ ...importTask, statusKey: 'queued', version: 2 }); await action; expect(ElMessage.success).not.toHaveBeenCalled();
    route.name = 'import-export-tasks'; await flushPromises(); expect(api.listImportExportTasks).toHaveBeenCalledTimes(2); expect(api.executeImportExportTask).toHaveBeenCalledOnce();
  });
  it('不同页读取立即清除旧行，同页补读失败保留结果', async () => {
    await setup(); vi.mocked(api.listImportExportTasks).mockRejectedValueOnce(new Error('offline')); await state().load(); expect(state().items).toHaveLength(2);
    state().page = 2; const pending = deferred<Awaited<ReturnType<typeof api.listImportExportTasks>>>(); vi.mocked(api.listImportExportTasks).mockReturnValueOnce(pending.promise); const loading = state().load(); expect(state().items).toEqual([]);
    pending.reject(new Error('offline')); await loading; expect(state().items).toEqual([]);
  });
  it('列表拒绝错误页回包，不能改写请求页码', async () => {
    await setup(); state().page = 2; vi.mocked(api.listImportExportTasks).mockResolvedValueOnce({ items: [importTask], page: 1, pageSize: 20, total: 1 }); await state().load();
    expect(state().items).toEqual([]); expect(state().page).toBe(2);
  });
  it('分页 change 合并页大小和页码变化，重复模型事件不再读取', async () => {
    await setup(); const pagination = wrapper.getComponent(ElPagination);
    pagination.vm.$emit('change', 1, 20); await flushPromises(); expect(api.listImportExportTasks).toHaveBeenCalledOnce();
    vi.mocked(api.listImportExportTasks).mockResolvedValueOnce({ items: [], page: 1, pageSize: 50, total: 0 });
    pagination.vm.$emit('update:pageSize', 50); pagination.vm.$emit('update:currentPage', 3); pagination.vm.$emit('change', 3, 50); await flushPromises();
    expect(api.listImportExportTasks).toHaveBeenLastCalledWith(1, 50, undefined, expect.any(AbortSignal)); expect(api.listImportExportTasks).toHaveBeenCalledTimes(2);
  });
  it('越界空页最多恢复读取一次末页', async () => {
    await setup(); state().page = 3; vi.mocked(api.listImportExportTasks).mockResolvedValueOnce({ items: [], page: 3, pageSize: 20, total: 21 }).mockResolvedValueOnce({ items: [], page: 2, pageSize: 20, total: 0 }); await state().load();
    expect(api.listImportExportTasks).toHaveBeenCalledTimes(3); expect(state().page).toBe(2);
  });
  it('详情补读失败保留已确认内容和可见重试入口', async () => {
    await setup(); await state().openDetail(importTask.id); vi.mocked(api.getImportExportTask).mockRejectedValueOnce(new Error('offline')); await state().refreshDetail(importTask.id);
    expect(state().drawerVisible).toBe(true); expect(state().selectedTask?.id).toBe(importTask.id);
    await state().load(); await flushPromises(); expect(wrapper.find('[data-testid="import-task-detail-problem"]').exists()).toBe(true);
    await wrapper.get('[data-testid="import-export-task-detail-refresh"]').trigger('click'); await flushPromises(); expect(api.getImportExportTask).toHaveBeenCalledTimes(3);
    expect(wrapper.find('[data-testid="import-task-detail-problem"]').exists()).toBe(false);
  });
  it('首次详情读取失败仍可从抽屉显式重试', async () => {
    await setup(); vi.mocked(api.getImportExportTask).mockRejectedValueOnce(new Error('offline')); await state().openDetail(importTask.id);
    expect(state().drawerVisible).toBe(true); await wrapper.get('[data-testid="import-export-task-detail-refresh"]').trigger('click'); await flushPromises(); expect(state().selectedTask?.id).toBe(importTask.id);
  });
  it('同任务更早版本详情不能覆盖已确认新版本', async () => {
    await setup(); await state().openDetail(importTask.id); await state().runAction('execute'); await state().refreshDetail(importTask.id);
    expect(state().selectedTask?.version).toBe(2); expect(state().selectedTask?.statusKey).toBe('queued');
  });
  it('详情拒绝其他租户，即使任务ID正确也不能接入', async () => {
    await setup(); vi.mocked(api.getImportExportTask).mockResolvedValueOnce({ ...importTask, tenantId: other.id }); await state().openDetail(importTask.id);
    expect(state().selectedTask).toBeUndefined(); expect(api.executeImportExportTask).not.toHaveBeenCalled();
  });
  it('旧详情按钮不能执行下一任务', async () => {
    await setup(); await state().openDetail(importTask.id); const click = button('import-export-task-execute').vm.$.vnode.props!.onClick as () => void;
    await state().openDetail(other.id); click(); await flushPromises(); expect(api.executeImportExportTask).not.toHaveBeenCalled();
  });
  it('旧抽屉关闭回调不能关闭新详情', async () => {
    await setup(); await state().openDetail(importTask.id); const close = wrapper.getComponent(ElDrawer).vm.$.vnode.props!['onUpdate:modelValue'] as (open: boolean) => void;
    await state().openDetail(other.id); close(false); await flushPromises(); expect(state().drawerVisible).toBe(true); expect(state().selectedTask?.id).toBe(other.id);
  });
  it('旧确认和关闭回调不能批准或清除新取消确认', async () => {
    await setup(); await state().openDetail(importTask.id); state().requestCancellation(); await flushPromises(); const click = button('import-cancel-confirm').vm.$.vnode.props!.onClick as () => void;
    await state().openDetail(other.id); state().requestCancellation(); await flushPromises(); click(); await flushPromises(); expect(api.cancelImportExportTask).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="import-cancel-confirm"]').exists()).toBe(true);
  });
  it('确认成功与目录补读失败分别记录，保留任务新状态', async () => {
    await setup(); await state().openDetail(importTask.id); vi.mocked(api.listImportExportTasks).mockRejectedValueOnce(new Error('offline')); await state().runAction('execute');
    expect(state().selectedTask?.statusKey).toBe('queued'); expect(state().items[0]?.statusKey).toBe('queued'); expect(state().actionReceipt?.outcome).toBe('succeeded');
  });
  it('未知提交失败读取不解锁，成功详情核对后才允许人工重试', async () => {
    await setup(); await state().openDetail(importTask.id); vi.mocked(api.executeImportExportTask).mockRejectedValueOnce(new Error('lost'));
    await state().runAction('execute'); await state().runAction('execute'); expect(api.executeImportExportTask).toHaveBeenCalledOnce();
    vi.mocked(api.getImportExportTask).mockRejectedValueOnce(new Error('offline')); await state().refreshDetail(importTask.id); await state().runAction('execute'); expect(api.executeImportExportTask).toHaveBeenCalledOnce();
    await state().refreshDetail(importTask.id); await state().runAction('execute'); expect(api.executeImportExportTask).toHaveBeenCalledTimes(2);
  });
  it('读取其他任务与列表刷新不能解除原任务未知门槛', async () => {
    await setup(); await state().openDetail(importTask.id); vi.mocked(api.executeImportExportTask).mockRejectedValueOnce(new Error('lost')); await state().runAction('execute'); await state().openDetail(other.id); await state().load();
    vi.mocked(api.getImportExportTask).mockRejectedValueOnce(new Error('offline')); await state().openDetail(importTask.id); expect(state().actionReceipt?.outcome).toBe('unconfirmed'); expect(api.executeImportExportTask).toHaveBeenCalledOnce();
  });
  it.each([0, 302, 408, 503])('状态%s属于未知提交，不能立即再次执行', async status => {
    await setup(); await state().openDetail(importTask.id); vi.mocked(api.executeImportExportTask).mockRejectedValueOnce({ status, code: 'gateway.failure' }); await state().runAction('execute'); await state().runAction('execute');
    expect(api.executeImportExportTask).toHaveBeenCalledOnce(); expect(state().actionReceipt?.outcome).toBe('unconfirmed');
  });
  it('明确409拒绝保留回执并允许人工重试', async () => {
    await setup(); await state().openDetail(importTask.id); vi.mocked(api.executeImportExportTask).mockRejectedValueOnce({ status: 409, code: 'import_export.task.state_conflict' }); await state().runAction('execute'); expect(state().actionReceipt?.outcome).toBe('failed');
    await state().runAction('execute'); expect(api.executeImportExportTask).toHaveBeenCalledTimes(2);
  });
  it('关闭在途提交后保留未知回执，晚成功不能提示或刷新', async () => {
    await setup(); await state().openDetail(importTask.id); const pending = deferred<typeof importTask>(); vi.mocked(api.executeImportExportTask).mockReturnValueOnce(pending.promise); const action = state().runAction('execute'); state().drawerVisible = false;
    pending.resolve({ ...importTask, statusKey: 'queued', version: 2 }); await action; expect(state().actionReceipt?.outcome).toBe('unconfirmed'); expect(ElMessage.success).not.toHaveBeenCalled(); expect(api.listImportExportTasks).toHaveBeenCalledOnce();
  });
  it('关闭后重开收到低于未知提交基线的详情，不能解除门槛', async () => {
    await setup(); vi.mocked(api.getImportExportTask).mockResolvedValueOnce({ ...importTask, version: 5 }); await state().openDetail(importTask.id);
    const pending = deferred<typeof importTask>(); vi.mocked(api.executeImportExportTask).mockReturnValueOnce(pending.promise); const action = state().runAction('execute'); state().drawerVisible = false;
    pending.reject(new Error('lost')); await action; vi.mocked(api.getImportExportTask).mockResolvedValueOnce({ ...importTask, version: 4 }); await state().openDetail(importTask.id);
    expect(state().selectedTask).toBeUndefined(); expect((wrapper.vm as unknown as { unconfirmedTasks: Map<string, unknown> }).unconfirmedTasks.has(importTask.id)).toBe(true);
    expect(state().actionReceipt?.outcome).toBe('unconfirmed');
  });
  it('写响应不能把同ID任务换成其他工作表', async () => {
    await setup(); await state().openDetail(importTask.id); vi.mocked(api.executeImportExportTask).mockResolvedValueOnce({ ...importTask, worksheetKey: 'another', version: 2 }); await state().runAction('execute');
    expect(state().selectedTask?.worksheetKey).toBe(importTask.worksheetKey); expect(state().actionReceipt?.outcome).toBe('unconfirmed'); expect(ElMessage.success).not.toHaveBeenCalled();
  });
  it('列表分页位于同一卡片测量容器底部', async () => {
    await setup(); expect(wrapper.get('.art-crud-table-main > .art-table-pagination').exists()).toBe(true);
  });
});
