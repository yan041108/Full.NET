// 两端分别执行严格类型检查；ESM仅组合验证原生控件和已授权管理动作的交互。
import { flushPromises, mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import { HttpProblem } from '../../../../clients/uniapp/src/api/problem-details';
import Panel from '../../../../clients/uniapp/src/features/workflow/WorkflowHumanManagementPanel.vue';
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: key => key }) }));
const origin = '01912345-6789-7abc-8def-0123456789a1'; const instance = '01912345-6789-7abc-8def-0123456789a2';
const step = '01912345-6789-7abc-8def-0123456789a3'; const actor = '01912345-6789-7abc-8def-0123456789a4';
const first = '01912345-6789-7abc-8def-0123456789a5'; const second = '01912345-6789-7abc-8def-0123456789a6';
const detail = { id: origin, instanceId: instance, stepId: step, assigneeUserId: actor, statusKey: 'active', revision: 3, submissionRevision: 4,
  allowedActions: ['transfer', 'return', 'countersign', 'remind'], signRequired: false, commentRequired: false };
const chain = { chainId: second, statusKey: 'active', directionKey: 'after', items: [{ itemId: first, assigneeUserId: second, sequenceNo: 1, statusKey: 'active', todoId: origin }] };
const permissions = { transfer: false, return: false, countersign: false, remind: false };
function client() { return { listManagementCandidates: vi.fn().mockImplementation(async (_id, _kind, page) => ({ page, pageSize: 20, total: 21, items: page === 1
  ? [{ id: actor, username: 'self', displayName: 'Self' }, { id: first, username: 'first', displayName: 'First' },
    ...Array.from({ length: 18 }, (_, index) => ({ id: `01912345-6789-7abc-8def-01234567${(0xb0 + index).toString(16).padStart(4, '0')}`, username: `member${index}`, displayName: `Member ${index}` }))]
  : [{ id: second, username: 'second', displayName: 'Second' }] })),
  listReturnTargets: vi.fn().mockResolvedValue([{ stepId: first, nodeKey: 'previous', assigneeUserId: null, completedAtUtc: '2026-10-26T00:00:00Z' }]),
  countersignChain: vi.fn().mockResolvedValue(null), reminderTargets: vi.fn().mockResolvedValue({ instanceRevision: 7, items: [{ stepId: step, stepRevision: 8, canRemind: true }] }),
  transfer: vi.fn().mockResolvedValue({}), returnTodo: vi.fn().mockResolvedValue({}), countersign: vi.fn().mockResolvedValue({}),
  cancelCountersign: vi.fn().mockResolvedValue({}), remind: vi.fn().mockResolvedValue({}) }; }
const native = { view: { template: '<div><slot /></div>' }, text: { template: '<span><slot /></span>' },
  picker: { props: ['range', 'value', 'disabled'], emits: ['change'], template: `<select :value="value" :disabled="disabled" @change="$emit('change', { detail: { value: $event.target.value } })"><option v-for="(name, index) in range" :key="index" :value="index">{{ name }}</option></select>` },
  'uni-easyinput': { props: ['modelValue', 'disabled'], emits: ['update:modelValue'], template: `<textarea :value="modelValue" :disabled="disabled" @input="$emit('update:modelValue', $event.target.value)" />` } };
