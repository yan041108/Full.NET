export type WorkflowVoteMetric = 'eligible' | 'participated' | 'valid' | 'yes' | 'no' | 'abstain';
export type WorkflowVoteResultRule = { readonly operatorKey: 'and' | 'or'; readonly conditions: readonly WorkflowVoteResultRule[] }
  | { readonly metricKey: WorkflowVoteMetric; readonly comparisonKey: 'eq' | 'gte' | 'lte'; readonly value: number };
export interface WorkflowVotePolicy {
  readonly ballotFormVersionId: string; readonly participationPercentage: number; readonly approvalPercentage: number;
  readonly allowAbstain: boolean; readonly closeAfterSeconds?: number; readonly resultFieldKey?: string;
  readonly resultRule?: WorkflowVoteResultRule;
}
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const integer = (value: unknown, minimum: number, maximum: number): value is number =>
  typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum && value <= maximum;
const invalid = (): never => { throw new Error('client.invalid_workflow_vote_policy'); };

/** 与服务端同一固定指标及规模边界，不解释脚本或展示文本。 */
export function readVoteResultRule(value: unknown): WorkflowVoteResultRule {
  let count = 0;
  const read = (candidate: unknown, depth: number): WorkflowVoteResultRule => {
    if (++count > 32 || depth > 4 || !record(candidate)) return invalid();
    if ('operatorKey' in candidate) {
      if (Object.keys(candidate).length !== 2 || !['and', 'or'].includes(String(candidate.operatorKey)) ||
        !Array.isArray(candidate.conditions) || candidate.conditions.length < 2 || candidate.conditions.length > 8) return invalid();
      return { operatorKey: candidate.operatorKey as 'and' | 'or', conditions: candidate.conditions.map(child => read(child, depth + 1)) };
    }
    if (Object.keys(candidate).length !== 3 || !['eligible', 'participated', 'valid', 'yes', 'no', 'abstain'].includes(String(candidate.metricKey)) ||
      !['eq', 'gte', 'lte'].includes(String(candidate.comparisonKey)) || !integer(candidate.value, 0, 20)) return invalid();
    return { metricKey: candidate.metricKey as WorkflowVoteMetric, comparisonKey: candidate.comparisonKey as 'eq' | 'gte' | 'lte', value: candidate.value };
  };
  return read(value, 1);
}

/** 独立表单必须选定已发布版本，个人选票绝不使用主表单修订。 */
export function readVotePolicy(value: unknown): WorkflowVotePolicy {
  if (!record(value) || Object.keys(value).some(key => !['ballotFormVersionId', 'participationPercentage', 'approvalPercentage',
    'allowAbstain', 'closeAfterSeconds', 'resultFieldKey', 'resultRule'].includes(key)) ||
    typeof value.ballotFormVersionId !== 'string' || !guid.test(value.ballotFormVersionId) ||
    !integer(value.participationPercentage, 1, 100) || !integer(value.approvalPercentage, 1, 100) || typeof value.allowAbstain !== 'boolean') return invalid();
  if ('closeAfterSeconds' in value && !integer(value.closeAfterSeconds, 60, 2_592_000) ||
    'resultFieldKey' in value && (typeof value.resultFieldKey !== 'string' || !/^[A-Za-z_][A-Za-z0-9_]{0,63}$/u.test(value.resultFieldKey))) return invalid();
  return { ballotFormVersionId: value.ballotFormVersionId, participationPercentage: value.participationPercentage,
    approvalPercentage: value.approvalPercentage, allowAbstain: value.allowAbstain,
    ...('closeAfterSeconds' in value ? { closeAfterSeconds: value.closeAfterSeconds as number } : {}),
    ...('resultFieldKey' in value ? { resultFieldKey: value.resultFieldKey as string } : {}),
    ...('resultRule' in value ? { resultRule: readVoteResultRule(value.resultRule) } : {}) };
}
