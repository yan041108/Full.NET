import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import { getWorkflowStarterSelections, listWorkflowStarterSelectionCandidates } from '../api/workflow-starter-selections';
import WorkflowStarterSelectionPicker from './WorkflowStarterSelectionPicker.vue';
vi.mock('../api/workflow-starter-selections', () => ({ getWorkflowStarterSelections: vi.fn(), listWorkflowStarterSelectionCandidates: vi.fn() }));
const id = '01912345-6789-7abc-8def-0123456789ab';
const user = { id, username: 'user', displayName: 'User' };
const metadata = { definitionVersionId: id, items: [{ selectionKey: 'review', nodeKey: 'review', nodeName: 'Review', modeKey: 'single' as const, maximumUsers: 1, rangeKey: 'role_members' as const }] };
function setup(permissions = ['workflow.instances.start']) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  session.currentUser = { id, username: 'starter', displayName: 'Starter', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, permissions, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(WorkflowStarterSelectionPicker, { props: { versionId: id, modelValue: {} }, global: { plugins: [pinia] } });
  return { wrapper, session };
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(getWorkflowStarterSelections).mockResolvedValue(metadata);
  vi.mocked(listWorkflowStarterSelectionCandidates).mockResolvedValue({ items: [user], page: 1, pageSize: 20, total: 1 }); });
describe('发起人真实选择', () => {
  it('只具有定义权限不加载启动候选', async () => {
    const { wrapper } = setup(['workflow.definitions.read']); await flushPromises(); expect(getWorkflowStarterSelections).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('从真实候选选择后才允许启动，不把候选默认为已选成员', async () => {
    const { wrapper } = setup(); await flushPromises();
    expect(wrapper.emitted('ready')?.at(-1)).toEqual([false]);
    await wrapper.get(`[data-testid="workflow-starter-candidate-${id}"]`).trigger('click'); await flushPromises();
    await wrapper.setProps({ modelValue: { review: [id] } }); await flushPromises();
    expect(wrapper.props('modelValue')).toEqual({ review: [id] }); expect(wrapper.emitted('ready')?.at(-1)).toEqual([true]);
    expect(wrapper.text()).not.toContain('selection-'); wrapper.unmount();
  });
  it('切换身份清除选择，迟到的旧候选不能重新显示', async () => {
    let resolve!: (value: { items: typeof user[]; page: number; pageSize: number; total: number }) => void;
    vi.mocked(listWorkflowStarterSelectionCandidates).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const { wrapper, session } = setup(); await flushPromises(); session.currentUser = { ...session.currentUser!, permissions: [] };
    resolve({ items: [user], page: 1, pageSize: 20, total: 1 }); await flushPromises();
    expect(wrapper.find(`[data-testid="workflow-starter-candidate-${id}"]`).exists()).toBe(false); expect(wrapper.props('modelValue')).toEqual({}); wrapper.unmount();
  });
  it('稳定选择键与对象原型同名时不读成已有成员', async () => {
    vi.mocked(getWorkflowStarterSelections).mockResolvedValue({ ...metadata, items: [{ ...metadata.items[0]!, selectionKey: 'constructor' }] });
    const { wrapper } = setup(); await flushPromises();
    expect(wrapper.emitted('ready')?.at(-1)).toEqual([false]);
    await wrapper.get(`[data-testid="workflow-starter-candidate-${id}"]`).trigger('click'); await flushPromises();
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([{ constructor: [id] }]); wrapper.unmount();
  });
});
