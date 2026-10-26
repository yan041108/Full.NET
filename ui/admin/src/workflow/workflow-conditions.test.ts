import { describe, expect, it } from 'vitest';
import { readWorkflowCondition } from './workflow-conditions';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';

const leaf = { fieldKey: 'amount', operator: 'in', value: [1, 2] };
describe('有界工作流组合条件', () => {
  it('复制组合树并保留类型和集合', () => {
    const condition = { operator: 'and', conditions: [leaf, { operator: 'or', conditions: [leaf, leaf] }] };
    expect(readWorkflowCondition(condition)).toEqual(condition);
    expect(readWorkflowCondition(condition)).not.toBe(condition);
  });
  it.each([
    { operator: 'and', conditions: [] },
    { ...leaf, value: [1, 1] },
    { ...leaf, value: Array.from({ length: 33 }, (_, index) => index) },
    { ...leaf, listeners: [] },
    { operator: 'or', conditions: [leaf] }
  ])('拒绝无界或扩展条件 %j', condition => {
    expect(() => readWorkflowCondition(condition)).toThrow('client.invalid_workflow_gateway');
  });
  it('组合条件与空节点经过树和 Draft 往返保留', () => {
    const condition = { operator: 'and', conditions: [leaf, { fieldKey: 'urgent', operator: 'isNotEmpty' }] };
    const draft = fromWorkflowVue3Tree({ id: 'start', type: 0, nodeName: '发起人', workflowKindKey: 'automation', childNode: {
      id: 'gateway', type: 10, nodeName: '条件分支', conditionNodes: [
        { id: 'yes', branchKey: 'yes', type: 3, condition, childNode: { id: 'emptyYes', type: 22, nodeName: '空节点' } },
        { id: 'no', branchKey: 'no', type: 3, isDefault: true, childNode: { id: 'emptyNo', type: 22, nodeName: '空节点' } }
      ]
    } });
    const roundTrip = fromWorkflowVue3Tree(toWorkflowVue3Tree(draft));
    expect(roundTrip).toEqual(draft);
    expect(draft.nodes.filter(node => node.nodeTypeKey === 'control.empty')).toHaveLength(2);
  });
});
