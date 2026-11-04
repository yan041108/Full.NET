import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElMessage } from 'element-plus';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import DocumentPreviewTasksView from './DocumentPreviewTasksView.vue';
import * as api from '../api/document-preview-tasks';
import { deferred, outputId } from '../test/data-output-fixtures';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
import { createShareSession } from '../test/document-share-fixtures';
vi.mock('../api/document-preview-tasks', () => ({
  cancelDocumentPreviewTask: vi.fn(),
  getDocumentPreviewTask: vi.fn(),
  listDocumentPreviewTasks: vi.fn(), createDocumentPreviewTask: vi.fn(), openDocumentPreviewTaskContent: vi.fn()
}));
let wrapper: VueWrapper | undefined;
const read = 'document.host_preview_tasks.read'; const create = 'document.host_preview_tasks.create';
beforeEach(() => {
  vi.clearAllMocks();
  for (const operation of Object.values(api)) if (vi.isMockFunction(operation)) operation.mockReset();
  vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [task], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.getDocumentPreviewTask).mockResolvedValue(task);
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
const setup = async (permissions = [read, create]) => {
  const context = createShareSession(permissions); wrapper = mount(DocumentPreviewTasksView, {
    global: { plugins: [context.pinia], directives: { loading: {} }, stubs: {
      ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot /><slot name="footer" /></div>' }
    } }
  });
  await flushPromises(); return context.session;
};
describe('文档预览请求归属', () => {
  const pendingTask = { ...task, statusKey: 'pending' };
  const queue = (value = pendingTask) => vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [value], page: 1, pageSize: 20, total: 1 });
  it('创建权限不授予取消，独立取消权限无需创建权限', async () => {
    queue(); const session = await setup();
    expect(wrapper!.find('[data-testid="document-preview-task-cancel"]').exists()).toBe(false);
    session.currentUser!.permissions = [read, 'document.host_preview_tasks.cancel']; await flushPromises();
    expect(wrapper!.find('[data-testid="document-preview-task-cancel"]').exists()).toBe(true);
  });
  it.each(['processing', 'succeeded', 'failed', 'cancelled'])('状态%s不显示取消入口', async statusKey => {
    queue({ ...pendingTask, statusKey }); await setup([read, 'document.host_preview_tasks.cancel']);
    expect(wrapper!.find('[data-testid="document-preview-task-cancel"]').exists()).toBe(false);
  });
  it('其他创建人的排队任务不能取消', async () => {
    queue({ ...pendingTask, requestedByUserId: '01980000-0000-7000-8000-000000000003' });
    await setup([read, 'document.host_preview_tasks.cancel']);
    expect(wrapper!.find('[data-testid="document-preview-task-cancel"]').exists()).toBe(false);
  });
  it('放弃取消确认没有 HTTP 副作用', async () => {
    queue(); await setup([read, 'document.host_preview_tasks.cancel']);
    await wrapper!.get('[data-testid="document-preview-task-cancel"]').trigger('click'); await flushPromises();
    expect(api.cancelDocumentPreviewTask).not.toHaveBeenCalled();
    await wrapper!.get('[data-testid="document-preview-cancel-dismiss"]').trigger('click'); await flushPromises();
    expect(api.cancelDocumentPreviewTask).not.toHaveBeenCalled();
  });
  it('确认后只提交一次，成功刷新任务目录', async () => {
    queue(); const pending = deferred<typeof task>(); vi.mocked(api.cancelDocumentPreviewTask).mockReturnValue(pending.promise);
    await setup([read, 'document.host_preview_tasks.cancel']);
    await wrapper!.get('[data-testid="document-preview-task-cancel"]').trigger('click'); await flushPromises();
    await wrapper!.get('[data-testid="document-preview-cancel-confirm"]').trigger('click');
    expect(api.cancelDocumentPreviewTask).toHaveBeenCalledOnce();
    pending.resolve({ ...task, statusKey: 'cancelled' }); await flushPromises();
    expect(api.listDocumentPreviewTasks).toHaveBeenCalledTimes(2);
  });
  it('确认期间任务版本变化不能沿用旧确认', async () => {
    const current = { ...pendingTask }; queue(current);
    const warning = vi.spyOn(ElMessage, 'warning'); await setup([read, 'document.host_preview_tasks.cancel']);
    await wrapper!.get('[data-testid="document-preview-task-cancel"]').trigger('click'); await flushPromises();
    current.version = 2;
    await wrapper!.get('[data-testid="document-preview-cancel-confirm"]').trigger('click'); await flushPromises();
    expect(api.cancelDocumentPreviewTask).not.toHaveBeenCalled(); expect(warning).toHaveBeenCalledOnce();
  });
  it('确认期间撤权关闭确认，不发送取消', async () => {
    queue(); const session = await setup([read, 'document.host_preview_tasks.cancel']);
    await wrapper!.get('[data-testid="document-preview-task-cancel"]').trigger('click'); await flushPromises();
    session.currentUser!.permissions = [read]; await flushPromises();
    expect(wrapper!.find('[data-testid="document-preview-cancel-confirm"]').exists()).toBe(false);
    expect(api.cancelDocumentPreviewTask).not.toHaveBeenCalled();
  });
  it('取消在途切范围中止接入，迟到成功不通知', async () => {
    queue(); const pending = deferred<typeof task>(); vi.mocked(api.cancelDocumentPreviewTask).mockReturnValue(pending.promise);
    const success = vi.spyOn(ElMessage, 'success'); const session = await setup([read, 'document.host_preview_tasks.cancel']);
    await wrapper!.get('[data-testid="document-preview-task-cancel"]').trigger('click'); await flushPromises();
    await wrapper!.get('[data-testid="document-preview-cancel-confirm"]').trigger('click');
    session.currentUser!.tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
    expect(vi.mocked(api.cancelDocumentPreviewTask).mock.calls[0]?.[1]?.aborted).toBe(true);
    pending.resolve({ ...task, statusKey: 'cancelled' }); await flushPromises();
    expect(success).not.toHaveBeenCalled();
  });
  it('明确文件404显示不可用，不再重复打开PDF', async () => {
    vi.mocked(api.openDocumentPreviewTaskContent).mockRejectedValue({ title: 'missing', status: 404, code: 'files.file.not_found' });
    await setup([read]); await wrapper!.get('[data-testid="document-preview-task-open-pdf"]').trigger('click'); await flushPromises();
    expect(wrapper!.find('[data-testid="document-result-unavailable"]').exists()).toBe(true);
    expect(wrapper!.get('[data-testid="document-preview-task-open-pdf"]').attributes('disabled')).toBeDefined();
    wrapper!.getComponent(ArtTableHeader).vm.$emit('refresh'); await flushPromises();
    expect(wrapper!.get('[data-testid="document-preview-task-open-pdf"]').attributes('disabled')).toBeUndefined();
  });
  it('旧身份迟到的文件404不污染新身份结果状态', async () => {
    const pending = deferred<void>(); vi.mocked(api.openDocumentPreviewTaskContent).mockReturnValue(pending.promise);
    const session = await setup([read]); await wrapper!.get('[data-testid="document-preview-task-open-pdf"]').trigger('click');
    session.currentUser!.permissions = [];
    pending.reject({ title: 'missing', status: 404, code: 'files.file.not_found' }); await flushPromises();
    session.currentUser!.permissions = [read]; await flushPromises();
    expect(wrapper!.find('[data-testid="document-result-unavailable"]').exists()).toBe(false);
    expect(wrapper!.get('[data-testid="document-preview-task-open-pdf"]').attributes('disabled')).toBeUndefined();
  });
  it('没有读取权限不加载；授权后加载，注销同步清理列表', async () => {
    const session = await setup([]); expect(api.listDocumentPreviewTasks).not.toHaveBeenCalled();
    session.currentUser!.permissions = [read]; await flushPromises(); expect(wrapper!.text()).toContain('敏感文档');
    session.state = 'anonymous'; await flushPromises(); expect(wrapper!.text()).not.toContain('敏感文档');
  });
  it('筛选请求交错时仅接入最新结果', async () => {
    const pending = deferred<{ items: typeof task[]; page: number; pageSize: number; total: number }>();
    vi.mocked(api.listDocumentPreviewTasks).mockReturnValueOnce(pending.promise).mockResolvedValue({ items: [{ ...task, documentTitle: '新文档' }], page: 1, pageSize: 20, total: 1 });
    await setup(); await wrapper!.get('[data-testid="document-preview-task-filter"]').setValue(outputId);
    await wrapper!.get('[data-testid="document-preview-task-filter"]').trigger('keydown', { key: 'Enter' }); await flushPromises();
    pending.resolve({ items: [task], page: 1, pageSize: 20, total: 1 }); await flushPromises();
    expect(wrapper!.text()).toContain('新文档'); expect(wrapper!.text()).not.toContain('敏感文档');
    expect(vi.mocked(api.listDocumentPreviewTasks).mock.calls[0]?.[3]?.aborted).toBe(true);
  });
  it('创建防重复，撤权后取消且不显示成功或刷新', async () => {
    const pending = deferred<typeof task>(); vi.mocked(api.createDocumentPreviewTask).mockReturnValue(pending.promise);
    const success = vi.spyOn(ElMessage, 'success'); const session = await setup();
    await wrapper!.get('[data-testid="document-preview-task-create"]').trigger('click'); await flushPromises();
    await wrapper!.get('[data-testid="document-preview-task-editor-form"] input').setValue(outputId);
    const button = wrapper!.findAllComponents(ElButton).find(value => value.attributes('data-testid') === 'document-preview-task-editor-submit')!;
    button.vm.$emit('click'); button.vm.$emit('click'); expect(api.createDocumentPreviewTask).toHaveBeenCalledTimes(1);
    session.currentUser!.permissions = [read]; expect(vi.mocked(api.createDocumentPreviewTask).mock.calls[0]?.[1]?.aborted).toBe(true);
    pending.resolve(task); await flushPromises(); expect(success).not.toHaveBeenCalled();
    expect(wrapper!.find('[data-testid="document-preview-task-editor-submit"]').exists()).toBe(false);
  });
  it('PDF 请求撤权后取消，迟到错误不保留；恢复权限可重试', async () => {
    const pending = deferred<void>(); vi.mocked(api.openDocumentPreviewTaskContent).mockReturnValueOnce(pending.promise).mockResolvedValue();
    const session = await setup([read]); await wrapper!.get('[data-testid="document-preview-task-open-pdf"]').trigger('click');
    session.currentUser!.permissions = []; expect(vi.mocked(api.openDocumentPreviewTaskContent).mock.calls[0]?.[1]?.aborted).toBe(true);
    pending.reject(new Error('late')); await flushPromises(); expect(wrapper!.find('.el-alert').exists()).toBe(false);
    session.currentUser!.permissions = [read]; await flushPromises();
    await wrapper!.get('[data-testid="document-preview-task-open-pdf"]').trigger('click'); await flushPromises();
    expect(api.openDocumentPreviewTaskContent).toHaveBeenCalledTimes(2);
  });
});

