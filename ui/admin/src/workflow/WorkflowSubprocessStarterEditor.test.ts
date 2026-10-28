import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ElOption, ElSelect } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { listWorkflowRecipientCandidates } from '../api/workflow-definitions';
import { useSessionStore } from '../auth/session';
import WorkflowSubprocessStarterEditor from './WorkflowSubprocessStarterEditor.vue';
vi.mock('../api/workflow-definitions', () => ({ listWorkflowRecipientCandidates: vi.fn() }));
const user = '11111111-1111-4111-8111-111111111111';
const fields: WorkflowFormField[] = [{ fieldKey: 'actor', fieldTypeKey: 'select', required: false, constraints: { options: [user] } }, { fieldKey: 'result', fieldTypeKey: 'text', required: false, constraints: {} }];
const policy = { source: { kindKey: 'specified_user', userId: user }, resultFieldKey: 'result' };
beforeEach(() => vi.clearAllMocks());
describe('子发起人编辑器', () => {
  it('多项回填只提供规范UUID选项字段，拒绝显示文字作为身份值', async () => {
    const wrapper = mount(WorkflowSubprocessStarterEditor, { props: {
      modelValue: { source: { kindKey: 'parent' }, resultFieldKey: 'labels' },
      fields: [{ fieldKey: 'actors', fieldTypeKey: 'checkbox', required: false, constraints: { options: [user] } },
        { fieldKey: 'labels', fieldTypeKey: 'checkbox', required: false, constraints: { options: ['A display label'] } }],
      outputKeys: [], multiple: true, disabled: false, multi: { modeKey: 'parallel', source: { kindKey: 'fixed_count', count: 2 }, requiredPercentage: 100, emptyModeKey: 'reject', outputReducers: {} }
    }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); const values = wrapper.findAllComponents(ElOption).map(option => option.props('value'));
      expect(values).toContain('actors'); expect(values).not.toContain('labels'); expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toBeNull();
    } finally { wrapper.unmount(); }
  });
  it('指定用户来自真实分页候选并保留回填，切回默认可去掉委托', async () => {
    vi.mocked(listWorkflowRecipientCandidates).mockResolvedValueOnce({ items: [{ id: user, username: 'user', displayName: 'Receiver' }], total: 1, page: 1, pageSize: 50 });
    const wrapper = mount(WorkflowSubprocessStarterEditor, { props: { modelValue: policy, fields, outputKeys: [], multiple: false, disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); expect(wrapper.text()).toContain('Receiver');
      wrapper.findAllComponents(ElSelect).find(select => select.attributes('data-testid') === 'subprocess-starter-result')!.vm.$emit('update:modelValue', ''); await flushPromises();
      expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toEqual({ source: { kindKey: 'specified_user', userId: user } });
      wrapper.findAllComponents(ElSelect).find(select => select.attributes('data-testid') === 'subprocess-starter-source')!.vm.$emit('update:modelValue', 'parent'); await flushPromises();
      expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toBeUndefined();
    } finally { wrapper.unmount(); }
  });
  it('切换身份丢弃迟到候选，角色项来源只能用于角色集合', async () => {
    let resolve!: (page: Awaited<ReturnType<typeof listWorkflowRecipientCandidates>>) => void;
    vi.mocked(listWorkflowRecipientCandidates).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const pinia = createPinia(); const wrapper = mount(WorkflowSubprocessStarterEditor, { props: { modelValue: policy, fields, outputKeys: [], multiple: false, disabled: false }, global: { plugins: [pinia] } });
    try { await flushPromises(); useSessionStore(pinia).switching = true; await flushPromises(); resolve({ items: [{ id: user, username: 'old', displayName: 'old-host' }], total: 1, page: 1, pageSize: 50 }); await flushPromises();
      expect(wrapper.text()).not.toContain('old-host'); expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toBeNull();
    } finally { wrapper.unmount(); }
  });
});
