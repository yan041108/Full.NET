import { describe, expect, it } from 'vitest';
import { readWorkflowSlaPolicy } from './workflow-sla-policy';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';
const policy = { dueAfterMinutes: 60, pauseModeKey: 'freeze', actionKey: 'suspend' };
describe('活动SLA固定策略', () => {
  it.each(['human.approval', 'human.group_approval', 'human.task', 'human.vote', 'human.claim'])('明确暂停语义并切断输入引用 %s', kind => {
    expect(readWorkflowSlaPolicy(policy, kind)).toEqual(policy);
    expect(readWorkflowSlaPolicy(policy, kind)).not.toBe(policy);
  });
  it.each([
    { ...policy, dueAfterMinutes: 0 }, { ...policy, dueAfterMinutes: 525601 }, { ...policy, dueAfterMinutes: 1.5 },
    { ...policy, pauseModeKey: 'implicit' }, { ...policy, actorUserId: 'fake' }, { ...policy, actionKey: 'script' }
  ])('拒绝隐含时钟、额外身份与动态动作 %j', value => expect(() => readWorkflowSlaPolicy(value, 'human.approval')).toThrow());
  it.each(['human.task', 'human.vote', 'human.claim', 'control.empty'])('系统通过不替代个人动作 %s', kind =>
    expect(() => readWorkflowSlaPolicy({ ...policy, actionKey: 'auto_pass' }, kind)).toThrow());
  it('真实适配器保存截止配置和原后继', () => {
    const root = { id: 'start', type: 0, nodeName: '发起人', workflowKindKey: 'automation', childNode: { id: 'approval', type: 1, nodeName: '审批人', slaPolicy: policy,
      childNode: null } };
    const draft = fromWorkflowVue3Tree(root); const restored = fromWorkflowVue3Tree(toWorkflowVue3Tree(draft));
    expect(restored).toEqual(draft); expect(draft.nodes.find(node => node.nodeKey === 'approval')!.config).toMatchObject({ slaPolicy: policy, nextNodeKeys: ['end'] });
  });
});
