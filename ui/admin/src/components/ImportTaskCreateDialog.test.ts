import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import Dialog from './ImportTaskCreateDialog.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/import-export-tasks';
import { showProblem, showWarning } from '../feedback/fullNetMessage';

vi.mock('../api/import-export-tasks', () => ({ listStaticImportSchemas: vi.fn(), createImportExportTask: vi.fn(), downloadStaticImportTemplate: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showWarning: vi.fn(), showSuccess: vi.fn() }));
const schema = { schemaKey: 'enterprise.requests', displayName: '企业申请', scopeKey: 'tenant', requiredPermission: 'enterprise_request.enterprise_requests.create',
  worksheets: [{ worksheetKey: 'requests', displayName: '申请', headerColumns: ['title'] }] };
const wrappers: VueWrapper[] = [];
async function render(permissions = ['import_export.import_tasks.create', 'import_export.static_schemas.read', schema.requiredPermission]) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.currentUser = { id: 'user', tenantId: 'tenant', sessionId: 'session', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(Dialog, { props: { modelValue: true }, global: { plugins: [pinia], stubs: {
    ElDialog: { template: '<div><slot /></div>' }
  } } }); wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
async function choose(wrapper: VueWrapper, name = 'requests.xlsx', size = 10) {
  const input = wrapper.get('[data-testid="import-task-file"]');
  Object.defineProperty(input.element, 'files', { configurable: true, value: [new File(['a'.repeat(size)], name)] });
  await input.trigger('change');
}
beforeEach(() => { vi.mocked(api.listStaticImportSchemas).mockResolvedValue([schema]); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); vi.clearAllMocks(); });

it('缺少模板读取权限不请求目录或渲染表单', async () => {
  const { wrapper } = await render(['import_export.import_tasks.create']);
  expect(api.listStaticImportSchemas).not.toHaveBeenCalled(); expect(wrapper.find('form').exists()).toBe(false);
});
it('只显示具有业务权限的租户模板，空目录不允许提交', async () => {
  const { wrapper } = await render(['import_export.import_tasks.create', 'import_export.static_schemas.read']);
  expect(wrapper.text()).toContain('当前没有已授权的导入模板');
  expect(wrapper.get('[data-testid="import-task-submit"]').attributes('disabled')).toBeDefined();
});
it('拒绝伪扩展名与超限文件，允许重新选择后提交预校验', async () => {
  const { wrapper } = await render();
  await choose(wrapper, 'requests.csv'); await wrapper.get('[data-testid="import-task-submit"]').trigger('click'); await flushPromises();
  await choose(wrapper, 'requests.xlsx', 1024 * 1024 + 1); await wrapper.get('[data-testid="import-task-submit"]').trigger('click'); await flushPromises();
  expect(showWarning).toHaveBeenCalledTimes(2); expect(api.createImportExportTask).not.toHaveBeenCalled();
  const task = { id: 'created', previewRows: [] } as unknown as Awaited<ReturnType<typeof api.createImportExportTask>>;
  vi.mocked(api.createImportExportTask).mockResolvedValue(task);
  await choose(wrapper); await wrapper.get('[data-testid="import-task-submit"]').trigger('click'); await flushPromises();
  expect(api.createImportExportTask).toHaveBeenCalledWith(schema.schemaKey, 'requests', expect.any(File), expect.any(AbortSignal));
  expect(wrapper.emitted('created')?.[0]).toEqual([task]);
});
it('上传过程中禁止重复提交，会话切换取消请求并忽略迟到结果', async () => {
  let resolve!: (value: Awaited<ReturnType<typeof api.createImportExportTask>>) => void;
  vi.mocked(api.createImportExportTask).mockImplementation(() => new Promise(done => { resolve = done; }));
  const { wrapper, session } = await render(); await choose(wrapper);
  await wrapper.get('[data-testid="import-task-submit"]').trigger('click');
  await wrapper.get('form').trigger('submit'); expect(api.createImportExportTask).toHaveBeenCalledTimes(1);
  const signal = vi.mocked(api.createImportExportTask).mock.calls[0]![3]!;
  session.switching = true; await flushPromises(); expect(signal.aborted).toBe(true);
  resolve({ id: 'old' } as Awaited<ReturnType<typeof api.createImportExportTask>>); await flushPromises();
  expect(wrapper.emitted('created')).toBeUndefined(); expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([false]);
});
it('服务端拒绝上传后保留选择并允许重试', async () => {
  vi.mocked(api.createImportExportTask).mockRejectedValue({ status: 403, title: 'Denied', code: 'authorization.permission_denied' });
  const { wrapper } = await render(); await choose(wrapper);
  await wrapper.get('[data-testid="import-task-submit"]').trigger('click'); await flushPromises();
  expect(showProblem).toHaveBeenCalled(); expect(wrapper.emitted('created')).toBeUndefined();
  expect(wrapper.get('[data-testid="import-task-submit"]').attributes('disabled')).toBeUndefined();
});

it('编辑映射清除旧文件，新上传携带当前完整映射', async () => {
  const { wrapper } = await render(); await choose(wrapper);
  await wrapper.get('[data-testid="import-profile-custom"] input').setValue(true); await flushPromises();
  expect(wrapper.get('[data-testid="import-task-submit"]').attributes('disabled')).toBeDefined();
  await wrapper.get('[data-testid="import-profile-header-0"]').setValue('申请标题');
  await choose(wrapper); vi.mocked(api.createImportExportTask).mockResolvedValue({ id: 'created', previewRows: [] } as unknown as Awaited<ReturnType<typeof api.createImportExportTask>>);
  await wrapper.get('[data-testid="import-task-submit"]').trigger('click'); await flushPromises();
  expect(api.createImportExportTask).toHaveBeenCalledWith(schema.schemaKey, 'requests', expect.any(File), expect.any(AbortSignal),
    { headerColumns: ['title'], columns: [{ targetColumn: 'title', sourceHeader: '申请标题' }] });
});
