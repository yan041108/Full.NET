import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElDrawer, ElMessage } from 'element-plus';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router, type LocationQueryRaw } from 'vue-router';
import View from './DocumentPreviewTasksView.vue';
import * as api from '../api/document-preview-tasks';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred, outputId } from '../test/data-output-fixtures';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
vi.mock('../api/document-preview-tasks', () => ({ getDocumentPreviewTask: vi.fn(), listDocumentPreviewTasks: vi.fn(),
  createDocumentPreviewTask: vi.fn(), cancelDocumentPreviewTask: vi.fn(), openDocumentPreviewTaskContent: vi.fn() }));
const read = 'document.host_preview_tasks.read', create = 'document.host_preview_tasks.create', cancel = 'document.host_preview_tasks.cancel';
const otherId = '01980000-0000-7000-8000-000000000003';
let wrapper: VueWrapper | undefined; let router: Router; let visible = ref(true);
beforeEach(() => {
  vi.resetAllMocks(); vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
  vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  vi.mocked(api.getDocumentPreviewTask).mockImplementation(async id => ({ ...task, id, documentTitle: id === otherId ? '新预览' : '链接预览' }));
  vi.mocked(api.createDocumentPreviewTask).mockResolvedValue(task); vi.mocked(api.openDocumentPreviewTaskContent).mockResolvedValue();
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; ElMessage.closeAll(); vi.restoreAllMocks(); vi.useRealTimers(); });
async function setup(query: LocationQueryRaw = { taskId: outputId }, permissions = [read], keepAlive = false, tenant = false) {
  const context = createShareSession(permissions); visible = ref(true);
  if (tenant) Object.assign(context.session.currentUser!, { tenantId: outputId, scope: `tenant:${outputId.replaceAll('-', '')}` });
  router = createRouter({ history: createMemoryHistory(), routes: [
    { name: 'document-preview-tasks', path: '/preview', component: { render: () => null } },
    { name: 'elsewhere', path: '/elsewhere', component: { render: () => null } }
  ] }); await router.push({ name: 'document-preview-tasks', query });
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => visible.value ? h(View) : h(blank) }) });
  wrapper = mount(keepAlive ? root : View, { global: { plugins: [context.pinia, router], directives: { loading: {} }, stubs: {
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' },
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); await flushPromises(); return context.session;
}
function vm() { return wrapper!.findComponent(View).vm as unknown as {
  editorOpen: boolean; changing: boolean; detailVisible: boolean; editorForm: { documentItemId: string; versionId: string };
  submitCreate: () => Promise<void>; load: () => Promise<void>; refreshResults: () => Promise<void>;
  openPdf: (row: typeof task) => Promise<void>; requestCancellation: (row: typeof task) => void;
}; }
function closeDetail() { wrapper!.getComponent(ElDrawer).vm.$emit('update:modelValue', false); }

describe('文档预览详情及完整任务流程', () => {
  it('读取列表页外任务，不自动打开PDF或取消', async () => {
    await setup(); expect(api.getDocumentPreviewTask).toHaveBeenCalledExactlyOnceWith(outputId, expect.any(AbortSignal));
    expect(wrapper!.text()).toContain('链接预览'); expect(api.openDocumentPreviewTaskContent).not.toHaveBeenCalled(); expect(api.cancelDocumentPreviewTask).not.toHaveBeenCalled();
  });
  it.each(['bad', [outputId], '00000000-0000-0000-0000-000000000000', ''])('非法 taskId %j 不读取详情', async taskId => {
    await setup({ taskId }); expect(api.getDocumentPreviewTask).not.toHaveBeenCalled();
  });
  it.each(['missing-read', 'tenant'] as const)('%s 不能读取详情或进入创建', async kind => {
    await setup(undefined, kind === 'missing-read' ? [create, cancel] : [read, create, cancel], false, kind === 'tenant');
    expect(api.listDocumentPreviewTasks).not.toHaveBeenCalled(); expect(api.getDocumentPreviewTask).not.toHaveBeenCalled();
    expect(wrapper!.find('[data-testid="document-preview-task-create"]').exists()).toBe(false);
  });
  it('详情独立于未完成的列表立即读取', async () => {
    const pending = deferred<Awaited<ReturnType<typeof api.listDocumentPreviewTasks>>>(); vi.mocked(api.listDocumentPreviewTasks).mockReturnValueOnce(pending.promise);
    await setup(); expect(wrapper!.text()).toContain('链接预览'); pending.resolve({ items: [], page: 1, pageSize: 20, total: 0 }); await flushPromises();
  });
  it('切链接取消旧详情，迟到内容不能覆盖新任务', async () => {
    const pending = deferred<typeof task>(); vi.mocked(api.getDocumentPreviewTask).mockReturnValueOnce(pending.promise); await setup();
    const signal = vi.mocked(api.getDocumentPreviewTask).mock.calls[0]![1]!;
    await router.replace({ query: { taskId: otherId } }); await flushPromises(); pending.resolve({ ...task, documentTitle: '旧详情' }); await flushPromises();
    expect(signal.aborted).toBe(true); expect(wrapper!.text()).toContain('新预览'); expect(wrapper!.text()).not.toContain('旧详情');
  });
  it('关闭待读取详情取消请求，迟到结果不能重开', async () => {
    const pending = deferred<typeof task>(); vi.mocked(api.getDocumentPreviewTask).mockReturnValueOnce(pending.promise); await setup();
    closeDetail(); expect(vi.mocked(api.getDocumentPreviewTask).mock.calls[0]![1]!.aborted).toBe(true);
    pending.resolve(task); await flushPromises(); expect(vm().detailVisible).toBe(false); expect(wrapper!.text()).not.toContain('敏感文档');
  });
  it('详情读取失败保留重试入口，显式关闭后刷新不重放链接', async () => {
    vi.mocked(api.getDocumentPreviewTask).mockRejectedValueOnce({ status: 404, code: 'document.preview_task.not_found', title: '任务不存在' });
    await setup(); expect(vm().detailVisible).toBe(true); expect(wrapper!.text()).toContain('任务不存在');
    await vm().refreshResults(); await flushPromises(); expect(api.getDocumentPreviewTask).toHaveBeenCalledTimes(2);
    closeDetail(); await vm().refreshResults(); await flushPromises(); expect(api.getDocumentPreviewTask).toHaveBeenCalledTimes(2);
  });
  it('详情响应身份错配不展示其他任务', async () => {
    vi.mocked(api.getDocumentPreviewTask).mockResolvedValueOnce({ ...task, id: otherId, documentTitle: '错误目标' });
    await setup(); expect(vm().detailVisible).toBe(true); expect(wrapper!.text()).not.toContain('错误目标');
  });
  it('关闭详情取消PDF读取，旧错误不污染重开的任务', async () => {
    const pending = deferred<void>(); vi.mocked(api.openDocumentPreviewTaskContent).mockReturnValueOnce(pending.promise); await setup();
    await wrapper!.get('[data-testid="document-preview-detail-open-pdf"]').trigger('click');
    const signal = vi.mocked(api.openDocumentPreviewTaskContent).mock.calls[0]![1]!; closeDetail();
    await router.replace({ query: { taskId: otherId } }); await flushPromises(); pending.reject({ status: 404, code: 'files.file.not_found', title: '旧文件' }); await flushPromises();
    expect(signal.aborted).toBe(true); expect(wrapper!.text()).toContain('新预览'); expect(wrapper!.text()).not.toContain('旧文件'); expect(vm().changing).toBe(false);
  });
  it('明确文件缺失禁用详情PDF，用户刷新可重新确认', async () => {
    vi.mocked(api.openDocumentPreviewTaskContent).mockRejectedValueOnce({ status: 404, code: 'files.file.not_found', title: 'missing' }); await setup();
    await wrapper!.get('[data-testid="document-preview-detail-open-pdf"]').trigger('click'); await flushPromises();
    expect(wrapper!.get('[data-testid="document-preview-detail-open-pdf"]').attributes('disabled')).toBeDefined();
    await vm().refreshResults(); await flushPromises(); expect(wrapper!.get('[data-testid="document-preview-detail-open-pdf"]').attributes('disabled')).toBeUndefined();
  });
  it('详情任务不在列表时，仍可按独立取消权限确认取消', async () => {
    vi.mocked(api.getDocumentPreviewTask).mockResolvedValue({ ...task, statusKey: 'pending' }); vi.mocked(api.cancelDocumentPreviewTask).mockResolvedValue({ ...task, statusKey: 'cancelled' });
    await setup(undefined, [read, cancel]); await wrapper!.get('[data-testid="document-preview-detail-cancel"]').trigger('click');
    expect(api.cancelDocumentPreviewTask).not.toHaveBeenCalled(); await wrapper!.get('[data-testid="document-preview-cancel-confirm"]').trigger('click'); await flushPromises();
    expect(api.cancelDocumentPreviewTask).toHaveBeenCalledExactlyOnceWith(task.id, expect.any(AbortSignal));
  });
  it('关闭详情取消在途取消请求，不接入迟到成功', async () => {
    const pending = deferred<typeof task>(); vi.mocked(api.getDocumentPreviewTask).mockResolvedValue({ ...task, statusKey: 'pending' });
    vi.mocked(api.cancelDocumentPreviewTask).mockReturnValueOnce(pending.promise); const success = vi.spyOn(ElMessage, 'success'); await setup(undefined, [read, cancel]);
    await wrapper!.get('[data-testid="document-preview-detail-cancel"]').trigger('click'); await wrapper!.get('[data-testid="document-preview-cancel-confirm"]').trigger('click');
    closeDetail(); expect(vi.mocked(api.cancelDocumentPreviewTask).mock.calls[0]![1]!.aborted).toBe(true); pending.resolve({ ...task, statusKey: 'cancelled' }); await flushPromises();
    expect(success).not.toHaveBeenCalled(); expect(vm().changing).toBe(false);
  });
  it('在途详情与列表共用轮询，终态停止读取', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    vi.mocked(api.getDocumentPreviewTask).mockResolvedValueOnce({ ...task, statusKey: 'processing' }).mockResolvedValue(task);
    await setup(); await vi.advanceTimersByTimeAsync(5000); await flushPromises(); expect(api.getDocumentPreviewTask).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(10000); await flushPromises(); expect(api.getDocumentPreviewTask).toHaveBeenCalledTimes(2);
  });
  it('详情关闭后停止仅由该详情触发的轮询', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }); vi.mocked(api.getDocumentPreviewTask).mockResolvedValue({ ...task, statusKey: 'processing' });
    await setup(); closeDetail(); await vi.advanceTimersByTimeAsync(15000); expect(api.getDocumentPreviewTask).toHaveBeenCalledOnce();
  });
  it('撤权清除详情，恢复权限不重放原链接，显式新链接可以打开', async () => {
    const session = await setup(); session.currentUser!.permissions = []; await flushPromises(); expect(vm().detailVisible).toBe(false);
    session.currentUser!.permissions = [read]; await flushPromises(); expect(api.getDocumentPreviewTask).toHaveBeenCalledOnce();
    await router.replace({ query: { taskId: otherId } }); await flushPromises(); expect(wrapper!.text()).toContain('新预览');
  });
  it('KeepAlive 恢复不重放旧链接，停用期间的新链接在恢复后读取', async () => {
    await setup(undefined, undefined, true); visible.value = false; await flushPromises(); visible.value = true; await flushPromises();
    expect(api.getDocumentPreviewTask).toHaveBeenCalledOnce(); visible.value = false; await flushPromises();
    await router.replace({ query: { taskId: otherId } }); await flushPromises(); expect(api.getDocumentPreviewTask).toHaveBeenCalledOnce();
    visible.value = true; await flushPromises(); expect(wrapper!.text()).toContain('新预览');
  });
  it('离开任务路由取消详情，晚结果不能显示', async () => {
    const pending = deferred<typeof task>(); vi.mocked(api.getDocumentPreviewTask).mockReturnValueOnce(pending.promise); await setup();
    const signal = vi.mocked(api.getDocumentPreviewTask).mock.calls[0]![1]!; await router.push({ name: 'elsewhere' }); pending.resolve(task); await flushPromises();
    expect(signal.aborted).toBe(true); expect(vm().detailVisible).toBe(false); expect(wrapper!.text()).not.toContain('敏感文档');
  });
  it('从列表打开PDF后离开路由，也同步取消列表输出请求', async () => {
    const pending = deferred<void>(); vi.mocked(api.openDocumentPreviewTaskContent).mockReturnValueOnce(pending.promise);
    vi.mocked(api.listDocumentPreviewTasks).mockResolvedValueOnce({ items: [task], page: 1, pageSize: 20, total: 1 }); await setup({});
    const action = vm().openPdf(task); const signal = vi.mocked(api.openDocumentPreviewTaskContent).mock.calls[0]![1]!;
    await router.push({ name: 'elsewhere' }); expect(signal.aborted).toBe(true); pending.resolve(); await action; expect(vm().changing).toBe(false);
  });
  it('详情确认期间权威修订变化，旧取消意图不能写入', async () => {
    vi.mocked(api.getDocumentPreviewTask).mockResolvedValueOnce({ ...task, statusKey: 'pending' }).mockResolvedValue({ ...task, statusKey: 'pending', version: 2 });
    await setup(undefined, [read, cancel]); await wrapper!.get('[data-testid="document-preview-detail-cancel"]').trigger('click');
    await vm().refreshResults(); await wrapper!.get('[data-testid="document-preview-cancel-confirm"]').trigger('click'); await flushPromises();
    expect(api.cancelDocumentPreviewTask).not.toHaveBeenCalled();
  });
  it('取消结果身份或终态错误不能显示成功', async () => {
    vi.mocked(api.getDocumentPreviewTask).mockResolvedValue({ ...task, statusKey: 'pending' });
    vi.mocked(api.cancelDocumentPreviewTask).mockResolvedValueOnce({ ...task, id: otherId, statusKey: 'processing' });
    const success = vi.spyOn(ElMessage, 'success'); await setup(undefined, [read, cancel]);
    await wrapper!.get('[data-testid="document-preview-detail-cancel"]').trigger('click'); await wrapper!.get('[data-testid="document-preview-cancel-confirm"]').trigger('click'); await flushPromises();
    expect(success).not.toHaveBeenCalled(); expect(api.listDocumentPreviewTasks).toHaveBeenCalledOnce();
  });
  it('健康创建完成后立即打开结果详情，不依赖任务位于列表首页', async () => {
    await setup({}, [read, create]); await wrapper!.get('[data-testid="document-preview-task-create"]').trigger('click'); await flushPromises();
    vm().editorForm.documentItemId = task.documentItemId; await vm().submitCreate(); await flushPromises();
    expect(api.createDocumentPreviewTask).toHaveBeenCalledWith({ documentItemId: task.documentItemId, versionId: null }, expect.any(AbortSignal));
    expect(api.getDocumentPreviewTask).toHaveBeenCalledExactlyOnceWith(task.id, expect.any(AbortSignal)); expect(wrapper!.text()).toContain('链接预览');
  });
  it('关闭旧创建后重开，旧 finally 不释放新操作锁', async () => {
    const old = deferred<typeof task>(); const next = deferred<typeof task>(); vi.mocked(api.createDocumentPreviewTask).mockReturnValueOnce(old.promise).mockReturnValueOnce(next.promise);
    await setup({}, [read, create]); await wrapper!.get('[data-testid="document-preview-task-create"]').trigger('click'); await flushPromises();
    vm().editorForm.documentItemId = task.documentItemId; const first = vm().submitCreate(); vm().editorOpen = false;
    await wrapper!.get('[data-testid="document-preview-task-create"]').trigger('click'); await flushPromises(); vm().editorForm.documentItemId = otherId;
    const second = vm().submitCreate(); old.resolve(task); await first; expect(vm().changing).toBe(true); expect(vm().editorOpen).toBe(true);
    vm().editorOpen = false; next.resolve(task); await second; expect(api.getDocumentPreviewTask).not.toHaveBeenCalled();
  });
  it('未打开创建弹窗不能直接提交', async () => {
    await setup({}, [read, create]); vm().editorForm.documentItemId = task.documentItemId; await vm().submitCreate(); expect(api.createDocumentPreviewTask).not.toHaveBeenCalled();
  });
  it('列表详情入口可以打开当前行，未知行不能打开PDF或取消', async () => {
    vi.mocked(api.listDocumentPreviewTasks).mockResolvedValueOnce({ items: [task], page: 1, pageSize: 20, total: 1 }); await setup({});
    await wrapper!.get('[data-testid="document-preview-task-detail"]').trigger('click'); await flushPromises(); expect(wrapper!.text()).toContain('链接预览');
    const fake = { ...task, id: otherId }; await vm().openPdf(fake); vm().requestCancellation({ ...fake, statusKey: 'pending' });
    expect(api.openDocumentPreviewTaskContent).not.toHaveBeenCalled(); expect(wrapper!.find('[data-testid="document-preview-cancel-confirm"]').exists()).toBe(false);
  });
});
