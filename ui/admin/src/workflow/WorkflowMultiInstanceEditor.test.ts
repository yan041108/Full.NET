import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ElInputNumber } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { listWorkflowRoleCandidates } from '../api/workflow-definitions';
import { useSessionStore } from '../auth/session';
import WorkflowMultiInstanceEditor from './WorkflowMultiInstanceEditor.vue';
vi.mock('../api/workflow-definitions', () => ({ listWorkflowRoleCandidates: vi.fn() }));
const role = '11111111-1111-4111-8111-111111111111';
const field: WorkflowFormField = { fieldKey: 'member', fieldTypeKey: 'text', required: false, constraints: {} };
const policy = { modeKey: 'serial', source: { kindKey: 'role_members', roleId: role, itemFieldKey: 'member', maximumCount: 2 }, requiredPercentage: 100, emptyModeKey: 'reject', outputReducers: {} };
beforeEach(() => vi.clearAllMocks());
describe('多实例来源编辑器', () => {
  it('来源角色必须来自真实当前候选，目录失败不能保存', async () => {
    vi.mocked(listWorkflowRoleCandidates).mockResolvedValueOnce({ items: [{ id: role, code: 'team', name: 'team' }], page: 1, pageSize: 50, total: 1 });
    const wrapper = mount(WorkflowMultiInstanceEditor, { props: { modelValue: policy, fields: [], childFields: [field], outputKeys: [], disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); expect(listWorkflowRoleCandidates).toHaveBeenCalledWith(1, 50, expect.any(AbortSignal));
      expect(wrapper.text()).toContain('team');
      wrapper.findAllComponents(ElInputNumber).find(input => input.props('modelValue') === 100)!.vm.$emit('update:modelValue', 99); await flushPromises();
      expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toEqual({ ...policy, requiredPercentage: 99 });
    } finally { wrapper.unmount(); }
    vi.mocked(listWorkflowRoleCandidates).mockRejectedValueOnce(new Error('denied'));
    const denied = mount(WorkflowMultiInstanceEditor, { props: { modelValue: policy, fields: [], childFields: [field], outputKeys: [], disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); expect(denied.find('[role="alert"]').exists()).toBe(true); expect(denied.emitted('update:modelValue')?.at(-1)?.[0]).toBeNull(); }
    finally { denied.unmount(); }
  });
  it('切换身份会丢弃迟到角色页，不复用旧作用域候选', async () => {
    let resolve!: (page: Awaited<ReturnType<typeof listWorkflowRoleCandidates>>) => void;
    vi.mocked(listWorkflowRoleCandidates).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const pinia = createPinia(); const wrapper = mount(WorkflowMultiInstanceEditor, { props: { modelValue: policy, fields: [], childFields: [field], outputKeys: [], disabled: false }, global: { plugins: [pinia] } });
    try { await flushPromises(); useSessionStore(pinia).switching = true; await flushPromises();
      resolve({ items: [{ id: role, code: 'old', name: 'old-host' }], page: 1, pageSize: 50, total: 1 }); await flushPromises();
      expect(wrapper.text()).not.toContain('old-host'); expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toBeNull();
    } finally { wrapper.unmount(); }
  });
});
