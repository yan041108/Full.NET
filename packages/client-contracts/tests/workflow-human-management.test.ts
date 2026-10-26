import { expect, it } from 'vitest';
import { readWorkflowReturnTargetPage, readWorkflowActiveCountersignChain, readWorkflowReminderTargets, readWorkflowReminderReceipt } from '../src/workflow-human-management.ts';
const id = '01912345-6789-7abc-8def-0123456789a1'; const actor = '01912345-6789-7abc-8def-0123456789a2'; const todo = '01912345-6789-7abc-8def-0123456789a3';
const target = { stepId: id, nodeKey: 'review', assigneeUserId: null, completedAtUtc: '2026-10-26T00:00:00Z' };
const item = { itemId: id, sequenceNo: 1, assigneeUserId: actor, statusKey: 'active', todoId: todo };
const chain = { chainId: todo, directionKey: 'before', statusKey: 'active', items: [item] };
const reminder = { stepId: id, nodeKey: 'review', nodeTypeKey: 'human.approval', stepRevision: 2, activeTodoCount: 2, manualCount: 0, lastRequestedAtUtc: null, canRemind: true };
it('多人退回目标保留空代表人，分页重复或未知身份失败关闭', () => {
  expect(readWorkflowReturnTargetPage([target])).toEqual([target]);
  for (const rows of [[target, target], Array(21).fill(target), [{ ...target, assigneeUserId: 'nobody' }], [{ ...target, completedAtUtc: 'later' }]]) {
    expect(() => readWorkflowReturnTargetPage(rows)).toThrow();
  }
});
it('加签显示固定次序及真实待办，不接受未知状态、重复成员或伪造空待办', () => {
  expect(readWorkflowActiveCountersignChain(chain)).toEqual(chain);
  for (const patch of [{ directionKey: 'forward' }, { statusKey: 'unknown' }, { items: [] },
    { items: [{ ...item, sequenceNo: 2 }] }, { items: [{ ...item, todoId: null }] },
    { items: [item, { ...item, itemId: todo, sequenceNo: 2 }] }]) {
    expect(() => readWorkflowActiveCountersignChain({ ...chain, ...patch })).toThrow();
  }
});
it('催办20次上限、人数及实际步骤独立修订都经业务守卫验证', () => {
  expect(readWorkflowReminderTargets({ instanceRevision: 3, items: [reminder] }).items).toEqual([reminder]);
  for (const patch of [{ manualCount: 20 }, { activeTodoCount: 0 }, { stepRevision: 0 }, { nodeTypeKey: 'timer.delay' }]) {
    expect(() => readWorkflowReminderTargets({ instanceRevision: 3, items: [{ ...reminder, ...patch }] })).toThrow();
  }
  const receipt = { requestId: todo, stepId: id, instanceRevision: 3, stepRevision: 2, manualSequence: 1, recipientCount: 2, acceptedAtUtc: '2026-10-26T00:00:00Z' };
  expect(readWorkflowReminderReceipt(receipt, id, 3, 2)).toEqual(receipt);
  expect(() => readWorkflowReminderReceipt(receipt, actor, 3, 2)).toThrow();
  expect(() => readWorkflowReminderReceipt(receipt, id, 4, 2)).toThrow();
});
