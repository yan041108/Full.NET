import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElSelect } from 'element-plus';
import ImportTaskCreateDialog from './ImportTaskCreateDialog.vue';
import * as api from '../api/import-export-tasks';
import { createOutputSession, deferred } from '../test/data-output-fixtures';
import { importTask } from '../test/task-lifecycle-fixtures';
import { showSuccess } from '../feedback/fullNetMessage';

vi.mock('../api/import-export-tasks', () => ({ listStaticImportSchemas: vi.fn(), createImportExportTask: vi.fn(), downloadStaticImportTemplate: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn() }));
const target = { schemaKey: 'demo.enterprise_requests', displayName: '企业申请', scopeKey: 'tenant', requiredPermission: 'enterprise_request.enterprise_requests.create',
  worksheets: [{ worksheetKey: 'requests', displayName: '申请', headerColumns: ['RequestNumber'] }] };
const other = { ...target, schemaKey: 'organization.tenant_positions', displayName: '职位' };
let wrapper: VueWrapper | undefined;
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.listStaticImportSchemas).mockResolvedValue([other, target]);
  vi.mocked(api.createImportExportTask).mockResolvedValue({ ...importTask, schemaKey: target.schemaKey, worksheetKey: 'requests' });
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup(schemaKey: string | undefined = target.schemaKey, businessPermission = true) {
  const context = createOutputSession(['import_export.import_tasks.create', 'import_export.static_schemas.read', ...(businessPermission ? [target.requiredPermission] : [])]);
  wrapper = mount(ImportTaskCreateDialog, { props: { open: true, schemaKey }, global: { plugins: [context.pinia], stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); await flushPromises(); return context.session;
}
async function upload() {
  const input = wrapper!.get('[data-testid="import-create-file"]');
  Object.defineProperty(input.element, 'files', { value: [new File(['fixture'], 'requests.xlsx')], configurable: true }); await input.trigger('change');
  await wrapper!.get('[data-testid="import-create-submit"]').trigger('click');
}
describe('业务入口限定导入目录', () => {
  it('只选择业务指定且获权的 Schema，不默认选择目录首项', async () => {
    await setup(); expect(wrapper!.findAllComponents(ElSelect)[0]!.props('modelValue')).toBe(target.schemaKey);
    expect(wrapper!.text()).not.toContain('职位'); await upload(); await flushPromises();
    expect(api.createImportExportTask).toHaveBeenCalledWith(target.schemaKey, 'requests', expect.any(File), expect.any(AbortSignal));
  });
  it.each(['unknown.schema', ''])('指定不可用 Schema %s 不回退到其它模板', async schemaKey => {
    await setup(schemaKey); expect(wrapper!.findAllComponents(ElSelect)[0]!.props('modelValue')).toBe('');
    await upload(); expect(api.createImportExportTask).not.toHaveBeenCalled(); expect(api.downloadStaticImportTemplate).not.toHaveBeenCalled();
  });
  it('指定 Schema 无业务权限时不显示或上传', async () => {
    await setup(target.schemaKey, false); await upload(); expect(api.createImportExportTask).not.toHaveBeenCalled();
    expect(wrapper!.findAllComponents(ElSelect)[0]!.props('modelValue')).toBe('');
  });
  it('未限定 Schema 的通用入口保留原目录', async () => {
    await setup(); await wrapper!.setProps({ schemaKey: undefined }); await flushPromises();
    expect(wrapper!.findAllComponents(ElSelect)[0]!.props('modelValue')).toBe(other.schemaKey);
  });
  it('业务参数变化取消旧目录，迟到目录不能替换新选择', async () => {
    const pending = deferred<typeof target[]>(); vi.mocked(api.listStaticImportSchemas).mockReturnValueOnce(pending.promise);
    await setup(); const signal = vi.mocked(api.listStaticImportSchemas).mock.calls[0]![0]!;
    await wrapper!.setProps({ schemaKey: other.schemaKey }); await flushPromises();
    pending.resolve([target]); await flushPromises(); expect(signal.aborted).toBe(true);
    expect(wrapper!.findAllComponents(ElSelect)[0]!.props('modelValue')).toBe(other.schemaKey);
  });
  it('业务参数变化取消旧上传且清除文件，不交付旧 created', async () => {
    const pending = deferred<typeof importTask>(); vi.mocked(api.createImportExportTask).mockReturnValueOnce(pending.promise);
    await setup(); await upload(); const signal = vi.mocked(api.createImportExportTask).mock.calls[0]![3]!;
    await wrapper!.setProps({ schemaKey: other.schemaKey }); await flushPromises();
    pending.resolve(importTask); await flushPromises(); expect(signal.aborted).toBe(true);
    expect(wrapper!.emitted('created')).toBeUndefined(); expect(showSuccess).not.toHaveBeenCalled();
    await wrapper!.get('[data-testid="import-create-submit"]').trigger('click'); expect(api.createImportExportTask).toHaveBeenCalledOnce();
  });
});
