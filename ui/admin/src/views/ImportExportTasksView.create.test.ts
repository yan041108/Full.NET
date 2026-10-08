import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElOption, ElSelect } from 'element-plus';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ImportExportTasksView from './ImportExportTasksView.vue';
import * as api from '../api/import-export-tasks';
import { createOutputSession, deferred } from '../test/data-output-fixtures';
import { importTask } from '../test/task-lifecycle-fixtures';
vi.mock('../api/import-export-tasks', () => ({
  listImportExportTasks: vi.fn(), getImportExportTask: vi.fn(), createImportExportTask: vi.fn(), listStaticImportSchemas: vi.fn(),
  downloadStaticImportTemplate: vi.fn(), executeImportExportTask: vi.fn(), resumeImportExportTask: vi.fn(),
  retryImportExportTask: vi.fn(), downloadImportExportTaskErrorReceipt: vi.fn()
}));
const read = 'import_export.import_tasks.read'; const create = 'import_export.import_tasks.create';
const schemasRead = 'import_export.static_schemas.read'; const importPermission = 'organization.positions.import';
const schema = { schemaKey: importTask.schemaKey, displayName: '租户职位', scopeKey: 'tenant', requiredPermission: importPermission,
  worksheets: [{ worksheetKey: 'positions', displayName: '职位', headerColumns: ['Code', 'Name'] }] };
