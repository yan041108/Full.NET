import { describe, expect, it } from 'vitest';
import { readWorkflowResultCallback } from './workflow-result-callback';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';
const config = { resultCallbackFieldKey: 'accepted', fieldPolicies: { accepted: 'editable' } };
describe('最终活动结果回填', () => {
  it.each(['human.approval', 'human.group_approval', 'human.task', 'human.vote', 'human.claim'])('绑定显式可写开关字段 %s', kind => {
    expect(readWorkflowResultCallback(config, kind, [{ fieldKey: 'accepted', fieldTypeKey: 'switch', required: false, constraints: {} }])).toBe('accepted');
  });
  it.each([
    { resultCallbackFieldKey: 'accepted' }, { ...config, resultCallbackFieldKey: 'constructor' },
    { ...config, resultCallbackFieldKey: 'other' }, { ...config, fieldPolicies: { accepted: 'hidden' } },
    { ...config, fieldPolicies: { accepted: 'readOnly' } }, { ...config, fieldPolicies: [] }
  ])('拒绝隐式、隐藏和只读目标 %j', value => expect(() => readWorkflowResultCallback(value, 'human.approval')).toThrow());
  it('不能把文本或文件字段作为结果，并拒绝非人工活动配置', () => {
    expect(() => readWorkflowResultCallback(config, 'timer.delay')).toThrow();
    expect(() => readWorkflowResultCallback(config, 'human.approval', [{ fieldKey: 'accepted', fieldTypeKey: 'text', required: false, constraints: {} }])).toThrow();
    expect(() => readWorkflowResultCallback(config, 'human.approval', [])).toThrow();
  });
  it('真实图与树往返保留最终结果字段和原后继', () => {
    const root = { id: 'start', type: 0, nodeName: '发起人', workflowKindKey: 'automation', childNode: { id: 'approval', type: 1, nodeName: '审批人', ...config, childNode: null } };
    const draft = fromWorkflowVue3Tree(root); expect(fromWorkflowVue3Tree(toWorkflowVue3Tree(draft))).toEqual(draft);
    expect(draft.nodes.find(node => node.nodeKey === 'approval')!.config).toMatchObject({ ...config, nextNodeKeys: ['end'] });
  });
});
