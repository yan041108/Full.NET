import { readWorkflowCondition, type WorkflowConditionConfig } from './workflow-conditions';

export interface WorkflowFieldAssignment { fieldKey: string; sourceFieldKey?: string; value?: unknown }
export interface WorkflowFieldMutationConfiguration {
  fieldPolicies: Record<string, string>;
  mutationPolicy: { assignments?: WorkflowFieldAssignment[]; fieldKeys?: string[]; condition?: WorkflowConditionConfig };
}
const invalid = (): never => { throw new Error('client.invalid_workflow_field_mutation'); };
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const key = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z][A-Za-z0-9_.-]{0,63}$/u.test(value) && !['constructor', 'prototype', '__proto__'].includes(value);
export const workflowConditionFields = (value: WorkflowConditionConfig): string[] => value.conditions?.flatMap(workflowConditionFields) ?? (value.fieldKey ? [value.fieldKey] : []);

/** 固定字段动作只投影显式权限和受控补丁；完整类型及必填约束在发布时绑定Schema。 */
export function readWorkflowFieldMutation(kind: string, config: unknown): WorkflowFieldMutationConfiguration {
  if (!['data.modify', 'data.clear'].includes(kind) || !record(config) || !record(config.fieldPolicies) || !record(config.mutationPolicy)) return invalid();
  const access: Record<string, string> = {};
  for (const [field, mode] of Object.entries(config.fieldPolicies)) {
    if (!key(field) || typeof mode !== 'string' || !['hidden', 'readOnly', 'editable', 'required'].includes(mode)) return invalid();
    access[field] = mode;
  }
  const policy = config.mutationPolicy;
  if (Object.keys(policy).some(name => !['assignments', 'fieldKeys', 'condition'].includes(name))) return invalid();
  const targets: string[] = [];
  const mutation: WorkflowFieldMutationConfiguration['mutationPolicy'] = {};
  if (kind === 'data.clear') {
    if ('assignments' in policy || !Array.isArray(policy.fieldKeys) || policy.fieldKeys.length < 1 || policy.fieldKeys.length > 16 || policy.fieldKeys.some(field => !key(field))) return invalid();
    targets.push(...policy.fieldKeys as string[]); mutation.fieldKeys = [...targets];
  } else {
    if ('fieldKeys' in policy || !Array.isArray(policy.assignments) || policy.assignments.length < 1 || policy.assignments.length > 16) return invalid();
    mutation.assignments = policy.assignments.map(row => {
      if (!record(row) || Object.keys(row).some(name => !['fieldKey', 'sourceFieldKey', 'value'].includes(name)) || !key(row.fieldKey) || ('sourceFieldKey' in row) === ('value' in row)) return invalid();
      targets.push(row.fieldKey);
      if ('sourceFieldKey' in row) {
        if (!key(row.sourceFieldKey) || !access[row.sourceFieldKey] || access[row.sourceFieldKey] === 'hidden') return invalid();
        return { fieldKey: row.fieldKey, sourceFieldKey: row.sourceFieldKey };
      }
      let json: string | undefined;
      try { json = JSON.stringify(row.value); } catch { return invalid(); }
      if (json === undefined || new TextEncoder().encode(json).byteLength > 16384 || typeof row.value === 'number' && !Number.isFinite(row.value)) return invalid();
      return { fieldKey: row.fieldKey, value: JSON.parse(json) as unknown };
    });
  }
  if (new Set(targets).size !== targets.length || targets.some(field => !['editable', 'required'].includes(access[field] ?? '')) || kind === 'data.clear' && targets.some(field => access[field] === 'required')) return invalid();
  if ('condition' in policy) {
    mutation.condition = readWorkflowCondition(policy.condition);
    if (workflowConditionFields(mutation.condition).some(field => !access[field] || access[field] === 'hidden')) return invalid();
  }
  return { fieldPolicies: access, mutationPolicy: mutation };
}
