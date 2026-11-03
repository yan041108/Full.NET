import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import Dialog from './ReportingDefinitionGrantsDialog.vue';
import { useSessionStore } from '../auth/session';
const api = vi.hoisted(() => ({ listHostTenants: vi.fn(), listReportingDefinitionTenantGrants: vi.fn(), setReportingDefinitionTenantGrant: vi.fn() }));
vi.mock('../api/tenants', () => ({ listHostTenants: api.listHostTenants }));
vi.mock('../api/reporting-publications', () => api);
const wrappers: VueWrapper[] = [];
const all = ['reporting.definition_grants.read', 'reporting.definition_grants.manage', 'tenancy.tenants.read'];
const grant = { id: 'grant', definitionId: 'definition', tenantId: 'tenant', isEnabled: true, version: 3 };
function page(items: unknown[] = []) { return { items, page: 1, pageSize: 20, total: items.length }; }
function render(permissions = all) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.currentUser = { id: 'host', sessionId: 'session', tenantId: null, permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(Dialog, { props: { definitionId: 'definition', modelValue: false,
    'onUpdate:modelValue': value => { void wrapper.setProps({ modelValue: value }); } }, global: { plugins: [pinia], stubs: {
      ElDialog: { props: ['modelValue'], template: '<section v-if="modelValue"><slot/></section>' }
    } } }); wrappers.push(wrapper); return { wrapper, session };
}
beforeEach(() => { vi.resetAllMocks(); api.listHostTenants.mockResolvedValue(page([{ id: 'tenant', name: 'Acme' }])); api.listReportingDefinitionTenantGrants.mockResolvedValue(page([grant])); api.setReportingDefinitionTenantGrant.mockResolvedValue({ ...grant, isEnabled: false, version: 4 }); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });
it('只读授权目录不加载租户管理目录，也不显示写操作', async () => {
  const { wrapper } = render(['reporting.definition_grants.read']); await wrapper.setProps({ modelValue: true }); await flushPromises();
  expect(api.listReportingDefinitionTenantGrants).toHaveBeenCalledTimes(1); expect(api.listHostTenants).not.toHaveBeenCalled();
  expect(wrapper.find('[data-testid="reporting-grant-toggle"]').exists()).toBe(false);
});
it('撤销使用当前授权的乐观版本并刷新权威结果', async () => {
  const { wrapper } = render(); await wrapper.setProps({ modelValue: true }); await flushPromises();
  await wrapper.get('[data-testid="reporting-grant-toggle"]').trigger('click'); await flushPromises();
  expect(api.setReportingDefinitionTenantGrant).toHaveBeenCalledWith('definition', 'tenant', false, 3, expect.any(AbortSignal));
  expect(api.listReportingDefinitionTenantGrants).toHaveBeenCalledTimes(2);
});
it('版本冲突显示失败并重新加载，不静默覆盖其他管理员修改', async () => {
  api.setReportingDefinitionTenantGrant.mockRejectedValue({ status: 409, code: 'reporting.grant.version_conflict', title: 'Changed' });
  const { wrapper } = render(); await wrapper.setProps({ modelValue: true }); await flushPromises();
  await wrapper.get('[data-testid="reporting-grant-toggle"]').trigger('click'); await flushPromises();
  expect(api.setReportingDefinitionTenantGrant).toHaveBeenCalledTimes(1); expect(api.listReportingDefinitionTenantGrants).toHaveBeenCalledTimes(2);
});
it('进入租户上下文立即关闭并取消在途 Host 授权读取', async () => {
  let resolve!: (value: unknown) => void;
  api.listReportingDefinitionTenantGrants.mockImplementation(() => new Promise(done => { resolve = done; }));
  const { wrapper, session } = render(); await wrapper.setProps({ modelValue: true });
  const signal = api.listReportingDefinitionTenantGrants.mock.calls[0]![2] as AbortSignal;
  session.currentUser!.tenantId = 'tenant'; await flushPromises(); expect(signal.aborted).toBe(true);
  resolve(page([grant])); await flushPromises(); expect(wrapper.find('section').exists()).toBe(false);
});
