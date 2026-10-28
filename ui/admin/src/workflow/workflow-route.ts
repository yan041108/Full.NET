import { readWorkflowCondition, type WorkflowConditionConfig } from './workflow-conditions';

export interface WorkflowRouteTarget {
  readonly targetKey: string;
  readonly nodeKey: string;
  readonly regionKey: string;
  readonly maximumIterations: number;
  readonly condition?: WorkflowConditionConfig;
}
export interface WorkflowRoutePolicy {
  readonly modeKey: 'rules' | 'decision_field';
  readonly decisionFieldKey?: string;
  readonly targets: readonly WorkflowRouteTarget[];
}
const keyPattern = /^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u;
const invalid = (): never => { throw new Error('client.invalid_workflow_route'); };
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
function key(value: unknown, maximum = 128): string {
  return typeof value === 'string' && value.length <= maximum && keyPattern.test(value) ? value : invalid();
}
/** 目标保持稳定引用，回边不会成为树的childNode；服务端另行验证结构区域和人审屏障。 */
export function readWorkflowRoutePolicy(value: unknown): WorkflowRoutePolicy {
  if (!record(value) || Object.keys(value).some(name => !['modeKey', 'decisionFieldKey', 'targets'].includes(name))
    || !['rules', 'decision_field'].includes(String(value.modeKey)) || !Array.isArray(value.targets)
    || value.targets.length < 1 || value.targets.length > 8) return invalid();
  const modeKey = value.modeKey as WorkflowRoutePolicy['modeKey'];
  if (modeKey === 'rules' && value.decisionFieldKey !== undefined) return invalid();
  const decisionFieldKey = modeKey === 'decision_field' ? key(value.decisionFieldKey) : undefined;
  const targets = value.targets.map((target): WorkflowRouteTarget => {
    if (!record(target) || Object.keys(target).some(name => !['targetKey', 'nodeKey', 'regionKey', 'maximumIterations', 'condition'].includes(name))
      || typeof target.maximumIterations !== 'number' || !Number.isInteger(target.maximumIterations)
      || target.maximumIterations < 1 || target.maximumIterations > 32 || target.regionKey === 'root'
      || typeof target.regionKey === 'string' && target.regionKey.startsWith('return.')
      || modeKey === 'decision_field' && target.condition !== undefined) return invalid();
    return { targetKey: key(target.targetKey, 64), nodeKey: key(target.nodeKey), regionKey: key(target.regionKey, 64), maximumIterations: target.maximumIterations,
      ...(modeKey === 'rules' ? { condition: readWorkflowCondition(target.condition) } : {}) };
  });
  if (['targetKey', 'nodeKey', 'regionKey'].some(name => new Set(targets.map(target => target[name as keyof WorkflowRouteTarget])).size !== targets.length)) return invalid();
  return { modeKey, ...(decisionFieldKey ? { decisionFieldKey } : {}), targets };
}
