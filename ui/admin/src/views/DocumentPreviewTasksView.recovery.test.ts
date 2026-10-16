import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElDrawer, ElMessage, ElPagination } from 'element-plus';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import View from './DocumentPreviewTasksView.vue';
import * as api from '../api/document-preview-tasks';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
vi.mock('../api/document-preview-tasks', () => ({ getDocumentPreviewTask: vi.fn(), listDocumentPreviewTasks: vi.fn(), createDocumentPreviewTask: vi.fn(), cancelDocumentPreviewTask: vi.fn(), openDocumentPreviewTaskContent: vi.fn() }));
const read = 'document.host_preview_tasks.read', create = 'document.host_preview_tasks.create', cancel = 'document.host_preview_tasks.cancel';
const otherId = '019bc2b1-2a40-7cc3-8992-a80de51bf298', pendingTask = { ...task, statusKey: 'pending', outputFileId: null };
let wrapper: VueWrapper;
interface State { items: typeof task[]; page: number; pageSize: number; selectedTask?: typeof task; detailVisible: boolean; editorOpen: boolean; editorForm: { documentItemId: string; versionId: string }; filterDocumentItemId: string; appliedDocumentItemId: string; receipt?: { taskId?: string; outcome: string; kind: string }; load: () => Promise<boolean>; applyFilter: () => Promise<void>; openCreate: () => void; submitCreate: () => Promise<void>; openDetail: (id: string) => Promise<void>; refreshDetail: (id: string) => Promise<void>; requestCancellation: (row: typeof task) => void; confirmCancellation: () => Promise<void>; acknowledgeCreate: () => void }
const state = () => wrapper.vm as unknown as State;
const button = (id: string) => wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === id)!;
beforeEach(() => {
  vi.resetAllMocks(); vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
  vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [{ ...pendingTask }], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.getDocumentPreviewTask).mockResolvedValue({ ...pendingTask }); vi.mocked(api.createDocumentPreviewTask).mockResolvedValue({ ...pendingTask }); vi.mocked(api.cancelDocumentPreviewTask).mockResolvedValue({ ...pendingTask, statusKey: 'cancelled', version: 2 });
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup() {
  const context = createShareSession([read, create, cancel]), route = reactive({ name: 'document-preview-tasks', query: {} });
  wrapper = mount(View, { global: { plugins: [context.pinia], provide: { [routeLocationKey as symbol]: route }, directives: { loading: () => {} }, stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }, ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' }
  } } }); await flushPromises(); return { ...context, route };
}
function edit(documentItemId = task.documentItemId, versionId = '') { state().openCreate(); state().editorForm.documentItemId = documentItemId; state().editorForm.versionId = versionId; }
describe('文档预览任务结果与恢复', () => {
  it('同查询补读失败保留已确认行，新查询在请求前清旧行', async () => {
    await setup(); vi.mocked(api.listDocumentPreviewTasks).mockRejectedValueOnce(new Error('offline')); await state().load(); expect(state().items).toHaveLength(1);
    state().filterDocumentItemId = otherId; const pending = deferred<Awaited<ReturnType<typeof api.listDocumentPreviewTasks>>>(); vi.mocked(api.listDocumentPreviewTasks).mockReturnValueOnce(pending.promise); const reading = state().applyFilter(); expect(state().items).toEqual([]); pending.reject(new Error('offline')); await reading;
  });
  it('错误页回包不能改写页码或恢复旧行', async () => {
    await setup(); state().page = 2; await state().load(); expect(state().page).toBe(2); expect(state().items).toEqual([]);
  });
  it('页大小改变回第一页且重复受控事件不重复读取', async () => {
    await setup(); const pager = wrapper.getComponent(ElPagination); pager.vm.$emit('change', 1, 20); await flushPromises(); expect(api.listDocumentPreviewTasks).toHaveBeenCalledOnce();
    vi.mocked(api.listDocumentPreviewTasks).mockResolvedValueOnce({ items: [], page: 1, pageSize: 50, total: 0 }); pager.vm.$emit('update:pageSize', 50); pager.vm.$emit('update:currentPage', 3); pager.vm.$emit('change', 3, 50); await flushPromises(); expect(api.listDocumentPreviewTasks).toHaveBeenLastCalledWith(1, 50, undefined, expect.any(AbortSignal));
  });
  it('空越界页最多补读一次末页', async () => {
    await setup(); state().page = 3; vi.mocked(api.listDocumentPreviewTasks).mockResolvedValueOnce({ items: [], page: 3, pageSize: 20, total: 21 }).mockResolvedValueOnce({ items: [], page: 2, pageSize: 20, total: 0 }); await state().load(); expect(api.listDocumentPreviewTasks).toHaveBeenCalledTimes(3); expect(state().page).toBe(2);
  });
  it('非法筛选不覆盖已应用条件或调用后端', async () => {
    await setup(); state().filterDocumentItemId = 'bad-id'; await state().applyFilter(); expect(state().appliedDocumentItemId).toBe(''); expect(api.listDocumentPreviewTasks).toHaveBeenCalledOnce();
  });
  it('详情首次失败仍保留抽屉和重试入口', async () => {
    await setup(); vi.mocked(api.getDocumentPreviewTask).mockRejectedValueOnce(new Error('offline')); await state().openDetail(task.id); expect(state().detailVisible).toBe(true);
    await wrapper.get('[data-testid="document-preview-detail-refresh"]').trigger('click'); await flushPromises(); expect(state().selectedTask?.id).toBe(task.id);
  });
  it('详情补读失败保留内容与独立错误，目录刷新不吞掉错误', async () => {
    await setup(); await state().openDetail(task.id); vi.mocked(api.getDocumentPreviewTask).mockRejectedValueOnce(new Error('offline')); await state().refreshDetail(task.id); await state().load(); await flushPromises();
    expect(state().selectedTask?.id).toBe(task.id); expect(wrapper.find('[data-testid="document-preview-detail-problem"]').exists()).toBe(true);
  });
  it('旧抽屉关闭回调不能关闭下一任务', async () => {
    await setup(); await state().openDetail(task.id); const close = wrapper.getComponent(ElDrawer).vm.$.vnode.props!['onUpdate:modelValue'] as (open: boolean) => void;
    vi.mocked(api.getDocumentPreviewTask).mockResolvedValueOnce({ ...pendingTask, id: otherId }); await state().openDetail(otherId); close(false); expect(state().detailVisible).toBe(true);
  });
  it('创建成功先保留任务事实，目录和详情补读失败仍显示创建回执', async () => {
    await setup(); edit(); vi.mocked(api.listDocumentPreviewTasks).mockRejectedValueOnce(new Error('offline')); vi.mocked(api.getDocumentPreviewTask).mockRejectedValueOnce(new Error('offline')); await state().submitCreate();
    expect(state().editorOpen).toBe(false); expect(state().selectedTask?.id).toBe(task.id); expect(state().receipt).toMatchObject({ kind: 'create', taskId: task.id, outcome: 'succeeded' }); expect(ElMessage.success).toHaveBeenCalledOnce();
  });
  it.each(['bad-id', '00000000-0000-0000-0000-000000000000'])('非法创建文档 %s 在请求前拒绝', async id => {
    await setup(); edit(id); await state().submitCreate(); expect(api.createDocumentPreviewTask).not.toHaveBeenCalled();
  });
  it('非法历史版本在请求前拒绝，不回退当前版本', async () => {
    await setup(); edit(task.documentItemId, 'bad-id'); await state().submitCreate(); expect(api.createDocumentPreviewTask).not.toHaveBeenCalled();
  });
  it('旧创建确认和关闭回调不能操作新窗口', async () => {
    await setup(); edit(); await flushPromises(); const dialog = wrapper.getComponent(ArtFormDialog), submit = dialog.vm.$.vnode.props!.onConfirm as () => void, close = dialog.vm.$.vnode.props!['onUpdate:open'] as (open: boolean) => void;
    state().editorOpen = false; edit(); submit(); close(false); await flushPromises(); expect(api.createDocumentPreviewTask).not.toHaveBeenCalled(); expect(state().editorOpen).toBe(true);
  });
  it('创建直接更换目标取消旧请求，迟到结果不交付', async () => {
    await setup(); edit(); const pending = deferred<typeof task>(); vi.mocked(api.createDocumentPreviewTask).mockReturnValueOnce(pending.promise); const creating = state().submitCreate(); state().editorForm.documentItemId = otherId;
    expect(vi.mocked(api.createDocumentPreviewTask).mock.calls[0]![1]!.aborted).toBe(true); pending.resolve(pendingTask); await creating; expect(ElMessage.success).not.toHaveBeenCalled(); expect(state().receipt?.outcome).toBe('unconfirmed');
  });
  it('未知创建同一目标跨重开保留门槛，其他目标不解除', async () => {
    await setup(); edit(); vi.mocked(api.createDocumentPreviewTask).mockRejectedValueOnce(new Error('lost')); await state().submitCreate(); state().editorOpen = false; edit(otherId); state().editorForm.documentItemId = task.documentItemId; await state().submitCreate(); expect(api.createDocumentPreviewTask).toHaveBeenCalledOnce();
  });
  it('核对确认只允许后续人工创建，不自动创建', async () => {
    await setup(); edit(); vi.mocked(api.createDocumentPreviewTask).mockRejectedValueOnce(new Error('lost')); await state().submitCreate(); state().acknowledgeCreate(); await flushPromises();
    await wrapper.get('[data-testid="document-preview-recovery-confirm"]').trigger('click'); await flushPromises(); expect(api.createDocumentPreviewTask).toHaveBeenCalledOnce(); await state().submitCreate(); expect(api.createDocumentPreviewTask).toHaveBeenCalledTimes(2);
  });
  it('旧恢复确认不能解除下一次未知创建门槛', async () => {
    await setup(); edit(); vi.mocked(api.createDocumentPreviewTask).mockRejectedValue(new Error('lost')); await state().submitCreate(); state().acknowledgeCreate(); await flushPromises();
    const dialog = wrapper.findAllComponents(ArtFormDialog).find(item => item.props('confirmTestId') === 'document-preview-recovery-confirm')!, accept = dialog.vm.$.vnode.props!.onConfirm as () => void;
    accept(); await state().submitCreate(); state().acknowledgeCreate(); await flushPromises(); accept(); await state().submitCreate(); expect(api.createDocumentPreviewTask).toHaveBeenCalledTimes(2);
  });
  it.each([0, 302, 408, 503])('创建状态%s为未知结果，不可直接重发', async status => {
    await setup(); edit(); vi.mocked(api.createDocumentPreviewTask).mockRejectedValueOnce({ status, code: 'gateway.failure' }); await state().submitCreate(); await state().submitCreate(); expect(api.createDocumentPreviewTask).toHaveBeenCalledOnce(); expect(state().receipt?.outcome).toBe('unconfirmed');
  });
  it('明确409创建拒绝可人工重试', async () => {
    await setup(); edit(); vi.mocked(api.createDocumentPreviewTask).mockRejectedValueOnce({ status: 409, code: 'document.version_conflict' }); await state().submitCreate(); expect(state().receipt?.outcome).toBe('failed'); await state().submitCreate(); expect(api.createDocumentPreviewTask).toHaveBeenCalledTimes(2);
  });
  it('取消确认成功后补读失败不丢终态与回执', async () => {
    await setup(); state().requestCancellation(pendingTask); vi.mocked(api.listDocumentPreviewTasks).mockRejectedValueOnce(new Error('offline')); await state().confirmCancellation(); expect(state().items[0]?.statusKey).toBe('cancelled'); expect(state().receipt).toMatchObject({ kind: 'cancel', taskId: task.id, outcome: 'succeeded' });
  });
  it('早发列表不能恢复已取消状态，更低版本补读也不回退', async () => {
    await setup(); state().requestCancellation(pendingTask); const mutation = deferred<typeof task>(); vi.mocked(api.cancelDocumentPreviewTask).mockReturnValueOnce(mutation.promise); const cancelling = state().confirmCancellation();
    const old = deferred<Awaited<ReturnType<typeof api.listDocumentPreviewTasks>>>(); vi.mocked(api.listDocumentPreviewTasks).mockReturnValueOnce(old.promise); const reading = state().load(); mutation.resolve({ ...pendingTask, statusKey: 'cancelled', version: 2 }); await cancelling; old.resolve({ items: [pendingTask], page: 1, pageSize: 20, total: 1 }); await reading; expect(state().items[0]?.statusKey).toBe('cancelled');
  });
  it('取消未知门槛不因列表读取解除，详情核对后才可人工再取消', async () => {
    await setup(); state().requestCancellation(pendingTask); vi.mocked(api.cancelDocumentPreviewTask).mockRejectedValueOnce(new Error('lost')); await state().confirmCancellation(); await state().load(); state().requestCancellation(pendingTask); await state().confirmCancellation(); expect(api.cancelDocumentPreviewTask).toHaveBeenCalledOnce();
    await state().openDetail(task.id); state().requestCancellation(pendingTask); await state().confirmCancellation(); expect(api.cancelDocumentPreviewTask).toHaveBeenCalledTimes(2);
  });
  it('取消响应不能改变来源文档，错配结果保留未知', async () => {
    await setup(); state().requestCancellation(pendingTask); vi.mocked(api.cancelDocumentPreviewTask).mockResolvedValueOnce({ ...pendingTask, statusKey: 'cancelled', documentItemId: otherId }); await state().confirmCancellation(); expect(state().items[0]?.statusKey).toBe('pending'); expect(state().receipt?.outcome).toBe('unconfirmed');
  });
  it('详情关闭保留取消未知基线，重开旧版本不能解除门槛', async () => {
    const current = { ...pendingTask, version: 5 }; vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [current], page: 1, pageSize: 20, total: 1 }); vi.mocked(api.getDocumentPreviewTask).mockResolvedValue(current);
    await setup(); await state().openDetail(task.id); state().requestCancellation(current); const pending = deferred<typeof task>(); vi.mocked(api.cancelDocumentPreviewTask).mockReturnValueOnce(pending.promise); const cancelling = state().confirmCancellation(); state().detailVisible = false;
    pending.reject(new Error('lost')); await cancelling; vi.mocked(api.getDocumentPreviewTask).mockResolvedValueOnce({ ...pendingTask, version: 4 }); await state().openDetail(task.id);
    expect(state().selectedTask).toBeUndefined(); expect((wrapper.vm as unknown as { cancellationBlocks: Map<string, unknown> }).cancellationBlocks.has(task.id)).toBe(true);
  });
  it('旧列表行回调不能操作新查询中的任务', async () => {
    await setup(); const row = (wrapper.vm as unknown as { tableRows: { cancel: () => void }[] }).tableRows[0]!; state().page = 2; vi.mocked(api.listDocumentPreviewTasks).mockResolvedValueOnce({ items: [pendingTask], page: 2, pageSize: 20, total: 21 }); await state().load(); row.cancel(); await flushPromises(); expect(wrapper.find('[data-testid="document-preview-cancel-confirm"]').exists()).toBe(false);
  });
  it('筛选和表头位于测量容器外，分页为同容器底部直接子项', async () => {
    await setup(); expect(wrapper.find('.art-crud-table-main > .art-table-pagination').exists()).toBe(true); expect(wrapper.find('.art-crud-table-main .document-module-inline-query').exists()).toBe(false);
  });
});
