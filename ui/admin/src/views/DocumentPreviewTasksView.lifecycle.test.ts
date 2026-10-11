import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElMessage } from 'element-plus';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import DocumentPreviewTasksView from './DocumentPreviewTasksView.vue';
import * as api from '../api/document-preview-tasks';
import { createOutputSession, deferred, outputId } from '../test/data-output-fixtures';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
vi.mock('../api/document-preview-tasks', () => ({
  listDocumentPreviewTasks: vi.fn(), createDocumentPreviewTask: vi.fn(), openDocumentPreviewTaskContent: vi.fn()
}));
let wrapper: VueWrapper | undefined;
const read = 'document.host_preview_tasks.read'; const create = 'document.host_preview_tasks.create';
beforeEach(() => {
  vi.clearAllMocks();
  for (const operation of Object.values(api)) if (vi.isMockFunction(operation)) operation.mockReset();
  vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [task], page: 1, pageSize: 20, total: 1 });
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
const setup = async (permissions = [read, create]) => {
  const context = createOutputSession(permissions); wrapper = mount(DocumentPreviewTasksView, {
    global: { plugins: [context.pinia], directives: { loading: {} }, stubs: {
      ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot /><slot name="footer" /></div>' }
    } }
  });
  await flushPromises(); return context.session;
};
describe('文档预览请求归属', () => {
  it('没有读取权限不加载；授权后加载，注销同步清理列表', async () => {
    const session = await setup([]); expect(api.listDocumentPreviewTasks).not.toHaveBeenCalled();
    session.currentUser!.permissions = [read]; await flushPromises(); expect(wrapper!.text()).toContain('敏感文档');
    session.state = 'anonymous'; await flushPromises(); expect(wrapper!.text()).not.toContain('敏感文档');
  });
  it('筛选请求交错时仅接入最新结果', async () => {
    const pending = deferred<{ items: typeof task[]; page: number; pageSize: number; total: number }>();
    vi.mocked(api.listDocumentPreviewTasks).mockReturnValueOnce(pending.promise).mockResolvedValue({ items: [{ ...task, documentTitle: '新文档' }], page: 1, pageSize: 20, total: 1 });
    await setup(); await wrapper!.get('[data-testid="document-preview-task-filter"]').setValue(outputId);
    await wrapper!.get('[data-testid="document-preview-task-filter"]').trigger('keyup.enter'); await flushPromises();
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