function panel(api, overrides = {}) { return mount(Panel, { props: { detail, client: api, permissions, comment: '', unsaved: false, disabled: false, ...overrides }, global: { stubs: native } }); }
async function click(wrapper, text) { const button = wrapper.findAll('button').find(item => item.text() === text); expect(button).toBeDefined(); await button.trigger('click'); await flushPromises(); }
describe('uni-app human management', () => {
  it('缺少精确权限时不枚举任何候选、退回、加签或催办范围', async () => {
    const api = client(); const wrapper = panel(api);
    try { await flushPromises(); expect(wrapper.find('select').exists()).toBe(false);
      for (const key of ['listManagementCandidates', 'listReturnTargets', 'countersignChain', 'reminderTargets']) expect(api[key]).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });
  it('真实20条分页保留目标，签名、意见与未保存字段分别限制转办，暂时失败复用键', async () => {
    const api = client(); api.transfer.mockRejectedValueOnce(new HttpProblem({ status: 503 })).mockResolvedValueOnce({});
    const wrapper = panel(api, { permissions: { ...permissions, transfer: true }, detail: { ...detail, signRequired: true, commentRequired: true } });
    try {
      await flushPromises(); expect(wrapper.text()).not.toContain('Self'); await wrapper.get('select').setValue('1');
      await click(wrapper, 'workflow.todo.nextPage'); expect(api.listManagementCandidates).toHaveBeenLastCalledWith(origin, 'transfer', 2);
      expect(wrapper.get('select').element.value).toBe('1'); const submit = wrapper.get('[data-testid="workflow-management-submit"]');
      expect(submit.attributes('disabled')).toBeDefined(); await wrapper.setProps({ signatureFileId: second, comment: '交接', unsaved: true }); expect(submit.attributes('disabled')).toBeDefined();
      await wrapper.setProps({ unsaved: false }); await submit.trigger('click'); await flushPromises(); await submit.trigger('click'); await flushPromises();
      expect(api.transfer).toHaveBeenCalledTimes(2); const body = api.transfer.mock.calls[0][1];
      expect(body).toEqual({ assigneeUserId: first, expectedRevision: 3, expectedFormRevision: 4, comment: '交接', signatureFileId: second, idempotencyKey: expect.any(String) });
      expect(api.transfer.mock.calls[1][1].idempotencyKey).toBe(body.idempotencyKey); expect(wrapper.emitted('completed')).toEqual([[]]);
    } finally { wrapper.unmount(); }
  });
  it('跨页加签保留成员顺序，管理请求不伪造业务签名或表单修订', async () => {
    const api = client(); const wrapper = panel(api, { permissions: { ...permissions, countersign: true }, detail: { ...detail, signRequired: true } });
    try {
      await flushPromises(); await wrapper.get('select').setValue('1'); await click(wrapper, 'workflow.todo.management.add');
      await click(wrapper, 'workflow.todo.nextPage'); await wrapper.get('select').setValue('1'); await click(wrapper, 'workflow.todo.management.add');
      await click(wrapper, 'workflow.todo.management.after'); await wrapper.get('[data-testid="workflow-management-submit"]').trigger('click'); await flushPromises();
      expect(api.countersign).toHaveBeenCalledWith(origin, { directionKey: 'after', assigneeUserIds: [first, second], expectedRevision: 3, comment: '', idempotencyKey: expect.any(String) });
    } finally { wrapper.unmount(); }
  });
  it('多人历史退回使用实际步骤而非单人代表，保持固定修订与空字段补丁', async () => {
    const api = client(); const wrapper = panel(api, { permissions: { ...permissions, return: true }, comment: '退回' });
    try { await flushPromises(); await wrapper.get('select').setValue('1'); await wrapper.get('[data-testid="workflow-management-submit"]').trigger('click'); await flushPromises();
      expect(api.returnTodo).toHaveBeenCalledWith(origin, { targetStepId: first, expectedRevision: 3, expectedFormRevision: 4, fieldPatch: {}, comment: '退回', signatureFileId: undefined, idempotencyKey: expect.any(String) });
    } finally { wrapper.unmount(); }
  });
  it('历史后加签可独立取消，催办使用服务端实例与步骤双修订', async () => {
    const api = client(); api.countersignChain.mockResolvedValue(chain);
    const wrapper = panel(api, { detail: { ...detail, statusKey: 'completed', commentRequired: true, signRequired: true }, permissions: { ...permissions, countersign: true } });
    try { await flushPromises(); await wrapper.get('textarea').setValue('取消咨询'); await wrapper.get('[data-testid="workflow-management-cancel"]').trigger('click'); await flushPromises();
      expect(api.cancelCountersign).toHaveBeenCalledWith(origin, { expectedRevision: 3, comment: '取消咨询', idempotencyKey: expect.any(String) });
    } finally { wrapper.unmount(); }
    const reminder = panel(api, { permissions: { ...permissions, remind: true } });
    try { await flushPromises(); await reminder.get('[data-testid="workflow-management-remind"]').trigger('click'); await flushPromises();
      expect(api.reminderTargets).toHaveBeenLastCalledWith(instance); expect(api.remind).toHaveBeenCalledWith(instance, { stepId: step, expectedInstanceRevision: 7, expectedStepRevision: 8, idempotencyKey: expect.any(String) });
      expect(reminder.emitted('refresh')).toEqual([[]]);
    } finally { reminder.unmount(); }
  });
  it('资源切换后拒绝迟到候选', async () => {
    const api = client(); let resolve;
    api.listManagementCandidates.mockImplementationOnce(() => new Promise(finish => { resolve = finish; }));
    const wrapper = panel(api, { permissions: { ...permissions, transfer: true } });
    try { await flushPromises(); await wrapper.setProps({ detail: { ...detail, id: second } }); await flushPromises();
      resolve({ items: [{ id: first, username: 'secret', displayName: 'Old secret' }], page: 1, pageSize: 20, total: 1 }); await flushPromises();
      expect(wrapper.text()).not.toContain('Old secret'); expect(api.listManagementCandidates).toHaveBeenLastCalledWith(second, 'transfer', 1);
    } finally { wrapper.unmount(); }
  });
});
