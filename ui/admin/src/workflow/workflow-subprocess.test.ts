import { describe, expect, it } from 'vitest';
import { readWorkflowSubprocess } from './workflow-subprocess';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';
const version = '11111111-1111-4111-8111-111111111111';
const config = () => ({ subprocessPolicy: { definitionVersionId: version, inputFields: { subject: 'title' }, outputFields: { result: 'answer' } }, fieldPolicies: { title: 'readOnly', result: 'editable' } });
describe('固定子流程配置', () => {
  it('真实适配器往返保留固定版本、映射和公共后继', () => {
    const tree = { id: 'start', type: 0, childNode: { id: 'child', type: 9, ...config(), childNode: { id: 'after', type: 22, childNode: null } } };
    const draft = fromWorkflowVue3Tree(tree); const child = draft.nodes.find(node => node.nodeKey === 'child');
    expect(child?.nodeTypeKey).toBe('process.subprocess'); expect((child?.config as Record<string, unknown>).nextNodeKeys).toEqual(['after']);
    expect(toWorkflowVue3Tree(draft).childNode?.subprocessPolicy).toEqual(config().subprocessPolicy);
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0, subprocessPolicy: config().subprocessPolicy, childNode: null })).toThrow();
  });
  it('复制映射并保留发布绑定', () => {
    const original = config(); const value = readWorkflowSubprocess(original); value.subprocessPolicy.inputFields.subject = 'other';
    expect(original.subprocessPolicy.inputFields.subject).toBe('title');
    const bound = { definitionVersionId: version, formVersionId: version, definitionHash: 'a'.repeat(64), formHash: 'b'.repeat(64), activityBudget: 64, maximumDepth: 1, hasHumanApproval: true };
    expect(readWorkflowSubprocess({ ...original, boundSubprocess: bound }).boundSubprocess).toEqual(bound);
  });
  it('拒绝隐式扩展和非法版本', () => {
    expect(() => readWorkflowSubprocess({ ...config(), subprocessPolicy: { ...config().subprocessPolicy, modeKey: 'parallel' } })).toThrow();
    expect(() => readWorkflowSubprocess({ ...config(), subprocessPolicy: { ...config().subprocessPolicy, definitionVersionId: '' } })).toThrow();
  });
  it('要求明确读取和写入权限', () => {
    expect(() => readWorkflowSubprocess({ ...config(), fieldPolicies: { title: 'hidden', result: 'editable' } })).toThrow();
    expect(() => readWorkflowSubprocess({ ...config(), fieldPolicies: { title: 'readOnly', result: 'readOnly' } })).toThrow();
  });
  it('拒绝错版绑定及原型字段', () => {
    expect(() => readWorkflowSubprocess({ ...config(), boundSubprocess: {} })).toThrow();
    expect(() => readWorkflowSubprocess({ ...config(), subprocessPolicy: { ...config().subprocessPolicy, inputFields: { subject: 'constructor' } } })).toThrow();
  });
});