describe('文档预览创建弹窗关闭', () => {
  it.each(['resolve','reject'] as const)('关闭同步取消，迟到%s不反馈或刷新，重开清空输入', async outcome => {
    const pending=deferred<typeof task>();vi.mocked(api.createDocumentPreviewTask).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(task);
    const success=vi.spyOn(ElMessage,'success');await setup();
    await wrapper!.get('[data-testid="document-preview-task-create"]').trigger('click');await flushPromises();
    const inputs=wrapper!.get('[data-testid="document-preview-task-editor-form"]').findAll('input');
    await inputs[0]!.setValue(outputId);await inputs[1]!.setValue(outputId);
    await wrapper!.get('[data-testid="document-preview-task-editor-submit"]').trigger('click');
    const signal=vi.mocked(api.createDocumentPreviewTask).mock.calls[0]![1]!;
    wrapper!.getComponent(ArtFormDialog).vm.$emit('update:open',false);
    expect(signal.aborted).toBe(true);
    if(outcome==='resolve')pending.resolve(task);else pending.reject(new Error('late'));
    await flushPromises();expect(success).not.toHaveBeenCalled();expect(api.listDocumentPreviewTasks).toHaveBeenCalledOnce();expect(wrapper!.find('.el-alert').exists()).toBe(false);
    await wrapper!.get('[data-testid="document-preview-task-create"]').trigger('click');await flushPromises();
    expect(wrapper!.get('[data-testid="document-preview-task-editor-form"]').findAll('input').map(input=>(input.element as HTMLInputElement).value)).toEqual(['','']);
    await wrapper!.get('[data-testid="document-preview-task-editor-form"] input').setValue(outputId);
    await wrapper!.get('[data-testid="document-preview-task-editor-submit"]').trigger('click');await flushPromises();expect(success).toHaveBeenCalledOnce();expect(api.listDocumentPreviewTasks).toHaveBeenCalledTimes(2);
  });
});
