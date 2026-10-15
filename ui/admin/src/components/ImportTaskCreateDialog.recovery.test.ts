import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { ElSelect } from 'element-plus';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ImportTaskCreateDialog from './ImportTaskCreateDialog.vue';
import * as api from '../api/import-export-tasks';
import { createOutputSession, deferred } from '../test/data-output-fixtures';
import { importTask } from '../test/task-lifecycle-fixtures';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
vi.mock('../api/import-export-tasks', () => ({ listStaticImportSchemas: vi.fn(), createImportExportTask: vi.fn(), downloadStaticImportTemplate: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn() }));
const schema = { schemaKey: importTask.schemaKey, displayName: '职位', scopeKey: 'tenant', requiredPermission: 'organization.positions.import', worksheets: [{ worksheetKey: 'positions', displayName: '职位', headerColumns: ['Code'] }, { worksheetKey: 'second', displayName: '其他', headerColumns: ['Name'] }] };
const other = { ...schema, schemaKey: 'another.schema' };
let wrapper: VueWrapper;
interface State { schemaKey: string; worksheetKey: string; selectedFile?: File; receipt?: { sequence: number; outcome: string; taskId?: string }; submit: () => Promise<void>; loadSchemas: () => Promise<void>; downloadTemplate: () => Promise<void>; acknowledgeUnknown: () => void }
const state = () => wrapper.vm as unknown as State;
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.listStaticImportSchemas).mockResolvedValue([schema, other]); vi.mocked(api.createImportExportTask).mockResolvedValue({ ...importTask }); });
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup() {
  const context = createOutputSession(['import_export.import_tasks.create', 'import_export.static_schemas.read', schema.requiredPermission]);
  wrapper = mount(ImportTaskCreateDialog, { props: { open: true }, global: { plugins: [context.pinia], stubs: { ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' } } } }); await flushPromises(); return context;
}
async function file(value = new File(['fixture'], 'positions.xlsx')) {
  const input = wrapper.get('[data-testid="import-create-file"]'); Object.defineProperty(input.element, 'files', { value: [value], configurable: true }); await input.trigger('change'); return value;
}
async function reopen() { await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true }); await flushPromises(); }
describe('导入创建结果与恢复', () => {
  it('目录首次失败可见错误和重试入口，不误报空目录', async () => {
    vi.mocked(api.listStaticImportSchemas).mockRejectedValueOnce(new Error('offline')); await setup();
    expect(wrapper.find('[data-testid="import-create-catalog-problem"]').exists()).toBe(true); expect(wrapper.find('[data-testid="import-create-no-schemas"]').exists()).toBe(false);
    await wrapper.get('[data-testid="import-create-refresh"]').trigger('click'); await flushPromises(); expect(state().schemaKey).toBe(schema.schemaKey); expect(api.listStaticImportSchemas).toHaveBeenCalledTimes(2);
  });
  it('目录重试失败保留已确认目录、选择和文件', async () => {
    await setup(); const selected = await file(); vi.mocked(api.listStaticImportSchemas).mockRejectedValueOnce(new Error('offline')); await state().loadSchemas();
    expect(state().schemaKey).toBe(schema.schemaKey); expect(state().selectedFile).toBe(selected);
  });
  it('相同目录补读保留所选工作表和文件', async () => {
    await setup(); state().worksheetKey = 'second'; const selected = await file(); await state().loadSchemas(); expect(state().worksheetKey).toBe('second'); expect(state().selectedFile).toBe(selected);
  });
  it('直接修改Schema清除旧文件并选择其真实工作表', async () => {
    await setup(); await file(); state().schemaKey = other.schemaKey; expect(state().selectedFile).toBeUndefined(); expect(state().worksheetKey).toBe('positions');
  });
  it('清空原生选择值后仍显示已选文件，允许再次选择同一文件', async () => {
    await setup(); const selected = await file(); expect((wrapper.get('[data-testid="import-create-file"]').element as HTMLInputElement).value).toBe('');
    expect(wrapper.get('[data-testid="import-create-selected-file"]').text()).toContain(selected.name); await file(selected); expect(state().selectedFile).toBe(selected);
  });
  it('直接修改工作表使旧下载失效，迟到Blob不交付', async () => {
    await setup(); const pending = deferred<Blob>(); vi.mocked(api.downloadStaticImportTemplate).mockReturnValueOnce(pending.promise); const url = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:old');
    const downloading = state().downloadTemplate(); state().worksheetKey = 'second'; expect(vi.mocked(api.downloadStaticImportTemplate).mock.calls[0]![2]!.aborted).toBe(true); pending.resolve(new Blob(['old'])); await downloading; expect(url).not.toHaveBeenCalled();
  });
  it('目录中工作表定义变化清除旧文件', async () => {
    await setup(); await file(); vi.mocked(api.listStaticImportSchemas).mockResolvedValueOnce([{ ...schema, worksheets: [{ ...schema.worksheets[0]!, headerColumns: ['Changed'] }] }]); await state().loadSchemas(); expect(state().selectedFile).toBeUndefined();
  });
  it('旧选择器回调不能修改重开窗口的选择', async () => {
    await setup(); const old = wrapper.getComponent(ElSelect).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void; await reopen(); old(other.schemaKey); await flushPromises(); expect(state().schemaKey).toBe(schema.schemaKey);
  });
  it('旧提交和关闭回调不能操作新窗口', async () => {
    await setup(); const dialog = wrapper.getComponent(ArtFormDialog), submit = dialog.vm.$.vnode.props!.onConfirm as () => void, close = dialog.vm.$.vnode.props!['onUpdate:open'] as (open: boolean) => void;
    await reopen(); await file(); submit(); close(false); await flushPromises(); expect(api.createImportExportTask).not.toHaveBeenCalled(); expect(wrapper.getComponent(ArtFormDialog).props('open')).toBe(true);
  });
  it('关闭后父组件尚未更新open也不能重新读取、下载或提交', async () => {
    await setup(); await file(); wrapper.getComponent(ArtFormDialog).vm.$emit('update:open', false); await state().loadSchemas(); await state().downloadTemplate(); await state().submit();
    expect(api.listStaticImportSchemas).toHaveBeenCalledOnce(); expect(api.createImportExportTask).not.toHaveBeenCalled(); expect(api.downloadStaticImportTemplate).not.toHaveBeenCalled();
  });
  it('本地关闭后仅业务参数变化不能重新打开窗口或恢复提交资格', async () => {
    await setup(); wrapper.getComponent(ArtFormDialog).vm.$emit('update:open', false); await wrapper.setProps({ schemaKey: other.schemaKey }); await flushPromises();
    await state().loadSchemas(); expect(api.listStaticImportSchemas).toHaveBeenCalledOnce(); expect(wrapper.getComponent(ArtFormDialog).props('open')).toBe(false);
  });
  it('选择新文件后旧提交回调不能提交新的文件', async () => {
    await setup(); await file(); const submit = wrapper.getComponent(ArtFormDialog).vm.$.vnode.props!.onConfirm as () => void; await file(new File(['another'], 'new.xlsx'));
    submit(); await flushPromises(); expect(api.createImportExportTask).not.toHaveBeenCalled();
  });
  it('未知创建不能立即重发，关闭重开仍要求显式核对', async () => {
    await setup(); await file(); vi.mocked(api.createImportExportTask).mockRejectedValueOnce(new Error('lost')); await state().submit(); await state().submit(); expect(api.createImportExportTask).toHaveBeenCalledOnce();
    await reopen(); await file(); await state().submit(); expect(api.createImportExportTask).toHaveBeenCalledOnce(); expect(state().receipt?.outcome).toBe('unconfirmed');
  });
  it('显式核对确认只解除门槛，不自动创建；之后仍须选择提交', async () => {
    await setup(); await file(); vi.mocked(api.createImportExportTask).mockRejectedValueOnce(new Error('lost')); await state().submit(); state().acknowledgeUnknown(); await flushPromises();
    await wrapper.get('[data-testid="import-create-recovery-confirm"]').trigger('click'); await flushPromises(); expect(api.createImportExportTask).toHaveBeenCalledOnce();
    await state().submit(); expect(api.createImportExportTask).toHaveBeenCalledTimes(2);
  });
  it('旧核对回调不能解除下一次未知提交门槛', async () => {
    await setup(); await file(); vi.mocked(api.createImportExportTask).mockRejectedValue(new Error('lost')); await state().submit(); state().acknowledgeUnknown(); await flushPromises();
    const dialog = wrapper.findAllComponents(ArtFormDialog).find(item => item.props('confirmTestId') === 'import-create-recovery-confirm')!, old = dialog.vm.$.vnode.props!.onConfirm as () => void;
    old(); await state().submit(); state().acknowledgeUnknown(); await flushPromises(); old(); await state().submit(); expect(api.createImportExportTask).toHaveBeenCalledTimes(2);
  });
  it.each([0, 302, 408, 503])('创建状态%s保留未知结果，不能直接重发', async status => {
    await setup(); await file(); vi.mocked(api.createImportExportTask).mockRejectedValueOnce({ status, code: 'gateway.failure' }); await state().submit(); await state().submit();
    expect(api.createImportExportTask).toHaveBeenCalledOnce(); expect(state().receipt?.outcome).toBe('unconfirmed');
  });
  it('明确409拒绝保留文件，允许人工再次提交', async () => {
    await setup(); const selected = await file(); vi.mocked(api.createImportExportTask).mockRejectedValueOnce({ status: 409, code: 'import_export.task.rejected' }); await state().submit();
    expect(state().receipt?.outcome).toBe('failed'); expect(state().selectedFile).toBe(selected); await state().submit(); expect(api.createImportExportTask).toHaveBeenCalledTimes(2);
  });
  it('创建成功立即本地关闭并保留任务ID，父组件忽略关闭也不重复创建', async () => {
    await setup(); await file(); await state().submit(); await state().submit(); expect(state().receipt).toMatchObject({ outcome: 'succeeded', taskId: importTask.id }); expect(api.createImportExportTask).toHaveBeenCalledOnce(); expect(wrapper.emitted('created')).toHaveLength(1);
  });
  it('关闭在途创建保留未知回执，晚成功不交付created', async () => {
    await setup(); await file(); const pending = deferred<typeof importTask>(); vi.mocked(api.createImportExportTask).mockReturnValueOnce(pending.promise); const submitting = state().submit(); wrapper.getComponent(ArtFormDialog).vm.$emit('update:open', false);
    pending.resolve(importTask); await submitting; await reopen(); expect(state().receipt?.outcome).toBe('unconfirmed'); expect(wrapper.emitted('created')).toBeUndefined(); expect(showSuccess).not.toHaveBeenCalled();
  });
  it('在途创建直接切换Schema保留未知，不把旧结果交给新选择', async () => {
    await setup(); await file(); const pending = deferred<typeof importTask>(); vi.mocked(api.createImportExportTask).mockReturnValueOnce(pending.promise); const submitting = state().submit(); state().schemaKey = other.schemaKey;
    pending.resolve(importTask); await submitting; expect(state().receipt?.outcome).toBe('unconfirmed'); expect(wrapper.emitted('created')).toBeUndefined();
  });
  it.each(['tenantId', 'schemaKey', 'worksheetKey', 'requestedByUserId'])('创建拒绝错配%s并保留未知结果', async field => {
    await setup(); await file(); const value = field.endsWith('Id') ? '019bc2b1-2a40-7cc3-8992-a80de51bf298' : 'another'; vi.mocked(api.createImportExportTask).mockResolvedValueOnce({ ...importTask, [field]: value }); await state().submit();
    expect(wrapper.emitted('created')).toBeUndefined(); expect(state().receipt?.outcome).toBe('unconfirmed');
  });
  it('新会话清除前一会话回执和恢复确认', async () => {
    const { session } = await setup(); await file(); vi.mocked(api.createImportExportTask).mockRejectedValueOnce(new Error('lost')); await state().submit(); state().acknowledgeUnknown();
    session.currentUser = { ...session.currentUser!, sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf298' }; await flushPromises(); expect(state().receipt).toBeUndefined(); expect(wrapper.find('[data-testid="import-create-recovery-confirm"]').exists()).toBe(false); expect(showProblem).toHaveBeenCalledOnce();
  });
});
