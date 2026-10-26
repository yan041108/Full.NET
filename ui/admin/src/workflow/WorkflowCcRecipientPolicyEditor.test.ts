import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElPagination, ElSelect } from 'element-plus';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import { listWorkflowRecipientCandidates } from '../api/workflow-definitions';
import WorkflowCcRecipientPolicyEditor from './WorkflowCcRecipientPolicyEditor.vue';
import { fromWorkflowVue3Tree, readCcRecipientPolicy, toWorkflowVue3Tree } from './workflow-vue3-adapter';
vi.mock('../api/workflow-definitions', () => ({ listWorkflowRecipientCandidates: vi.fn(), listWorkflowRoleCandidates: vi.fn(), listWorkflowOrganizationUnitCandidates: vi.fn(), listWorkflowPositionCandidates: vi.fn() }));
const first = '01912345-6789-7abc-8def-0123456789ab'; const second = '01912345-6789-7abc-8def-0123456789ac';
const candidate = { id: first, username: 'first', displayName: 'First' };
beforeEach(() => { vi.clearAllMocks(); vi.mocked(listWorkflowRecipientCandidates).mockResolvedValue({ items: [candidate], page: 1, pageSize: 20, total: 21 }); });
describe('动态抄送收件来源', () => {
  it('策略往返保留且拒绝混用静态名单和人工无人策略', () => {
    const recipientPolicy = { sources: [{ resolverKindKey: 'flow_administrators' }] };
    const tree = { id: 'start', type: 0, workflowKindKey: 'automation', administratorUserIds: [first], childNode: { id: 'cc', type: 2, recipientPolicy, childNode: null } };
    expect(toWorkflowVue3Tree(fromWorkflowVue3Tree(tree))).toMatchObject(tree);
    expect(() => fromWorkflowVue3Tree({ ...tree, childNode: { ...tree.childNode, recipientUserIds: [first] } })).toThrow();
    expect(() => readCcRecipientPolicy({ ...recipientPolicy, handling: { whenEmpty: { actionKey: 'auto_pass' } } })).toThrow();
    expect(() => readCcRecipientPolicy({ sources: [{ resolverKindKey: 'initiator', tenantId: second }] })).toThrow();
  });
  it('真实分页保留屏外选择并保存完整来源', async () => {
    const pinia = createPinia(); vi.mocked(listWorkflowRecipientCandidates).mockResolvedValueOnce({ items: [candidate], page: 1, pageSize: 20, total: 21 })
      .mockResolvedValueOnce({ items: [{ id: second, username: 'second', displayName: 'Second' }], page: 2, pageSize: 20, total: 21 });
    const wrapper = mount(WorkflowCcRecipientPolicyEditor, { props: { modelValue: { sources: [{ resolverKindKey: 'specified_users', userIds: [first] }] }, fields: [], previousNodes: [], disabled: false }, global: { plugins: [pinia] } });
    await flushPromises(); wrapper.findComponent(ElPagination).vm.$emit('current-change', 2); await flushPromises();
    expect(listWorkflowRecipientCandidates).toHaveBeenLastCalledWith(2, 20, expect.any(AbortSignal));
    const picker = wrapper.findAllComponents(ElSelect)[1]!; expect(picker.props('modelValue')).toEqual([first]);
    picker.vm.$emit('update:modelValue', [first, second]); await flushPromises(); await wrapper.get('[data-testid="workflow-cc-policy-save"]').trigger('click');
    expect(wrapper.emitted('save')?.[0]).toEqual([{ sources: [{ resolverKindKey: 'specified_users', userIds: [first, second] }] }]); wrapper.unmount();
  });
  it('不存在的前序绑定不能保存，身份变化丢弃旧候选响应', async () => {
    const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
    session.currentUser = { id: first, username: 'first', displayName: 'First', tenantId: null, scope: 'host', actorScope: 'host', isSuperAdministrator: false,
      passwordChangeRequired: false, permissions: ['workflow.definitions.update'], sessionId: first, preferredLocale: 'zh-CN', profileVersion: 1 };
    const invalid = mount(WorkflowCcRecipientPolicyEditor, { props: { modelValue: { sources: [{ resolverKindKey: 'previous_node_actors', nodeKey: 'missing' }] }, fields: [], previousNodes: [], disabled: false }, global: { plugins: [pinia] } });
    await invalid.get('[data-testid="workflow-cc-policy-save"]').trigger('click'); expect(invalid.emitted('save')).toBeUndefined(); invalid.unmount();
    let resolve!: (value: Awaited<ReturnType<typeof listWorkflowRecipientCandidates>>) => void;
    vi.mocked(listWorkflowRecipientCandidates).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mount(WorkflowCcRecipientPolicyEditor, { props: { modelValue: { sources: [{ resolverKindKey: 'specified_users', userIds: [first] }] }, fields: [], previousNodes: [], disabled: false }, global: { plugins: [pinia] } });
    session.currentUser = { ...session.currentUser, tenantId: second }; resolve({ items: [candidate], page: 1, pageSize: 20, total: 1 }); await flushPromises();
    expect(wrapper.emitted('cancel')).toHaveLength(1); expect(wrapper.find('[data-testid="workflow-cc-policy-save"]').exists()).toBe(false); wrapper.unmount();
  });
});
