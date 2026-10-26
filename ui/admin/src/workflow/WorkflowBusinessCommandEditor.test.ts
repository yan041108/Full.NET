import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ElSelect } from 'element-plus';
import { listWorkflowBusinessActions, type WorkflowBusinessAction } from '../api/workflow-business-actions';
import { useSessionStore } from '../auth/session';
import WorkflowBusinessCommandEditor from './WorkflowBusinessCommandEditor.vue';
import WorkflowVue3Designer from './WorkflowVue3Designer.vue';
vi.mock('../api/workflow-business-actions', () => ({ listWorkflowBusinessActions: vi.fn() }));
const action: WorkflowBusinessAction = { ownerKey: 'data_approval', actionKey: 'data_approval.apply_serial_rule_update', actionVersion: 1,
  businessType: 'data_approval.serial_rule.update', placementKey: 'root_finalizer', actionHash: 'a'.repeat(64) };
const config = { businessCommandPolicy: { actionKey: action.actionKey, actionVersion: 1 }, boundBusinessActionHash: 'a'.repeat(64) };
beforeEach(() => { vi.clearAllMocks(); vi.mocked(listWorkflowBusinessActions).mockResolvedValue([action]); });
describe('业务操作抽屉', () => {
  it('保存真实目录动作，重新编辑时丢弃旧的服务器绑定', async () => {
    const wrapper = mount(WorkflowBusinessCommandEditor, { props: { modelValue: config, disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); await wrapper.get('[data-testid="business-command-save"]').trigger('click');
      expect(wrapper.emitted('save')?.[0]?.[0]).toEqual({ businessCommandPolicy: config.businessCommandPolicy });
      await wrapper.setProps({ disabled: true }); expect(wrapper.get('[data-testid="business-command-save"]').attributes('disabled')).toBeDefined();
    } finally { wrapper.unmount(); }
  });
  it('读取失败和不存在的版本均禁止提交', async () => {
    vi.mocked(listWorkflowBusinessActions).mockRejectedValueOnce(new Error('unavailable'));
    const wrapper = mount(WorkflowBusinessCommandEditor, { props: { modelValue: config, disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); expect(wrapper.get('[data-testid="business-command-save"]').attributes('disabled')).toBeDefined(); expect(wrapper.find('[role="alert"]').exists()).toBe(true); }
    finally { wrapper.unmount(); }
    const absent = mount(WorkflowBusinessCommandEditor, { props: { modelValue: { businessCommandPolicy: { ...config.businessCommandPolicy, actionVersion: 2 } }, disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); expect(absent.get('[data-testid="business-command-save"]').attributes('disabled')).toBeDefined(); } finally { absent.unmount(); }
  });
  it('身份切换丢弃旧响应，租户空目录不能保存原Host动作', async () => {
    let resolveOld!: (value: WorkflowBusinessAction[]) => void; vi.mocked(listWorkflowBusinessActions).mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }));
    const pinia = createPinia(); const wrapper = mount(WorkflowBusinessCommandEditor, { props: { modelValue: config, disabled: false }, global: { plugins: [pinia] } });
    const session = useSessionStore(pinia);
    try { session.switching = true; await flushPromises(); resolveOld([action]); await flushPromises(); expect(wrapper.findAllComponents(ElSelect)[0]!.props('disabled')).toBe(true);
      vi.mocked(listWorkflowBusinessActions).mockResolvedValueOnce([]); session.switching = false; await flushPromises();
      expect(wrapper.get('[data-testid="business-command-save"]').attributes('disabled')).toBeDefined(); expect(wrapper.find('[role="status"]').exists()).toBe(true);
    } finally { wrapper.unmount(); }
  });
  it('真实节点打开专用抽屉并保存回树', async () => {
    const root = { id: 'start', type: 0, childNode: { id: 'review', type: 1, childNode: { id: 'apply', type: 23, ...config, childNode: null } } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue: root, disabled: false, enabledNodeTypes: ['start', 'human.approval', 'business.command', 'end'] }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); const node = wrapper.findAll('.node-wrap-box').find(item => item.text().includes(action.actionKey)); expect(node).toBeDefined();
      await node!.trigger('click'); await flushPromises(); const editor = wrapper.findComponent(WorkflowBusinessCommandEditor); expect(editor.exists()).toBe(true);
      await editor.get('[data-testid="business-command-save"]').trigger('click'); await flushPromises();
      const values = wrapper.emitted('update:modelValue'); expect(values?.length).toBeGreaterThan(0);
      const saved = values!.at(-1)![0] as typeof root; expect(saved.childNode.childNode.businessCommandPolicy).toEqual(config.businessCommandPolicy);
      expect('boundBusinessActionHash' in saved.childNode.childNode).toBe(false);
    } finally { wrapper.unmount(); }
  });
});
