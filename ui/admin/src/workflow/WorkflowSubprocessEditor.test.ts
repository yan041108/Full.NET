import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ElOption, ElSelect } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { getSubprocessAsset, listSubprocessDefinitions, listSubprocessVersions, type SubprocessAsset } from '../api/workflow-subprocesses';
import { useSessionStore } from '../auth/session';
import WorkflowSubprocessEditor from './WorkflowSubprocessEditor.vue';
import WorkflowVue3Designer from './WorkflowVue3Designer.vue';
vi.mock('../api/workflow-subprocesses', () => ({ getSubprocessAsset: vi.fn(), listSubprocessDefinitions: vi.fn(), listSubprocessVersions: vi.fn() }));
const version = '11111111-1111-4111-8111-111111111111'; const definition = '22222222-2222-4222-8222-222222222222';
const field = (fieldKey: string): WorkflowFormField => ({ fieldKey, fieldTypeKey: 'text', required: false, constraints: {} });
const fields = [field('title'), field('result')]; const child: SubprocessAsset = { definitionId: definition, versionId: version, fields: [field('subject'), field('answer')] };
const config = { subprocessPolicy: { definitionVersionId: version, inputFields: { subject: 'title' }, outputFields: { result: 'answer' } }, fieldPolicies: { title: 'readOnly', result: 'editable' } };
beforeEach(() => {
  vi.clearAllMocks(); vi.mocked(getSubprocessAsset).mockResolvedValue(child);
  vi.mocked(listSubprocessDefinitions).mockResolvedValue([{ id: definition, definitionKey: 'child', latestPublishedVersionId: version, statusKey: 'active' } as Awaited<ReturnType<typeof listSubprocessDefinitions>>[number]]);
  vi.mocked(listSubprocessVersions).mockResolvedValue([{ id: version, versionNumber: 1 } as Awaited<ReturnType<typeof listSubprocessVersions>>[number]]);
});
describe('子流程配置抽屉', () => {
  it('读取真实版本并保存明确映射，禁用时不可提交', async () => {
    const wrapper = mount(WorkflowSubprocessEditor, { props: { modelValue: config, fields, disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); await wrapper.get('[data-testid="subprocess-save"]').trigger('click'); expect(wrapper.emitted('save')?.[0]?.[0]).toEqual(config);
      await wrapper.setProps({ disabled: true }); expect(wrapper.get('[data-testid="subprocess-save"]').attributes('disabled')).toBeDefined();
    } finally { wrapper.unmount(); }
  });
  it('真实选择并行集合后保存明确门槛及归约，切回单项移除多实例策略', async () => {
    const wrapper = mount(WorkflowSubprocessEditor, { props: { modelValue: config, fields, disabled: false }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises();
      wrapper.findAllComponents(ElSelect).find(select => select.attributes('data-testid') === 'subprocess-multi-mode')!.vm.$emit('update:modelValue', 'parallel');
      await flushPromises(); expect(wrapper.get('[data-testid="subprocess-save"]').attributes('disabled')).toBeUndefined();
      await wrapper.get('[data-testid="subprocess-save"]').trigger('click');
      expect(wrapper.emitted('save')?.at(-1)?.[0]).toEqual({ ...config, multiInstancePolicy: { modeKey: 'parallel', source: { kindKey: 'fixed_count', count: 2 }, requiredPercentage: 100, emptyModeKey: 'reject', outputReducers: { result: 'first' } } });
      wrapper.findAllComponents(ElSelect).find(select => select.attributes('data-testid') === 'subprocess-multi-mode')!.vm.$emit('update:modelValue', 'single');
      await flushPromises(); await wrapper.get('[data-testid="subprocess-save"]').trigger('click'); expect(wrapper.emitted('save')?.at(-1)?.[0]).toEqual(config);
    } finally { wrapper.unmount(); }
  });
  it('无法读取资产或定义已停用时拒绝保存', async () => {
    vi.mocked(getSubprocessAsset).mockRejectedValueOnce(new Error('denied'));
    const wrapper = mount(WorkflowSubprocessEditor, { props: { modelValue: config, fields, disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); expect(wrapper.find('[role="alert"]').exists()).toBe(true); expect(wrapper.get('[data-testid="subprocess-save"]').attributes('disabled')).toBeDefined(); }
    finally { wrapper.unmount(); }
    vi.mocked(listSubprocessDefinitions).mockResolvedValueOnce([]);
    const missing = mount(WorkflowSubprocessEditor, { props: { modelValue: config, fields, disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); expect(missing.get('[data-testid="subprocess-save"]').attributes('disabled')).toBeDefined(); } finally { missing.unmount(); }
  });
  it('身份切换使迟到的子资产失效', async () => {
    let resolve!: (value: SubprocessAsset) => void;
    vi.mocked(getSubprocessAsset).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const pinia = createPinia(); const wrapper = mount(WorkflowSubprocessEditor, { props: { modelValue: config, fields, disabled: false }, global: { plugins: [pinia] } });
    try { await flushPromises(); const session = useSessionStore(pinia); session.switching = true; await flushPromises(); resolve(child); await flushPromises();
      expect(wrapper.get('[data-testid="subprocess-save"]').attributes('disabled')).toBeDefined();
      vi.mocked(listSubprocessDefinitions).mockResolvedValueOnce([]); session.switching = false; await flushPromises();
      expect(wrapper.get('[data-testid="subprocess-save"]').attributes('disabled')).toBeDefined();
    } finally { wrapper.unmount(); }
  });
  it('身份切换后迟到的定义目录不能重新出现', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof listSubprocessDefinitions>>) => void;
    vi.mocked(listSubprocessDefinitions).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const pinia = createPinia(); const wrapper = mount(WorkflowSubprocessEditor, { props: { fields, disabled: false }, global: { plugins: [pinia] } });
    try { const session = useSessionStore(pinia); session.switching = true; await flushPromises();
      resolve([{ id: definition, definitionKey: 'old-host' } as Awaited<ReturnType<typeof listSubprocessDefinitions>>[number]]); await flushPromises();
      expect(wrapper.findAllComponents(ElOption)).toHaveLength(0);
    } finally { wrapper.unmount(); }
  });
  it('真实子流程节点打开专用抽屉并保存回树', async () => {
    const tree = { id: 'start', type: 0, childNode: { id: 'child', type: 9, ...config, childNode: null } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue: tree, gatewayFields: fields, disabled: false, enabledNodeTypes: ['start', 'process.subprocess', 'end'] }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); const node = wrapper.findAll('.node-wrap-box').find(item => item.text().includes(version)); expect(node).toBeDefined();
      await node!.trigger('click'); await flushPromises(); const editor = wrapper.findComponent(WorkflowSubprocessEditor); expect(editor.exists()).toBe(true);
      editor.findAllComponents(ElSelect).find(select => select.props('modelValue') === 'answer')!.vm.$emit('update:modelValue', 'subject'); await flushPromises();
      expect(editor.get('[data-testid="subprocess-save"]').attributes('disabled'), editor.text()).toBeUndefined();
      await editor.get('[data-testid="subprocess-save"]').trigger('click'); await flushPromises();
      expect(editor.emitted('save')).toHaveLength(1);
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      expect(wrapper.emitted('update:modelValue')).toBeDefined();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as typeof tree;
      expect(saved.childNode.subprocessPolicy.outputFields).toEqual({ result: 'subject' });
    } finally { wrapper.unmount(); }
  });
});
