import { describe, expect, it } from 'vitest';
import { readWorkflowMultiInstance, readWorkflowSubprocess } from './workflow-subprocess';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';

const version = '11111111-1111-4111-8111-111111111111';
const policy = { modeKey: 'parallel', source: { kindKey: 'fixed_count', count: 3 }, requiredPercentage: 67, emptyModeKey: 'reject', outputReducers: { result: 'sum' } };
describe('多实例子流程闭合配置', () => {
  it('四种来源有严格类型和上限，拒绝未知属性及字符串计数', () => {
    for (const source of [{ kindKey: 'fixed_count', count: 3 }, { kindKey: 'number_field', fieldKey: 'count', maximumCount: 3 },
      { kindKey: 'collection_field', fieldKey: 'members', itemFieldKey: 'member', maximumCount: 3 },
      { kindKey: 'role_members', roleId: version, itemFieldKey: 'member', maximumCount: 3 }]) expect(readWorkflowMultiInstance({ ...policy, source }).source).toEqual(source);
    for (const source of [{ kindKey: 'fixed_count', count: '3' }, { kindKey: 'fixed_count', count: 33 }, { kindKey: 'fixed_count', count: 2, script: 'x' },
      { kindKey: 'role_members', roleId: 'any', itemFieldKey: 'member', maximumCount: 3 }]) expect(() => readWorkflowMultiInstance({ ...policy, source })).toThrow();
    expect(() => readWorkflowMultiInstance({ ...policy, requiredPercentage: '67' })).toThrow();
    expect(() => readWorkflowMultiInstance({ ...policy, outputReducers: { result: 'eval' } })).toThrow();
  });
  it('来源字段必须可读，归约集合完整，单项输入不能重复映射', () => {
    const node = { subprocessPolicy: { definitionVersionId: version, inputFields: {}, outputFields: { result: 'score' } }, fieldPolicies: { result: 'editable' }, multiInstancePolicy: policy };
    expect(readWorkflowSubprocess(node).multiInstancePolicy).toEqual(policy);
    expect(() => readWorkflowSubprocess({ ...node, multiInstancePolicy: { ...policy, outputReducers: {} } })).toThrow();
    expect(() => readWorkflowSubprocess({ ...node, multiInstancePolicy: { ...policy, source: { kindKey: 'number_field', fieldKey: 'count', maximumCount: 3 } } })).toThrow();
    expect(() => readWorkflowSubprocess({ ...node, subprocessPolicy: { ...node.subprocessPolicy, inputFields: { member: 'title' } }, fieldPolicies: { ...node.fieldPolicies, title: 'readOnly', members: 'readOnly' },
      multiInstancePolicy: { ...policy, source: { kindKey: 'collection_field', fieldKey: 'members', itemFieldKey: 'member', maximumCount: 3 } } })).toThrow();
  });
  it('真实树与规范图往返保留策略及公共后继', () => {
    const tree = { id: 'start', type: 0, nodeName: 'start', childNode: { id: 'children', type: 9, nodeName: 'children', subprocessPolicy: { definitionVersionId: version, inputFields: {}, outputFields: { result: 'score' } },
      fieldPolicies: { result: 'editable' }, multiInstancePolicy: policy, childNode: null } };
    const canonical = fromWorkflowVue3Tree(tree);
    expect((canonical.nodes.find(node => node.nodeKey === 'children')?.config as Record<string, unknown>).multiInstancePolicy).toEqual(policy);
    expect(fromWorkflowVue3Tree(toWorkflowVue3Tree(canonical))).toEqual(canonical);
  });
});
