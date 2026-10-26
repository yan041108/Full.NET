import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent } from 'vue';
import { useSessionStore } from '../auth/session';
import { getOrganizationUnitLeadership, listOrganizationUnitLeaderCandidates, setOrganizationUnitLeadership } from '../api/org-unit-leadership';
import OrgUnitLeadershipDialog from './OrgUnitLeadershipDialog.vue';
vi.mock('../api/org-unit-leadership', () => ({ getOrganizationUnitLeadership: vi.fn(), listOrganizationUnitLeaderCandidates: vi.fn(), setOrganizationUnitLeadership: vi.fn() }));
const unit = '01912345-6789-7abc-8def-0123456789ab'; const actor = '01912345-6789-7abc-8def-0123456789ac';
const state = { unitId: unit, unitVersion: 3, isUnitActive: true, leaderUserId: actor, revision: 7, updatedById: actor, updatedAtUtc: '2026-10-25T00:00:00Z', leaderDisplayName: 'Actor' };
const Dialog = defineComponent({ props: ['open', 'showConfirm'], emits: ['confirm'], template: '<section v-if="open"><slot/><button v-if="showConfirm" data-testid="save" @click="$emit(\'confirm\')">Save</button></section>' });
function setup(permissions = ['organization.unit_leadership.read', 'organization.unit_leadership.update']) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  session.currentUser = { id: actor, username: 'actor', displayName: 'Actor', tenantId: unit, actorScope: 'tenant', scope: 'tenant', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions, sessionId: actor, preferredLocale: 'zh-CN', profileVersion: 1 };
  return { wrapper: mount(OrgUnitLeadershipDialog, { props: { unitId: unit, unitName: 'Unit' }, global: { plugins: [pinia], stubs: { ArtFormDialog: Dialog } } }), session };
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(getOrganizationUnitLeadership).mockResolvedValue(state);
  vi.mocked(listOrganizationUnitLeaderCandidates).mockResolvedValue({ items: [{ id: actor, username: 'actor', displayName: 'Actor' }], page: 1, pageSize: 20, total: 1 });
  vi.mocked(setOrganizationUnitLeadership).mockResolvedValue({ ...state, revision: 8 }); });
describe('机构负责人配置', () => {
  it('没有精确读取权限不请求；只读权限不读取候选或暴露保存', async () => {
    const denied = setup([]); await flushPromises(); expect(getOrganizationUnitLeadership).not.toHaveBeenCalled(); denied.wrapper.unmount();
    const read = setup(['organization.unit_leadership.read']); await flushPromises(); expect(getOrganizationUnitLeadership).toHaveBeenCalledOnce();
    expect(listOrganizationUnitLeaderCandidates).not.toHaveBeenCalled(); expect(read.wrapper.find('[data-testid="save"]').exists()).toBe(false); read.wrapper.unmount();
  });
  it('保存使用权威读取的两个版本且成功后关闭', async () => {
    const { wrapper } = setup(); await flushPromises(); await wrapper.get('[data-testid="save"]').trigger('click'); await flushPromises();
    expect(setOrganizationUnitLeadership).toHaveBeenCalledWith(unit, { leaderUserId: actor, expectedUnitVersion: 3, expectedRevision: 7 }, expect.any(AbortSignal));
    expect(wrapper.emitted('saved')).toHaveLength(1); expect(wrapper.emitted('close')).toHaveLength(1); wrapper.unmount();
  });
  it.each([new TypeError('network'), { type: 'about:blank', title: 'Server error', status: 500 }])('未知写结果阻止复用旧修订，刷新后才允许下一次写入 %j', async failure => {
    vi.mocked(setOrganizationUnitLeadership).mockRejectedValueOnce(failure);
    const { wrapper } = setup(); await flushPromises(); await wrapper.get('[data-testid="save"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="save"]').exists()).toBe(false);
    await wrapper.get('[data-testid="org-unit-leadership-refresh"]').trigger('click'); await flushPromises();
    expect(getOrganizationUnitLeadership).toHaveBeenCalledTimes(2); expect(wrapper.find('[data-testid="save"]').exists()).toBe(true); wrapper.unmount();
  });
  it('身份切换关闭并丢弃迟到配置，不能展示前一个租户的数据', async () => {
    let resolve!: (value: typeof state) => void;
    vi.mocked(getOrganizationUnitLeadership).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const { wrapper, session } = setup(); session.currentUser = { ...session.currentUser!, tenantId: actor };
    resolve(state); await flushPromises(); expect(wrapper.emitted('close')).toHaveLength(1); expect(listOrganizationUnitLeaderCandidates).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="save"]').exists()).toBe(false); wrapper.unmount();
  });
});
