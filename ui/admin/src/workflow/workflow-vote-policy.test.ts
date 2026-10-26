import { describe, expect, it } from 'vitest';
import { readVotePolicy, readVoteResultRule } from './workflow-vote-policy';

const base = { ballotFormVersionId: '019a1a10-1000-7000-8000-000000000001', participationPercentage: 100,
  approvalPercentage: 67, allowAbstain: true };
describe('独立投票配置', () => {
  it('保留固定表单、比例、截止和声明式结果绑定', () => {
    const value = { ...base, closeAfterSeconds: 600, resultFieldKey: 'accepted',
      resultRule: { operatorKey: 'and', conditions: [{ metricKey: 'yes', comparisonKey: 'gte', value: 2 },
        { metricKey: 'no', comparisonKey: 'eq', value: 0 }] } };
    expect(readVotePolicy(value)).toEqual(value);
    expect(readVotePolicy(value)).not.toBe(value);
  });
  it.each([{ ...base, script: 'return true' }, { ...base, approvalPercentage: 0 },
    { ...base, participationPercentage: 1.5 }, { ...base, ballotFormVersionId: '00000000-0000-0000-0000-000000000000' },
    { ...base, closeAfterSeconds: 59 }, { ...base, resultFieldKey: 'a.b' }, { ...base, allowAbstain: 'true' }])('拒绝未知或越界配置', value => {
    expect(() => readVotePolicy(value)).toThrow('client.invalid_workflow_vote_policy');
  });
  it.each([{ metricKey: 'user', comparisonKey: 'eq', value: 0 }, { metricKey: 'yes', comparisonKey: 'eval', value: 0 },
    { metricKey: 'yes', comparisonKey: 'gte', value: 21 }, { operatorKey: 'and', conditions: [] },
    { metricKey: 'yes', comparisonKey: 'gte', value: 1, script: 'true' }])('结果规则不能执行脚本或访问成员身份', value => {
    expect(() => readVoteResultRule(value)).toThrow('client.invalid_workflow_vote_policy');
  });
  it('同时限制深度和节点总量', () => {
    const leaf = { metricKey: 'yes', comparisonKey: 'gte', value: 1 };
    const group = (child: unknown) => ({ operatorKey: 'and', conditions: [child, leaf] });
    expect(() => readVoteResultRule(group(group(group(group(leaf)))))).toThrow();
    const wide = { operatorKey: 'or', conditions: Array.from({ length: 8 }, () => leaf) };
    expect(() => readVoteResultRule({ operatorKey: 'and', conditions: Array.from({ length: 4 }, () => wide) })).toThrow();
  });
});
