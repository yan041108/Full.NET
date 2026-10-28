import { describe, expect, it } from 'vitest';
import { readWorkflowSubprocess, readWorkflowSubprocessStarter } from './workflow-subprocess';
const user = '11111111-1111-4111-8111-111111111111';
describe('子发起人闭合配置', () => {
  it('拒绝数组身份、外部租户、空身份和无角色集合的项发起人', () => {
    for (const source of [{ kindKey: 'specified_user', userId: [user] }, { kindKey: 'specified_user', userId: '00000000-0000-0000-0000-000000000000' },
      { kindKey: 'specified_user', userId: user, tenantId: user }, { kindKey: 'item_user' }]) expect(() => readWorkflowSubprocessStarter({ source })).toThrow();
    expect(readWorkflowSubprocessStarter({ source: { kindKey: 'field_user', fieldKey: 'actor' } })).toEqual({ source: { kindKey: 'field_user', fieldKey: 'actor' } });
  });
  it('来源必须可读、回填必须可写且不能与子结果映射重叠', () => {
    const config = { subprocessPolicy: { definitionVersionId: user, inputFields: {}, outputFields: {} }, fieldPolicies: { actor: 'readOnly', result: 'editable' }, starterPolicy: { source: { kindKey: 'field_user', fieldKey: 'actor' }, resultFieldKey: 'result' } };
    expect(readWorkflowSubprocess(config).starterPolicy).toEqual(config.starterPolicy);
    expect(() => readWorkflowSubprocess({ ...config, fieldPolicies: { actor: 'hidden', result: 'editable' } })).toThrow();
    expect(() => readWorkflowSubprocess({ ...config, subprocessPolicy: { ...config.subprocessPolicy, outputFields: { result: 'child' } } })).toThrow();
  });
});
