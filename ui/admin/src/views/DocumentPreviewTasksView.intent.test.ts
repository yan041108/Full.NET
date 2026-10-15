import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElPagination } from 'element-plus';
import View from './DocumentPreviewTasksView.vue';
import * as api from '../api/document-preview-tasks';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred, outputId } from '../test/data-output-fixtures';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';

vi.mock('../api/document-preview-tasks', () => ({ getDocumentPreviewTask: vi.fn(), listDocumentPreviewTasks: vi.fn(), createDocumentPreviewTask: vi.fn(), cancelDocumentPreviewTask: vi.fn(), openDocumentPreviewTaskContent: vi.fn() }));
const read = 'document.host_preview_tasks.read', create = 'document.host_preview_tasks.create', cancel = 'document.host_preview_tasks.cancel';
const other = '01980000-0000-7000-8000-000000000003';
let wrapper: VueWrapper | undefined;
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [{ ...task }], page: 1, pageSize: 20, total: 100 });
  vi.mocked(api.getDocumentPreviewTask).mockResolvedValue({ ...task });
  vi.mocked(api.createDocumentPreviewTask).mockResolvedValue({ ...task });
  vi.mocked(api.cancelDocumentPreviewTask).mockResolvedValue({ ...task, statusKey: 'cancelled' });
  vi.mocked(api.openDocumentPreviewTaskContent).mockResolvedValue();
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
async function setup() {
  const context = createShareSession([read, create, cancel]);
  const route = reactive({ name: 'document-preview-tasks', query: {} as { taskId?: string } });
  wrapper = mount(View, { global: { plugins: [context.pinia], provide: { [routeLocationKey as symbol]: route }, directives: { loading: () => {} }, stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' },
    ElDrawer: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' }
  } } }); await flushPromises(); return { ...context, route };
}
const vm = () => wrapper!.vm as unknown as { load(): Promise<void>; applyFilter(): Promise<void>; filterDocumentItemId: string; page: number; editorForm: { documentItemId: string; versionId: string }; openCreate(): void; submitCreate(): Promise<void>; requestCancellation(row: typeof task): void; openDetail(id: string): Promise<void>; openPdf(row: typeof task): Promise<void>; changing: boolean };
const click = (id: string) => wrapper!.get(`[data-testid="${id}"]`).trigger('click');
const queued = () => vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [{ ...task, statusKey: 'pending' }], page: 1, pageSize: 20, total: 100 });

