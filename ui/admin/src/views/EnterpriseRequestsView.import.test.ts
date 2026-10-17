import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElSelect } from 'element-plus';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import EnterpriseRequestsView from './EnterpriseRequestsView.vue';
import ImportTaskCreateDialog from '../components/ImportTaskCreateDialog.vue';
import { enterpriseRequestsHttp } from '../api/enterprise-requests';
import * as api from '../api/import-export-tasks';
import { createOutputSession, deferred, outputId } from '../test/data-output-fixtures';
import { importTask } from '../test/task-lifecycle-fixtures';

vi.mock('../api/enterprise-requests', async original => ({
  ...await original<typeof import('../api/enterprise-requests')>(), enterpriseRequestsHttp: { request: vi.fn() }
}));
vi.mock('../api/import-export-tasks', () => ({ listStaticImportSchemas: vi.fn(), createImportExportTask: vi.fn(), downloadStaticImportTemplate: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn() }));
const permissions = ['enterprise_request.enterprise_requests.read', 'enterprise_request.enterprise_requests.create',
  'import_export.import_tasks.read', 'import_export.import_tasks.create', 'import_export.static_schemas.read'];
const schema = { schemaKey: 'demo.enterprise_requests', displayName: '企业申请', scopeKey: 'tenant', requiredPermission: permissions[1]!,
  worksheets: [{ worksheetKey: 'requests', displayName: '申请', headerColumns: ['RequestNumber'] }] };
const task = { ...importTask, schemaKey: schema.schemaKey, worksheetKey: 'requests' };
let wrapper: VueWrapper | undefined; let router: Router;
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(enterpriseRequestsHttp.request).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  vi.mocked(api.listStaticImportSchemas).mockResolvedValue([schema]); vi.mocked(api.createImportExportTask).mockResolvedValue(task);
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup(granted = permissions) {
  const context = createOutputSession(granted);
  router = createRouter({ history: createMemoryHistory(), routes: [
    { name: 'enterprise-requests', path: '/requests', component: { render: () => null } },
    { name: 'import-export-tasks', path: '/tasks', component: { render: () => null } }
  ] }); await router.push({ name: 'enterprise-requests' });
  wrapper = mount(EnterpriseRequestsView, { global: { plugins: [context.pinia, router], directives: { loading: {} }, stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); await flushPromises(); return context.session;
}
async function open() { await wrapper!.get('[data-testid="enterprise-request-import"]').trigger('click'); await flushPromises(); }
async function upload() {
  const input = wrapper!.get('[data-testid="import-create-file"]');
  Object.defineProperty(input.element, 'files', { value: [new File(['fixture'], 'requests.xlsx')], configurable: true }); await input.trigger('change');
  await wrapper!.get('[data-testid="import-create-submit"]').trigger('click');
}
describe('企业申请 Excel 导入业务入口', () => {
  it.each(permissions)('缺少 %s 不提供不完整入口', async missing => {
    await setup(permissions.filter(value => value !== missing)); expect(wrapper!.find('[data-testid="enterprise-request-import"]').exists()).toBe(false);
    expect(api.listStaticImportSchemas).not.toHaveBeenCalled();
  });
  it('只在打开时读取指定模板，预校验完成直达任务详情', async () => {
    await setup(); expect(api.listStaticImportSchemas).not.toHaveBeenCalled(); await open();
    expect(wrapper!.getComponent(ElSelect).props('modelValue')).toBe(schema.schemaKey);
    await upload(); await flushPromises(); expect(router.currentRoute.value.name).toBe('import-export-tasks');
    expect(router.currentRoute.value.query).toEqual({ taskId: outputId });
    expect(api.createImportExportTask).toHaveBeenCalledWith(schema.schemaKey, 'requests', expect.any(File), expect.any(AbortSignal));
  });
  it('撤权关闭上传并拒绝迟到任务导航', async () => {
    const pending = deferred<typeof task>(); vi.mocked(api.createImportExportTask).mockReturnValueOnce(pending.promise);
    const session = await setup(); await open(); await upload();
    const signal = vi.mocked(api.createImportExportTask).mock.calls[0]![3]!;
    session.currentUser = { ...session.currentUser!, permissions: [permissions[0]!] }; await flushPromises();
    pending.resolve(task); await flushPromises(); expect(signal.aborted).toBe(true); expect(router.currentRoute.value.name).toBe('enterprise-requests');
  });
  it('其它租户或其它业务任务的 created 不能导航', async () => {
    await setup(); await open(); const dialog = wrapper!.getComponent(ImportTaskCreateDialog);
    dialog.vm.$emit('created', { ...task, tenantId: '01980000-0000-7000-8000-000000000003' });
    dialog.vm.$emit('created', { ...task, schemaKey: 'other.schema' }); await flushPromises();
    expect(router.currentRoute.value.name).toBe('enterprise-requests');
  });
  it('Host 身份切入可信租户可导入，Host 作用域不能使用入口', async () => {
    const session = await setup(); session.currentUser!.actorScope = 'host'; await flushPromises();
    expect(wrapper!.find('[data-testid="enterprise-request-import"]').exists()).toBe(true);
    session.currentUser!.scope = 'host'; await flushPromises(); expect(wrapper!.find('[data-testid="enterprise-request-import"]').exists()).toBe(false);
  });
});
