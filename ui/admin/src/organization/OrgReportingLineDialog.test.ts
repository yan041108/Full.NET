import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import { getOrganizationReportingLine, listOrganizationReportingCandidates, setOrganizationReportingLine } from '../api/org-reporting-lines';
import OrgReportingLineDialog from './OrgReportingLineDialog.vue';
vi.mock('../api/org-reporting-lines', () => ({ getOrganizationReportingLine: vi.fn(), listOrganizationReportingCandidates: vi.fn(), setOrganizationReportingLine: vi.fn() }));
const employee = '01912345-6789-7abc-8def-0123456789ab'; const manager = '01912345-6789-7abc-8def-0123456789ac';
const value = { employeeUserId: employee, managerUserId: manager, revision: 3, updatedById: employee, updatedAtUtc: '2026-10-25T00:00:00Z', managerDisplayName: 'Manager' };
const Dialog = defineComponent({ props: ['open', 'showConfirm'], emits: ['confirm'], template: '<section v-if="open"><slot/><button v-if="showConfirm" data-testid="save" @click="$emit(\'confirm\')">Save</button></section>' });
function setup(permissions = ['organization.reporting_lines.read', 'organization.reporting_lines.update']) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  session.currentUser = { id: employee, username: 'actor', displayName: 'Actor', tenantId: manager, actorScope: 'tenant', scope: 'tenant', isSuperAdministrator: false, passwordChangeRequired: false,
    permissions, sessionId: employee, preferredLocale: 'zh-CN', profileVersion: 1 };
  return { wrapper: mount(OrgReportingLineDialog, { props: { employeeUserId: employee, employeeName: 'Employee' }, global: { plugins: [pinia], stubs: { ArtFormDialog: Dialog } } }), session };
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(getOrganizationReportingLine).mockResolvedValue(value);
  vi.mocked(listOrganizationReportingCandidates).mockResolvedValue({ items: [{ id: manager, username: 'manager', displayName: 'Manager' }], page: 1, pageSize: 20, total: 1 });
  vi.mocked(setOrganizationReportingLine).mockResolvedValue({ ...value, revision: 4 }); });
describe('直属上级配置', () => {
  it('没有精确读取权限不查询；只读身份不加载候选', async () => {
    const denied = setup([]); await flushPromises(); expect(getOrganizationReportingLine).not.toHaveBeenCalled(); denied.wrapper.unmount();
    const read = setup(['organization.reporting_lines.read']); await flushPromises(); expect(listOrganizationReportingCandidates).not.toHaveBeenCalled(); expect(read.wrapper.find('[data-testid="save"]').exists()).toBe(false); read.wrapper.unmount();
  });
  it('只提交当前配置的直属上级和修订', async () => {
    const { wrapper } = setup(); await flushPromises(); await wrapper.get('[data-testid="save"]').trigger('click'); await flushPromises();
    expect(setOrganizationReportingLine).toHaveBeenCalledWith(employee, manager, 3, expect.any(AbortSignal)); expect(wrapper.emitted('saved')).toHaveLength(1); wrapper.unmount();
  });
  it('服务器未知写结果必须刷新，切换身份丢弃迟到结果', async () => {
    vi.mocked(setOrganizationReportingLine).mockRejectedValueOnce({ type: 'about:blank', status: 500, title: 'Unknown' });
    const { wrapper, session } = setup(); await flushPromises(); await wrapper.get('[data-testid="save"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="save"]').exists()).toBe(false);
    let resolve!: (result: typeof value) => void; vi.mocked(getOrganizationReportingLine).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    await wrapper.get('[data-testid="org-reporting-refresh"]').trigger('click'); session.currentUser = { ...session.currentUser!, tenantId: employee }; resolve(value); await flushPromises();
    expect(wrapper.emitted('close')).toHaveLength(1); expect(wrapper.find('[data-testid="save"]').exists()).toBe(false); wrapper.unmount();
  });
});
