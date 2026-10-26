import { describe, expect, it } from 'vitest';
import { readWorkflowBusinessCommand } from './workflow-business-command';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';
const policy = { actionKey: 'data_approval.apply_serial_rule_update', actionVersion: 1 };
describe('业务命令固定配置', () => {
  it('保留固定动作和服务端摘要，双向转换不共享可变对象', () => {
    const node = { id: 'apply', type: 23, businessCommandPolicy: { ...policy }, boundBusinessActionHash: 'a'.repeat(64), childNode: null };
    const root = { id: 'start', type: 0, childNode: { id: 'review', type: 1, childNode: node } };
    const draft = fromWorkflowVue3Tree(root); const saved = draft.nodes.find(item => item.nodeKey === 'apply');
    expect(saved?.nodeTypeKey).toBe('business.command'); expect(readWorkflowBusinessCommand(saved?.config).businessCommandPolicy).toEqual(policy);
    expect(toWorkflowVue3Tree(draft).childNode?.childNode?.businessCommandPolicy).toEqual(policy);
    const read = readWorkflowBusinessCommand(node); read.businessCommandPolicy.actionVersion = 2; expect(node.businessCommandPolicy.actionVersion).toBe(1);
  });
  it.each([{ ...policy, actionVersion: '1' }, { ...policy, actionVersion: 0 }, { ...policy, actionVersion: 65536 }, { ...policy, sql: 'delete' }, { ...policy, actionKey: ['owner.apply'] }])('拒绝非闭合动作策略 %j', value => {
    expect(() => readWorkflowBusinessCommand({ businessCommandPolicy: value })).toThrow();
  });
  it('不接受伪造摘要、任意目标和其他节点的业务策略', () => {
    expect(() => readWorkflowBusinessCommand({ businessCommandPolicy: policy, boundBusinessActionHash: 'A'.repeat(64) })).toThrow();
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0, businessCommandPolicy: policy, childNode: null })).toThrow();
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0, childNode: { id: 'apply', type: 23, businessCommandPolicy: policy, targetId: 'arbitrary', childNode: null } })).toThrow();
  });
});