describe('文档预览筛选与任务快照意图', () => {
  it('翻页不会应用尚未提交的文档筛选草稿', async () => {
    await setup(); vm().filterDocumentItemId = outputId; await vm().applyFilter(); vm().filterDocumentItemId = other;
    vm().page = 2; await vm().load(); expect(vi.mocked(api.listDocumentPreviewTasks).mock.calls.at(-1)?.slice(0, 3)).toEqual([2, 20, outputId]);
  });
  it('应用条件修剪输入并从第一页查询，清空应用后恢复全部', async () => {
    await setup(); vm().page = 3; vm().filterDocumentItemId = ' ' + outputId + ' '; await vm().applyFilter();
    expect(vi.mocked(api.listDocumentPreviewTasks).mock.calls.at(-1)?.slice(0, 3)).toEqual([1, 20, outputId]);
    vm().filterDocumentItemId = ''; await vm().applyFilter(); expect(vi.mocked(api.listDocumentPreviewTasks).mock.calls.at(-1)?.[2]).toBeUndefined();
  });
  it('Enter使用同一应用入口而不继续触发表单默认提交', async () => {
    await setup(); const input = wrapper!.get('[data-testid="document-preview-task-filter"]'); await input.setValue(outputId); await input.trigger('keydown.enter'); await flushPromises();
    expect(api.listDocumentPreviewTasks).toHaveBeenCalledTimes(2); expect(vi.mocked(api.listDocumentPreviewTasks).mock.calls.at(-1)?.[2]).toBe(outputId);
  });
  it('创建预填仅使用已应用文档条件', async () => {
    await setup(); vm().filterDocumentItemId = outputId; await vm().applyFilter(); vm().filterDocumentItemId = other; vm().openCreate(); await flushPromises(); expect(vm().editorForm.documentItemId).toBe(outputId);
  });
  it('会话变更清空草稿和已应用条件', async () => {
    const { session } = await setup(); vm().filterDocumentItemId = outputId; await vm().applyFilter(); session.currentUser!.sessionId = 'next'; await flushPromises();
    expect(vm().filterDocumentItemId).toBe(''); expect(vi.mocked(api.listDocumentPreviewTasks).mock.calls.at(-1)?.[2]).toBeUndefined();
  });
  it.each(['revision', 'file', 'status', 'missing'] as const)('PDF在途目标%s变化同步取消，旧完成不妨碍重新创建', async change => {
    await setup(); const old = deferred<void>(); vi.mocked(api.openDocumentPreviewTaskContent).mockReturnValueOnce(old.promise);
    const first = vm().openPdf(task); const signal = vi.mocked(api.openDocumentPreviewTaskContent).mock.calls[0]?.[1];
    const changed = change === 'revision' ? { ...task, version: 2 } : change === 'file' ? { ...task, outputFileId: other } : { ...task, statusKey: 'failed' };
    vi.mocked(api.listDocumentPreviewTasks).mockResolvedValueOnce({ items: change === 'missing' ? [] : [changed], page: 1, pageSize: 20, total: 1 }); await vm().load(); expect(signal?.aborted).toBe(true);
    vm().openCreate(); await flushPromises(); expect(wrapper!.find('[data-testid="document-preview-task-editor-submit"]').exists()).toBe(true);
    old.resolve(); await first; expect(vm().changing).toBe(false); expect(api.openDocumentPreviewTaskContent).toHaveBeenCalledOnce();
  });
  it('应用新查询立即取消旧PDF，不等待新目录返回', async () => {
    await setup(); const old = deferred<void>(); vi.mocked(api.openDocumentPreviewTaskContent).mockReturnValueOnce(old.promise); const opening = vm().openPdf(task);
    const next = deferred<Awaited<ReturnType<typeof api.listDocumentPreviewTasks>>>(); vi.mocked(api.listDocumentPreviewTasks).mockReturnValueOnce(next.promise);
    vm().filterDocumentItemId = other; const query = vm().applyFilter(); expect(vi.mocked(api.openDocumentPreviewTaskContent).mock.calls[0]?.[1]?.aborted).toBe(true);
    old.resolve(); next.resolve({ items: [], page: 1, pageSize: 20, total: 0 }); await Promise.all([opening, query]);
  });
  it('同任务UUID大小写不影响PDF读取身份', async () => {
    await setup(); await vm().openPdf({ ...task, id: task.id.toUpperCase() }); expect(api.openDocumentPreviewTaskContent).toHaveBeenCalledExactlyOnceWith(task.id, expect.any(AbortSignal));
  });
  it('同任务旧结果文件或旧状态不能读取PDF', async () => {
    await setup(); await vm().openPdf({ ...task, outputFileId: other }); await vm().openPdf({ ...task, statusKey: 'failed' }); expect(api.openDocumentPreviewTaskContent).not.toHaveBeenCalled();
  });
  it('详情读取期间不能用旧详情启动PDF或取消', async () => {
    await setup(); await vm().openDetail(task.id); const pending = deferred<typeof task>(); vi.mocked(api.getDocumentPreviewTask).mockReturnValueOnce(pending.promise);
    const next = vm().openDetail(task.id); await vm().openPdf(task); vm().requestCancellation({ ...task, statusKey: 'pending' });
    expect(api.openDocumentPreviewTaskContent).not.toHaveBeenCalled(); expect(wrapper!.find('[data-testid="document-preview-cancel-confirm"]').exists()).toBe(false); pending.resolve(task); await next;
  });
  it('重复请求取消确认不能替换原确认', async () => {
    queued(); await setup(); vm().requestCancellation({ ...task, statusKey: 'pending' }); await flushPromises();
    const confirm = wrapper!.findAllComponents(ElButton).find(item => item.attributes('data-testid') === 'document-preview-cancel-confirm')!.vm.$.vnode.props!.onClick;
    vm().requestCancellation({ ...task, statusKey: 'pending' }); await flushPromises();
    expect(wrapper!.findAllComponents(ElButton).find(item => item.attributes('data-testid') === 'document-preview-cancel-confirm')!.vm.$.vnode.props!.onClick).toBe(confirm);
  });
  it('旧确认与关闭回调不能批准或关闭下一次取消', async () => {
    queued(); await setup(); await click('document-preview-task-cancel');
    const accept = wrapper!.findAllComponents(ElButton).find(item => item.attributes('data-testid') === 'document-preview-cancel-confirm')!.vm.$.vnode.props!.onClick as () => void;
    const dismiss = wrapper!.findAllComponents(ElButton).find(item => item.attributes('data-testid') === 'document-preview-cancel-dismiss')!.vm.$.vnode.props!.onClick as () => void;
    await click('document-preview-cancel-dismiss'); await click('document-preview-task-cancel'); accept(); dismiss(); await flushPromises();
    expect(api.cancelDocumentPreviewTask).not.toHaveBeenCalled(); expect(wrapper!.find('[data-testid="document-preview-cancel-confirm"]').exists()).toBe(true);
    await click('document-preview-cancel-confirm'); await flushPromises(); expect(api.cancelDocumentPreviewTask).toHaveBeenCalledExactlyOnceWith(task.id, expect.any(AbortSignal));
  });
  it('大小写等价取消按目录中的权威标识发送', async () => {
    queued(); await setup(); vm().requestCancellation({ ...task, statusKey: 'pending', id: task.id.toUpperCase() }); await flushPromises(); await click('document-preview-cancel-confirm'); await flushPromises();
    expect(api.cancelDocumentPreviewTask).toHaveBeenCalledExactlyOnceWith(task.id, expect.any(AbortSignal));
  });
  it('路由离开清空目录并取消在途读取，返回重新读取', async () => {
    const old = deferred<Awaited<ReturnType<typeof api.listDocumentPreviewTasks>>>(); vi.mocked(api.listDocumentPreviewTasks).mockReturnValueOnce(old.promise);
    const { route } = await setup(); const signal = vi.mocked(api.listDocumentPreviewTasks).mock.calls[0]?.[3]; route.name = 'elsewhere'; await flushPromises(); expect(signal?.aborted).toBe(true);
    old.resolve({ items: [{ ...task, documentTitle: '迟到旧目录' }], page: 1, pageSize: 20, total: 1 }); await flushPromises(); route.name = 'document-preview-tasks'; await flushPromises();
    expect(api.listDocumentPreviewTasks).toHaveBeenCalledTimes(2); expect(wrapper!.text()).not.toContain('迟到旧目录'); expect(wrapper!.text()).toContain(task.documentTitle);
  });
  it('页大小变化只走一次change查询入口', async () => {
    await setup(); const pager = wrapper!.getComponent(ElPagination); pager.vm.$emit('size-change', 50); pager.vm.$emit('change', 1, 50); await flushPromises(); expect(api.listDocumentPreviewTasks).toHaveBeenCalledTimes(2);
  });
});
