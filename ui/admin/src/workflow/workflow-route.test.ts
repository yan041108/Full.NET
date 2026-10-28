import { describe, expect, it } from 'vitest';
import { readWorkflowRoutePolicy } from './workflow-route';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';

const target = { targetKey: 'retry', nodeKey: 'review', regionKey: 'review-round', maximumIterations: 3,
  condition: { fieldKey: 'amount', operator: 'greaterThan', value: 0 } };
const policy = { modeKey: 'rules', targets: [target] };
describe('固定路由引用', () => {
  it('有界规则与机器决策严格分开', () => {
    expect(readWorkflowRoutePolicy(policy)).toEqual(policy);
    const { condition: _, ...decisionTarget } = target;
    expect(readWorkflowRoutePolicy({ modeKey: 'decision_field', decisionFieldKey: 'choice', targets: [decisionTarget] }).targets[0]?.nodeKey).toBe('review');
    expect(() => readWorkflowRoutePolicy({ modeKey: 'decision_field', decisionFieldKey: 'choice', targets: [target] })).toThrow();
  });
  it.each([0, 33, 1.5, '3'])('拒绝无界或非整数迭代 %s', maximumIterations => {
    expect(() => readWorkflowRoutePolicy({ ...policy, targets: [{ ...target, maximumIterations }] })).toThrow();
  });
  it('拒绝开放属性及重复引用', () => {
    expect(() => readWorkflowRoutePolicy({ ...policy, script: 'return next' })).toThrow();
    expect(() => readWorkflowRoutePolicy({ ...policy, targets: [target, target] })).toThrow();
    expect(() => readWorkflowRoutePolicy({ ...policy, targets: [{ ...target, regionKey: 'root' }] })).toThrow();
    expect(() => readWorkflowRoutePolicy({ ...policy, targets: [{ ...target, regionKey: 'return.reserved' }] })).toThrow();
  });
  it('往返后仍只包含一个review，路由回边不递归展开', () => {
    const draft = fromWorkflowVue3Tree({ id: 'start', type: 0, nodeName: '发起人', childNode: { id: 'review', type: 1, nodeName: '审批人',
      childNode: { id: 'route', type: 14, nodeName: '动态路由', routePolicy: policy } } });
    expect(draft.nodes.filter(node => node.nodeKey === 'review')).toHaveLength(1);
    expect(draft.nodes.find(node => node.nodeKey === 'route')?.config).toMatchObject({ routePolicy: policy });
    expect(fromWorkflowVue3Tree(toWorkflowVue3Tree(draft))).toEqual(draft);
  });
  it('结束目标的非默认机器键不能在树回显后悬空', () => {
    const source = fromWorkflowVue3Tree({ id: 'start', type: 0, childNode: { id: 'review', type: 1,
      childNode: { id: 'route', type: 14, routePolicy: { ...policy, targets: [{ ...target, nodeKey: 'end' }] } } } });
    const imported = { ...source, nodes: source.nodes.map(node => node.nodeTypeKey === 'end' ? { ...node, nodeKey: 'finish' }
      : node.nodeKey === 'route' ? { ...node, config: { ...(node.config as Record<string, unknown>), nextNodeKeys: ['finish'], routePolicy: { ...policy, targets: [{ ...target, nodeKey: 'finish' }] } } } : node) };
    const roundtrip = fromWorkflowVue3Tree(toWorkflowVue3Tree(imported));
    expect(roundtrip.nodes.find(node => node.nodeTypeKey === 'end')?.nodeKey).toBe('finish');
    expect(roundtrip.nodes.find(node => node.nodeKey === 'route')?.config).toMatchObject({ nextNodeKeys: ['finish'],
      routePolicy: { ...policy, targets: [{ ...target, nodeKey: 'finish' }] } });
  });
  it('隐式终点改名时同名end人工节点仍是实际步骤', () => {
    const source = fromWorkflowVue3Tree({ id: 'start', type: 0, endNodeKey: 'finish', childNode: { id: 'end', type: 1,
      childNode: { id: 'route', type: 14, routePolicy: { ...policy, targets: [{ ...target, nodeKey: 'end' }] } } } });
    const roundtrip = fromWorkflowVue3Tree(toWorkflowVue3Tree(source));
    expect(roundtrip.nodes.find(node => node.nodeKey === 'end')?.nodeTypeKey).toBe('human.approval');
    expect(roundtrip.nodes.find(node => node.nodeTypeKey === 'end')?.nodeKey).toBe('finish');
  });
});
