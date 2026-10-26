// 两端分别执行严格类型检查；此ESM用例只组合验证uni-app原生控件语义与页面状态。
import { flushPromises, mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import { HttpProblem } from '../../../../clients/uniapp/src/api/problem-details';
import WorkflowDelegationPanel from '../../../../clients/uniapp/src/features/workflow/WorkflowDelegationPanel.vue';

vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key) => key }) }));
const origin = '01912345-6789-7abc-8def-0123456789a1'; const proxy = '01912345-6789-7abc-8def-0123456789a2';
const actor = '01912345-6789-7abc-8def-0123456789a3'; const targetA = '01912345-6789-7abc-8def-0123456789a4';
const targetB = '01912345-6789-7abc-8def-0123456789a5';
const detail = { id: origin, assigneeUserId: actor, statusKey: 'active', revision: 3, submissionRevision: 4,
  allowedActions: ['delegate'], signRequired: false, commentRequired: false };
const grant = { originTodoId: origin, proxyTodoId: proxy, originalAssigneeUserId: actor, proxyUserId: targetA,
  statusKey: 'active', proxyRevision: 7, formRevision: 9, canRecall: true, createdAtUtc: '2026-10-25T00:00:00Z' };
const receipt = { id: origin, definitionVersionId: actor, formVersionId: actor, businessType: 'delegation', businessId: 'one',
  businessTitle: null, statusKey: 'active', revision: 8, activeTodoId: proxy, startedAtUtc: '2026-10-25T00:00:00Z' };
function client() {
  return { delegation: vi.fn().mockResolvedValue(null), delegate: vi.fn().mockResolvedValue(receipt), recallDelegation: vi.fn().mockResolvedValue(receipt),
    listDelegationCandidates: vi.fn().mockImplementation(async (_id, page) => ({ page, pageSize: 20, total: 21, items: page === 1
      ? [{ id: actor, username: 'self', displayName: 'Self' }, { id: targetA, username: 'a', displayName: 'Proxy A' }]
      : [{ id: targetB, username: 'b', displayName: 'Proxy B' }] })) };
}
const native = {
  view: { template: '<div><slot /></div>' }, text: { template: '<span><slot /></span>' },
  picker: { props: ['range', 'value', 'disabled'], emits: ['change'], template: `<select :value="value" :disabled="disabled" @change="$emit('change', { detail: { value: $event.target.value } })"><option v-for="(name, index) in range" :key="index" :value="index">{{ name }}</option></select>` },
  'uni-easyinput': { props: ['modelValue', 'disabled'], emits: ['update:modelValue'], template: `<textarea :value="modelValue" :disabled="disabled" @input="$emit('update:modelValue', $event.target.value)" />` }
};
function mountPanel(api, overrides = {}) {
  return mount(WorkflowDelegationPanel, { props: { detail, client: api, mayDelegate: true, mayRecall: false,
    comment: '', unsaved: false, disabled: false, ...overrides }, global: { stubs: native } });
}

describe('uni-app delegation component', () => {
  it('原生选择器使用空占位，首项可选，跨页保留选择并在暂时失败后复用幂等键', async () => {
    const api = client(); vi.mocked(api.delegate).mockRejectedValueOnce(new HttpProblem({ status: 503, code: 'unavailable' })).mockResolvedValueOnce(receipt);
    const wrapper = mountPanel(api);
    try {
      await flushPromises(); const submit = wrapper.get('[data-testid="workflow-delegation-submit"]');
      expect(submit.attributes('disabled')).toBeDefined(); expect(wrapper.findAll('option').some(option => option.text() === 'Self')).toBe(false);
      await wrapper.get('select').setValue('1'); await flushPromises(); expect(submit.attributes('disabled')).toBeUndefined();
      await wrapper.findAll('button').find(button => button.text() === 'workflow.todo.nextPage').trigger('click'); await flushPromises();
      expect(api.listDelegationCandidates).toHaveBeenLastCalledWith(origin, 2);
      expect(wrapper.get('select').element.value).toBe('1'); expect(wrapper.text()).toContain('Proxy A'); expect(wrapper.text()).toContain('Proxy B');
      await submit.trigger('click'); await flushPromises(); await submit.trigger('click'); await flushPromises();
      const calls = vi.mocked(api.delegate).mock.calls; expect(calls).toHaveLength(2);
      expect(calls[0][1]).toEqual(expect.objectContaining({ assigneeUserId: targetA, expectedRevision: 3, expectedFormRevision: 4 }));
      expect(calls[0][1].idempotencyKey).toBe(calls[1][1].idempotencyKey); expect(wrapper.emitted('completed')).toEqual([['delegate']]);
    } finally { wrapper.unmount(); }
  });

  it('资源改变清空状态，并拒绝旧候选的迟到回复', async () => {
    const api = client(); let finish;
    vi.mocked(api.listDelegationCandidates).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }))
      .mockResolvedValueOnce({ items: [{ id: targetB, username: 'current', displayName: 'Current' }], page: 1, pageSize: 20, total: 1 });
    const wrapper = mountPanel(api);
    try {
      await flushPromises(); await wrapper.setProps({ detail: { ...detail, id: proxy } }); await flushPromises();
      finish({ items: [{ id: targetA, username: 'old-secret', displayName: 'Old secret' }], page: 1, pageSize: 20, total: 1 }); await flushPromises();
      expect(wrapper.text()).toContain('Current'); expect(wrapper.text()).not.toContain('Old secret');
      expect(api.listDelegationCandidates).toHaveBeenLastCalledWith(proxy, 1);
    } finally { wrapper.unmount(); }
  });

  it.each([false, true])('历史收回使用独立权限、最新修订和收回原因，无需代理签名：%s', async allowed => {
    const api = client(); vi.mocked(api.delegation).mockResolvedValue(grant);
    const wrapper = mountPanel(api, { detail: { ...detail, statusKey: 'completed', signRequired: true, commentRequired: true }, mayDelegate: false, mayRecall: allowed });
    try {
      await flushPromises(); expect(wrapper.find('select').exists()).toBe(false); expect(api.listDelegationCandidates).not.toHaveBeenCalled();
      const submit = wrapper.find('[data-testid="workflow-delegation-recall"]'); expect(submit.exists()).toBe(allowed);
      if (allowed) {
        expect(submit.attributes('disabled')).toBeDefined(); await wrapper.get('textarea').setValue('收回'); await submit.trigger('click'); await flushPromises();
        expect(api.recallDelegation).toHaveBeenCalledWith(origin, { expectedRevision: 7, expectedFormRevision: 9, comment: '收回', idempotencyKey: expect.any(String) });
        expect(wrapper.emitted('completed')).toEqual([['recall']]);
      } else expect(api.recallDelegation).not.toHaveBeenCalled();
      expect(api.delegate).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });
});
