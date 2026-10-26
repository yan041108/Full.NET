import { expect, it } from 'vitest';
import { readWorkflowTodoDelegation } from '../src/workflow-delegations.ts';

const grant = { originTodoId: '01912345-6789-7abc-8def-0123456789a1', proxyTodoId: '01912345-6789-7abc-8def-0123456789a2',
  originalAssigneeUserId: '01912345-6789-7abc-8def-0123456789a3', proxyUserId: '01912345-6789-7abc-8def-0123456789a4',
  statusKey: 'active', proxyRevision: 2, formRevision: 3, canRecall: true, createdAtUtc: '2026-10-25T00:00:00Z' };
it('代理最小事实允许显式空值和固定授权身份', () => {
  expect(readWorkflowTodoDelegation({ delegation: null })).toBeNull();
  expect(readWorkflowTodoDelegation({ delegation: grant })).toEqual(grant);
  expect(readWorkflowTodoDelegation({ delegation: { ...grant, statusKey: 'completed', formRevision: 0, canRecall: false } })?.canRecall).toBe(false);
});
for (const patch of [{ originTodoId: grant.proxyTodoId }, { originalAssigneeUserId: grant.proxyUserId }, { statusKey: 'administrator' },
  { statusKey: 'recalled' }, { formRevision: 0 }, { proxyRevision: 0 }, { formRevision: 1.5 }, { createdAtUtc: 'invalid' },
  { proxyUserId: '00000000-0000-0000-0000-000000000000' }]) {
  it(`代理业务不变量失败关闭 ${JSON.stringify(patch)}`, () => expect(() => readWorkflowTodoDelegation({ delegation: { ...grant, ...patch } })).toThrow());
}
