import { flushPromises, mount } from '@vue/test-utils';
import { ElPagination } from 'element-plus';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { listWorkflowTodoDelegationCandidates } from '../api/workflow-delegations';
import WorkflowTodoHandoffPicker from './WorkflowTodoHandoffPicker.vue';
vi.mock('../api/workflow-delegations', () => ({ listWorkflowTodoDelegationCandidates: vi.fn() }));
vi.mock('../api/workflow-todos', () => ({ listWorkflowTodoTransferCandidates: vi.fn() }));

const a = '01912345-6789-7abc-8def-0123456789b1'; const b = '01912345-6789-7abc-8def-0123456789b2';
describe('WorkflowTodoHandoffPicker', () => {
  beforeEach(() => vi.mocked(listWorkflowTodoDelegationCandidates).mockReset());
  it('使用20人分页并保留当前已选项，不能把第二页当作整个候选集', async () => {
    vi.mocked(listWorkflowTodoDelegationCandidates).mockImplementation(async (_id, page = 1) => ({
      items: [{ id: page === 1 ? a : b, username: page === 1 ? 'first' : 'second', displayName: '' }], page, pageSize: 20, total: 21 }));
    const wrapper = mount(WorkflowTodoHandoffPicker, { props: { todoId: a, action: 'delegate', excludedUserId: 'self',
      modelValue: '', disabled: false, testId: 'recipient' } });
    try {
      await flushPromises(); await wrapper.get('select').setValue(a); await wrapper.setProps({ modelValue: a });
      wrapper.getComponent(ElPagination).vm.$emit('current-change', 2); await flushPromises();
      expect(listWorkflowTodoDelegationCandidates).toHaveBeenLastCalledWith(a, 2, 20, expect.any(AbortSignal));
      expect(wrapper.get('select').element.value).toBe(a);
      expect(wrapper.findAll('option').map(option => option.text())).toContain('first');
      expect(wrapper.findAll('option').map(option => option.text())).toContain('second');
    } finally { wrapper.unmount(); }
  });
  it('资源切换清空候选并拒绝旧响应，卸载终止请求', async () => {
    let complete!: (value: Awaited<ReturnType<typeof listWorkflowTodoDelegationCandidates>>) => void;
    vi.mocked(listWorkflowTodoDelegationCandidates).mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }))
      .mockResolvedValueOnce({ items: [{ id: b, username: 'current', displayName: '' }], page: 1, pageSize: 20, total: 1 });
    const wrapper = mount(WorkflowTodoHandoffPicker, { props: { todoId: a, action: 'delegate', excludedUserId: 'self', modelValue: '', disabled: false, testId: 'recipient' } });
    const oldSignal = vi.mocked(listWorkflowTodoDelegationCandidates).mock.calls[0]![3]!;
    await wrapper.setProps({ todoId: b }); await flushPromises();
    complete({ items: [{ id: a, username: 'old-secret', displayName: '' }], page: 1, pageSize: 20, total: 1 }); await flushPromises();
    expect(oldSignal.aborted).toBe(true); expect(wrapper.text()).not.toContain('old-secret'); expect(wrapper.text()).toContain('current');
    const currentSignal = vi.mocked(listWorkflowTodoDelegationCandidates).mock.calls[1]![3]!; wrapper.unmount(); expect(currentSignal.aborted).toBe(true);
  });
});
