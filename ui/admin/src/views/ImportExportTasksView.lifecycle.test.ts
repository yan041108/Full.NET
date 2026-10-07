import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElDrawer, ElMessage } from 'element-plus';
import ImportExportTasksView from './ImportExportTasksView.vue';
import * as api from '../api/import-export-tasks';
import { createOutputSession, deferred } from '../test/data-output-fixtures';
import { importTask } from '../test/task-lifecycle-fixtures';
vi.mock('../api/import-export-tasks', () => ({
  listImportExportTasks: vi.fn(), getImportExportTask: vi.fn(), executeImportExportTask: vi.fn(),
  resumeImportExportTask: vi.fn(), retryImportExportTask: vi.fn(), downloadImportExportTaskErrorReceipt: vi.fn()
}));
let wrapper: VueWrapper | undefined;
const read = 'import_export.import_tasks.read'; const execute = 'import_export.import_tasks.execute';
const other = { ...importTask, id: '019bc2b1-2a40-7cc3-8992-a80de51bf298', schemaDisplayName: '新任务' };
beforeEach(() => {
  vi.clearAllMocks();
  for (const operation of Object.values(api)) if (vi.isMockFunction(operation)) operation.mockReset();
  vi.mocked(api.listImportExportTasks).mockResolvedValue({ items: [importTask, other], page: 1, pageSize: 20, total: 2 });
  vi.mocked(api.getImportExportTask).mockImplementation(async id => id === other.id ? other : importTask);
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
const setup = async (permissions = [read, execute]) => {
  const context = createOutputSession(permissions); wrapper = mount(ImportExportTasksView, {
    global: { plugins: [context.pinia], directives: { loading: {} } }
  });
  await flushPromises(); return context.session;
};
const detail = async (index = 0) => { await wrapper!.findAll('[data-testid="import-export-task-detail"]')[index]!.trigger('click'); await flushPromises(); };
describe('导入任务请求归属', () => {
  it('两个详情交错返回时只保留最后选择', async () => {
    const pending = deferred<typeof importTask>(); vi.mocked(api.getImportExportTask).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(other);
    await setup(); await detail(); await detail(1); pending.resolve(importTask); await flushPromises();
    expect(wrapper!.getComponent(ElDrawer).text()).toContain('新任务');
    expect(vi.mocked(api.getImportExportTask).mock.calls[0]?.[1]?.aborted).toBe(true);
  });
  it('切换租户同步取消详情并忽略迟到结果', async () => {
    const pending = deferred<typeof importTask>(); vi.mocked(api.getImportExportTask).mockReturnValue(pending.promise);
    const session = await setup(); await detail(); session.currentUser!.tenantId = other.id;
    expect(vi.mocked(api.getImportExportTask).mock.calls[0]?.[1]?.aborted).toBe(true);
    pending.resolve(importTask); await flushPromises(); expect(wrapper!.find('[data-testid="import-export-task-execute"]').exists()).toBe(false);
  });
  it('选择另一个任务取消旧执行，迟到完成不覆盖新详情或通知成功', async () => {
    const pending = deferred<typeof importTask>(); vi.mocked(api.executeImportExportTask).mockReturnValue(pending.promise);
    const success = vi.spyOn(ElMessage, 'success'); await setup(); await detail();
    await wrapper!.get('[data-testid="import-export-task-execute"]').trigger('click'); await detail(1);
    pending.resolve({ ...importTask, schemaDisplayName: '迟到旧任务' }); await flushPromises();
    expect(wrapper!.getComponent(ElDrawer).text()).toContain('新任务');
    expect(success).not.toHaveBeenCalled(); expect(vi.mocked(api.executeImportExportTask).mock.calls[0]?.[1]?.aborted).toBe(true);
  });
  it('关闭抽屉立即取消详情；迟到失败不通知', async () => {
    const pending = deferred<typeof importTask>(); vi.mocked(api.getImportExportTask).mockReturnValue(pending.promise);
    const error = vi.spyOn(ElMessage, 'error'); await setup(); await detail();
    wrapper!.getComponent(ElDrawer).vm.$emit('update:modelValue', false);
    expect(vi.mocked(api.getImportExportTask).mock.calls[0]?.[1]?.aborted).toBe(true);
    pending.reject(new Error('late')); await flushPromises(); expect(error).not.toHaveBeenCalled();
  });
  it('恢复和重试共享互斥，失败后可以重新操作', async () => {
    const partial = { ...importTask, statusKey: 'execution_partial' }; vi.mocked(api.getImportExportTask).mockResolvedValue(partial);
    const pending = deferred<typeof importTask>(); vi.mocked(api.resumeImportExportTask).mockReturnValue(pending.promise);
    vi.mocked(api.retryImportExportTask).mockResolvedValue(importTask); await setup(); await detail();
    const buttons = wrapper!.findAllComponents(ElButton);
    buttons.find(button => button.attributes('data-testid') === 'import-export-task-resume')!.vm.$emit('click');
    buttons.find(button => button.attributes('data-testid') === 'import-export-task-retry')!.vm.$emit('click');
    expect(api.resumeImportExportTask).toHaveBeenCalledTimes(1); expect(api.retryImportExportTask).not.toHaveBeenCalled();
    pending.reject(new Error('retryable')); await flushPromises();
    await wrapper!.get('[data-testid="import-export-task-retry"]').trigger('click'); await flushPromises();
    expect(api.retryImportExportTask).toHaveBeenCalledTimes(1);
  });
  it('回执等待期间撤权不能下载；失败触发也释放 URL', async () => {
    vi.mocked(api.getImportExportTask).mockResolvedValue({ ...importTask, hasErrorReceipt: true });
    const pending = deferred<Blob>(); vi.mocked(api.downloadImportExportTaskErrorReceipt).mockReturnValueOnce(pending.promise).mockResolvedValue(new Blob(['fixture']));
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fixture');
    const revokeUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => { throw new Error('blocked'); });
    const session = await setup(); await detail(); await wrapper!.get('[data-testid="import-export-task-error-receipt"]').trigger('click');
    session.currentUser!.permissions = [read]; pending.resolve(new Blob(['private'])); await flushPromises();
    expect(createUrl).not.toHaveBeenCalled();
    session.currentUser!.permissions = [read, execute]; await flushPromises(); await detail();
    await wrapper!.get('[data-testid="import-export-task-error-receipt"]').trigger('click'); await flushPromises();
    expect(revokeUrl).toHaveBeenCalledOnce();
  });
  it('离开页面取消列表并忽略迟到失败', async () => {
    const pending = deferred<{ items: typeof importTask[]; page: number; pageSize: number; total: number }>();
    vi.mocked(api.listImportExportTasks).mockReturnValue(pending.promise); await setup([read]); wrapper!.unmount(); wrapper = undefined;
    expect(vi.mocked(api.listImportExportTasks).mock.calls[0]?.[3]?.aborted).toBe(true);
    pending.reject(new Error('late')); await flushPromises();
  });
});
