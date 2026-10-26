import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElPagination, ElSelect } from 'element-plus';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import { listWorkflowRecipientCandidates } from '../api/workflow-definitions';
import WorkflowFlowAdministratorEditor from './WorkflowFlowAdministratorEditor.vue';
import { fromWorkflowVue3Tree, readAssigneePolicy, readFlowAdministratorUserIds, toWorkflowVue3Tree } from './workflow-vue3-adapter';
vi.mock('../api/workflow-definitions', () => ({ listWorkflowRecipientCandidates: vi.fn() }));
const first = '01912345-6789-7abc-8def-0123456789ab'; const second = '01912345-6789-7abc-8def-0123456789ac';
const candidate = { id: first, username: 'first', displayName: 'First' };
beforeEach(() => { vi.clearAllMocks(); vi.mocked(listWorkflowRecipientCandidates).mockResolvedValue({ items: [candidate], page: 1, pageSize: 20, total: 21 }); });
describe('固定版本流程管理员', () => {
  it('开始名单往返保留，节点不能覆盖全局名单或传入任意身份参数', () => {
    const root = { id: 'start', type: 0, workflowKindKey: 'automation', administratorUserIds: [first.toUpperCase()], childNode: {
      id: 'review', type: 1, assigneePolicy: { sources: [{ resolverKindKey: 'flow_administrators' }] }, childNode: null } };
    const draft = fromWorkflowVue3Tree(root); const restored = toWorkflowVue3Tree(draft);
    expect(restored.administratorUserIds).toEqual([first]); expect(restored.childNode?.assigneePolicy).toEqual(root.childNode.assigneePolicy);
    expect(readFlowAdministratorUserIds([])).toEqual([]);
    expect(() => readFlowAdministratorUserIds([first, first.toUpperCase()])).toThrow();
    expect(() => readFlowAdministratorUserIds(['00000000-0000-0000-0000-000000000000'])).toThrow();
    expect(() => readAssigneePolicy({ sources: [{ resolverKindKey: 'flow_administrators', userIds: [second] }] })).toThrow();
  });
  it('真实翻页不丢失原名单或把首屏当成全部可选范围', async () => {
    const pinia = createPinia(); vi.mocked(listWorkflowRecipientCandidates).mockResolvedValueOnce({ items: [candidate], page: 1, pageSize: 20, total: 21 })
      .mockResolvedValueOnce({ items: [{ id: second, username: 'second', displayName: 'Second' }], page: 2, pageSize: 20, total: 21 });
    const wrapper = mount(WorkflowFlowAdministratorEditor, { props: { userIds: [first], disabled: false }, global: { plugins: [pinia] } });
    await flushPromises(); wrapper.findComponent(ElPagination).vm.$emit('current-change', 2); await flushPromises();
    expect(listWorkflowRecipientCandidates).toHaveBeenLastCalledWith(2, 20, expect.any(AbortSignal));
    expect(wrapper.findComponent(ElSelect).props('modelValue')).toEqual([first]);
    wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', [first, second]); await flushPromises();
    await wrapper.get('[data-testid="workflow-flow-administrators-save"]').trigger('click'); expect(wrapper.emitted('save')?.[0]).toEqual([[first, second]]); wrapper.unmount();
  });
  it('身份切换关闭编辑并忽略旧候选响应', async () => {
    const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
    session.currentUser = { id: first, username: 'first', displayName: 'First', tenantId: null, scope: 'host', actorScope: 'host', isSuperAdministrator: false,
      passwordChangeRequired: false, permissions: ['workflow.definitions.update'], sessionId: first, preferredLocale: 'zh-CN', profileVersion: 1 };
    let resolve!: (value: Awaited<ReturnType<typeof listWorkflowRecipientCandidates>>) => void;
    vi.mocked(listWorkflowRecipientCandidates).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mount(WorkflowFlowAdministratorEditor, { props: { userIds: [first], disabled: false }, global: { plugins: [pinia] } });
    session.currentUser = { ...session.currentUser, tenantId: second }; resolve({ items: [candidate], page: 1, pageSize: 20, total: 1 }); await flushPromises();
    expect(wrapper.emitted('cancel')).toHaveLength(1); expect(wrapper.find('[data-testid="workflow-flow-administrators-save"]').exists()).toBe(false); expect(wrapper.emitted('save')).toBeUndefined(); wrapper.unmount();
  });
});