let wrapper: VueWrapper | undefined;
beforeEach(() => {
  vi.clearAllMocks();
  for (const operation of Object.values(api)) if (vi.isMockFunction(operation)) operation.mockReset();
  vi.mocked(api.listImportExportTasks).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  vi.mocked(api.listStaticImportSchemas).mockResolvedValue([schema]);
  vi.mocked(api.createImportExportTask).mockResolvedValue(importTask);
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
async function setup(permissions = [read, create, schemasRead, importPermission]) {
  const context = createOutputSession(permissions);
  wrapper = mount(ImportExportTasksView, { global: { plugins: [context.pinia], directives: { loading: {} },
    stubs: { ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot /><slot name="footer" /></div>' } } } });
  await flushPromises(); return context.session;
}
async function open() { await wrapper!.get('[data-testid="import-export-task-create"]').trigger('click'); await flushPromises(); }
async function file(value = new File(['fixture'], 'positions.xlsx')) {
  const input = wrapper!.get('[data-testid="import-create-file"]');
  Object.defineProperty(input.element, 'files', { value: [value], configurable: true }); await input.trigger('change'); return value;
}
describe('导入创建完整入口', () => {
  it('Host 身份切入正式租户上下文可以读取导入 Schema', async () => {
    const session = await setup();session.currentUser!.actorScope = 'host';await flushPromises();
    await open();expect(api.listStaticImportSchemas).toHaveBeenCalledOnce();
    expect(wrapper!.get('[data-testid="import-create-schema"]').text()).toContain('租户职位');
  });
  it.each(['host', 'tenant', 'tenant:00000000000000000000000000000000'])('不匹配作用域 %s 隐藏并关闭导入入口', async (scope) => {
    const session = await setup();await open();
    session.currentUser!.scope = scope;await flushPromises();
    expect(wrapper!.find('[data-testid="import-export-task-create"]').exists()).toBe(false);
    expect(wrapper!.find('[data-testid="import-create-file"]').exists()).toBe(false);
  });
  it('有权用户可打开提交入口；关闭时不预取目录', async () => {
    await setup(); expect(api.listStaticImportSchemas).not.toHaveBeenCalled();
    expect(wrapper!.get('[data-testid="import-export-task-create"]').attributes('disabled')).toBeUndefined();
    await open(); expect(api.listStaticImportSchemas).toHaveBeenCalledOnce();
    expect(wrapper!.get('[data-testid="import-create-schema"]').text()).toContain('租户职位');
  });
  it('缺少目录权限或处于 Host 不提供上传入口', async () => {
    const session = await setup([read, create]); expect(wrapper!.find('[data-testid="import-export-task-create"]').exists()).toBe(false);
    session.currentUser!.permissions = [read, create, schemasRead, importPermission];
    session.currentUser!.tenantId = null; session.currentUser!.scope = 'host'; session.currentUser!.actorScope = 'host';
    await flushPromises(); expect(wrapper!.find('[data-testid="import-export-task-create"]').exists()).toBe(false);
  });
  it('只展示当前作用域及业务权限允许的 Schema，切换时清空旧文件', async () => {
    vi.mocked(api.listStaticImportSchemas).mockResolvedValue([schema, { ...schema, schemaKey: 'forbidden', requiredPermission: 'unknown' },
      { ...schema, schemaKey: 'host', scopeKey: 'host' }, { ...schema, schemaKey: 'second', displayName: '第二模板' }]);
    await setup(); await open();
    const select = wrapper!.findAllComponents(ElSelect).find(value => value.attributes('data-testid') === 'import-create-schema')!;
    expect(select.findAllComponents(ElOption).map(value => value.props('value'))).toEqual([schema.schemaKey, 'second']);
    await file();
    select.vm.$emit('update:modelValue', 'second'); select.vm.$emit('change', 'second'); await flushPromises();
    await wrapper!.get('[data-testid="import-create-submit"]').trigger('click'); await flushPromises();
    expect(api.createImportExportTask).not.toHaveBeenCalled();
  });
  it('拒绝非 xlsx、空文件和超过 1MiB 的文件，不调用创建 API', async () => {
    await setup(); await open();
    for (const invalid of [new File(['a'], 'bad.csv'), new File([], 'empty.xlsx'), new File([new Uint8Array(1024 * 1024 + 1)], 'large.xlsx')]) {
      await file(invalid); await wrapper!.get('[data-testid="import-create-submit"]').trigger('click'); await flushPromises();
    }
    expect(api.createImportExportTask).not.toHaveBeenCalled();
  });
  it('正确上传只提交一次，失败后可重试；成功关闭并刷新及展示预校验行', async () => {
    const pending = deferred<typeof importTask>(); vi.mocked(api.createImportExportTask).mockReturnValueOnce(pending.promise).mockResolvedValue({
      ...importTask, invalidRowCount: 1, previewRows: [{ lineNumber: 2, isValid: false, errorCode: 'fixture.row_invalid', message: '名称必填' }]
    });
    await setup(); await open(); const selectedFile = await file();
    const submit = wrapper!.findAllComponents(ElButton).find(value => value.attributes('data-testid') === 'import-create-submit')!;
    submit.vm.$emit('click'); submit.vm.$emit('click'); expect(api.createImportExportTask).toHaveBeenCalledTimes(1);
    expect(vi.mocked(api.createImportExportTask).mock.calls[0]?.slice(0, 3)).toEqual([schema.schemaKey, 'positions', selectedFile]);
    pending.reject(new Error('retryable')); await flushPromises(); await wrapper!.get('[data-testid="import-create-submit"]').trigger('click'); await flushPromises();
    expect(api.createImportExportTask).toHaveBeenCalledTimes(2); expect(api.listImportExportTasks).toHaveBeenCalledTimes(2);
    expect(wrapper!.text()).toContain('名称必填'); expect(wrapper!.text()).toContain('fixture.row_invalid');
  });
  it('等待上传时关闭或撤权会取消，迟到完成不打开详情或刷新', async () => {
    const pending = deferred<typeof importTask>(); vi.mocked(api.createImportExportTask).mockReturnValue(pending.promise);
    const session = await setup(); await open(); await file(); await wrapper!.get('[data-testid="import-create-submit"]').trigger('click');
    session.currentUser!.permissions = [read]; expect(vi.mocked(api.createImportExportTask).mock.calls[0]?.[3]?.aborted).toBe(true);
    pending.resolve(importTask); await flushPromises(); expect(api.listImportExportTasks).toHaveBeenCalledTimes(2);
    expect(wrapper!.find('[data-testid="import-export-task-execute"]').exists()).toBe(false);
  });
  it('模板下载复用选择，浏览器触发异常仍释放 URL', async () => {
    vi.mocked(api.downloadStaticImportTemplate).mockResolvedValue(new Blob(['template']));
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fixture'); const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => { throw new Error('blocked'); });
    await setup(); await open(); await wrapper!.get('[data-testid="import-create-template"]').trigger('click'); await flushPromises();
    expect(api.downloadStaticImportTemplate).toHaveBeenCalledWith(schema.schemaKey, 'positions', expect.any(AbortSignal));
    expect(revoke).toHaveBeenCalledExactlyOnceWith('blob:fixture');
  });
  it('关闭弹窗取消等待模板，迟到 Blob 不触发下载', async () => {
    const pending = deferred<Blob>(); vi.mocked(api.downloadStaticImportTemplate).mockReturnValue(pending.promise);
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fixture'); await setup(); await open();
    await wrapper!.get('[data-testid="import-create-template"]').trigger('click');
    wrapper!.getComponent(ArtFormDialog).vm.$emit('update:open', false); await flushPromises();
    expect(vi.mocked(api.downloadStaticImportTemplate).mock.calls[0]?.[2]?.aborted).toBe(true);
    pending.resolve(new Blob(['private'])); await flushPromises(); expect(createUrl).not.toHaveBeenCalled();
  });
  it('关闭等待上传后不接入旧完成，重开重新加载目录', async () => {
    const pending = deferred<typeof importTask>(); vi.mocked(api.createImportExportTask).mockReturnValue(pending.promise);
    await setup(); await open(); await file(); await wrapper!.get('[data-testid="import-create-submit"]').trigger('click');
    wrapper!.getComponent(ArtFormDialog).vm.$emit('update:open', false); await flushPromises();
    expect(vi.mocked(api.createImportExportTask).mock.calls[0]?.[3]?.aborted).toBe(true);
    pending.resolve(importTask); await flushPromises(); expect(api.listImportExportTasks).toHaveBeenCalledTimes(1);
    await open(); expect(api.listStaticImportSchemas).toHaveBeenCalledTimes(2);
  });
});
