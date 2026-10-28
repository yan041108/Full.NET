export interface WorkflowSubprocessConfiguration {
  subprocessPolicy: { definitionVersionId: string; inputFields: Record<string, string>; outputFields: Record<string, string> };
  fieldPolicies: Record<string, string>;
  boundSubprocess?: Record<string, unknown>;
  multiInstancePolicy?: WorkflowMultiInstancePolicy;
  starterPolicy?: WorkflowSubprocessStarterPolicy;
  childTimeoutPolicy?: WorkflowSubprocessTimeoutPolicy;
}
export interface WorkflowSubprocessTimeoutPolicy { dueAfterMinutes: number; pauseModeKey: 'freeze' | 'elapsed'; actionKey: 'continue' | 'route'; nextNodeKey?: string }
export interface WorkflowSubprocessStarterPolicy {
  source: { kindKey: 'parent' | 'specified_user' | 'item_user' | 'field_user'; userId?: string; fieldKey?: string };
  resultFieldKey?: string;
}
export interface WorkflowMultiInstancePolicy {
  modeKey: 'serial' | 'parallel';
  source: { kindKey: 'fixed_count' | 'number_field' | 'collection_field' | 'role_members'; count?: number; maximumCount?: number; fieldKey?: string; roleId?: string; itemFieldKey?: string };
  requiredPercentage: number;
  emptyModeKey: 'reject' | 'skip';
  outputReducers: Record<string, string>;
}
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const id = (value: unknown): value is string => typeof value === 'string' && /^(?!00000000-0000-0000-0000-000000000000$)[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/iu.test(value);
const key = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z][A-Za-z0-9_.-]{0,63}$/u.test(value) && !['constructor', 'prototype', '__proto__'].includes(value);
const invalid = (): never => { throw new Error('client.invalid_workflow_subprocess'); };
function map(value: unknown): Record<string, string> {
  if (!object(value) || Object.keys(value).length > 32) return invalid();
  const result: Record<string, string> = {};
  for (const [target, source] of Object.entries(value)) { if (!key(target) || !key(source)) return invalid(); result[target] = source; }
  return result;
}
/** 子流程只接受固定版本、明确字段映射和闭合多实例策略，禁止静默透传来源脚本。 */
export function readWorkflowSubprocess(value: unknown): WorkflowSubprocessConfiguration {
  if (!object(value) || !object(value.subprocessPolicy) || !object(value.fieldPolicies)) return invalid();
  const policy = value.subprocessPolicy;
  if (Object.keys(policy).length !== 3 || Object.keys(policy).some(name => !['definitionVersionId', 'inputFields', 'outputFields'].includes(name)) || !id(policy.definitionVersionId)) return invalid();
  const inputFields = map(policy.inputFields); const outputFields = map(policy.outputFields); const grants: Record<string, string> = {};
  for (const [field, access] of Object.entries(value.fieldPolicies)) {
    if (!key(field) || typeof access !== 'string' || !['hidden', 'readOnly', 'editable', 'required'].includes(access)) return invalid();
    grants[field] = access;
  }
  if (Object.values(inputFields).some(field => !grants[field] || grants[field] === 'hidden') ||
      Object.keys(outputFields).some(field => !['editable', 'required'].includes(grants[field] ?? ''))) return invalid();
  const result: WorkflowSubprocessConfiguration = { subprocessPolicy: { definitionVersionId: policy.definitionVersionId, inputFields, outputFields }, fieldPolicies: grants };
  if (value.multiInstancePolicy !== undefined) {
    result.multiInstancePolicy = readWorkflowMultiInstance(value.multiInstancePolicy);
    const multi = result.multiInstancePolicy;
    if (multi.source.fieldKey && (!grants[multi.source.fieldKey] || grants[multi.source.fieldKey] === 'hidden') ||
        multi.source.itemFieldKey && inputFields[multi.source.itemFieldKey] ||
        Object.keys(multi.outputReducers).length !== Object.keys(outputFields).length ||
        Object.keys(outputFields).some(field => !multi.outputReducers[field])) return invalid();
  }
  if (value.starterPolicy !== undefined) {
    const starter = readWorkflowSubprocessStarter(value.starterPolicy, result.multiInstancePolicy);
    if (starter.source.fieldKey && (!grants[starter.source.fieldKey] || grants[starter.source.fieldKey] === 'hidden') ||
        starter.resultFieldKey && (outputFields[starter.resultFieldKey] || !['editable', 'required'].includes(grants[starter.resultFieldKey] ?? ''))) return invalid();
    result.starterPolicy = starter;
  }
  if (value.childTimeoutPolicy !== undefined) result.childTimeoutPolicy = readWorkflowSubprocessTimeout(value.childTimeoutPolicy);
  if (value.boundSubprocess !== undefined) {
    const bound = value.boundSubprocess;
    if (!object(bound) || Object.keys(bound).length !== 7 || Object.keys(bound).some(name => !['definitionVersionId', 'formVersionId', 'definitionHash', 'formHash', 'activityBudget', 'maximumDepth', 'hasHumanApproval'].includes(name)) ||
        bound.definitionVersionId !== policy.definitionVersionId || !id(bound.formVersionId) ||
        ![bound.definitionHash, bound.formHash].every(hash => typeof hash === 'string' && /^[a-f0-9]{64}$/u.test(hash)) ||
        !Number.isInteger(bound.activityBudget) || Number(bound.activityBudget) < 2 || Number(bound.activityBudget) > 4096 ||
        !Number.isInteger(bound.maximumDepth) || Number(bound.maximumDepth) < 1 || Number(bound.maximumDepth) > 8 || typeof bound.hasHumanApproval !== 'boolean') return invalid();
    result.boundSubprocess = { ...bound };
  }
  return result;
}

/** 固定分母的有界来源与确定性归约，服务端仍复核真实Schema及角色作用域。 */
export function readWorkflowMultiInstance(value: unknown): WorkflowMultiInstancePolicy {
  if (!object(value) || Object.keys(value).length !== 5 || Object.keys(value).some(name => !['modeKey', 'source', 'requiredPercentage', 'emptyModeKey', 'outputReducers'].includes(name)) ||
      !['serial', 'parallel'].includes(String(value.modeKey)) || !['reject', 'skip'].includes(String(value.emptyModeKey)) ||
      !Number.isInteger(value.requiredPercentage) || Number(value.requiredPercentage) < 1 || Number(value.requiredPercentage) > 100 || !object(value.source) || !object(value.outputReducers)) return invalid();
  const source = value.source; const kind = source.kindKey;
  const expected = kind === 'fixed_count' ? ['kindKey', 'count'] : kind === 'number_field' ? ['kindKey', 'fieldKey', 'maximumCount']
    : kind === 'collection_field' ? ['kindKey', 'fieldKey', 'itemFieldKey', 'maximumCount'] : kind === 'role_members' ? ['kindKey', 'roleId', 'itemFieldKey', 'maximumCount'] : [];
  if (!expected.length || Object.keys(source).length !== expected.length || Object.keys(source).some(name => !expected.includes(name))) return invalid();
  const maximum = kind === 'fixed_count' ? source.count : source.maximumCount;
  if (!Number.isInteger(maximum) || Number(maximum) < 1 || Number(maximum) > 32 ||
      ['number_field', 'collection_field'].includes(String(kind)) && !key(source.fieldKey) ||
      ['role_members', 'collection_field'].includes(String(kind)) && !key(source.itemFieldKey) || kind === 'role_members' && !id(source.roleId)) return invalid();
  const reducers: Record<string, string> = {};
  if (Object.keys(value.outputReducers).length > 32) return invalid();
  for (const [field, reducer] of Object.entries(value.outputReducers)) {
    if (!key(field) || typeof reducer !== 'string' || !['first', 'last', 'sum', 'min', 'max', 'all_true', 'any_true', 'append'].includes(reducer)) return invalid();
    reducers[field] = reducer;
  }
  return { modeKey: value.modeKey as WorkflowMultiInstancePolicy['modeKey'], source: { ...source } as WorkflowMultiInstancePolicy['source'],
    requiredPercentage: Number(value.requiredPercentage), emptyModeKey: value.emptyModeKey as WorkflowMultiInstancePolicy['emptyModeKey'], outputReducers: reducers };
}

/** 发起人来源闭合为当前作用域身份，不能夹带任意用户数组或租户标识。 */
export function readWorkflowSubprocessStarter(value: unknown, multi?: WorkflowMultiInstancePolicy): WorkflowSubprocessStarterPolicy {
  if (!object(value) || Object.keys(value).length < 1 || Object.keys(value).length > 2 || Object.keys(value).some(name => !['source', 'resultFieldKey'].includes(name)) || !object(value.source)) return invalid();
  const source = value.source; const expected = source.kindKey === 'specified_user' ? ['kindKey', 'userId'] : source.kindKey === 'field_user' ? ['kindKey', 'fieldKey'] : ['parent', 'item_user'].includes(String(source.kindKey)) ? ['kindKey'] : [];
  if (!expected.length || Object.keys(source).length !== expected.length || Object.keys(source).some(name => !expected.includes(name)) ||
      source.kindKey === 'specified_user' && !id(source.userId) || source.kindKey === 'field_user' && !key(source.fieldKey) ||
      source.kindKey === 'item_user' && multi?.source.kindKey !== 'role_members' || value.resultFieldKey !== undefined && !key(value.resultFieldKey)) return invalid();
  return { source: { ...source } as WorkflowSubprocessStarterPolicy['source'], ...(value.resultFieldKey ? { resultFieldKey: String(value.resultFieldKey) } : {}) };
}

/** 超时只选择固定路径，不接受表达式、脚本或未声明的暂停语义。 */
export function readWorkflowSubprocessTimeout(value: unknown): WorkflowSubprocessTimeoutPolicy {
  if (!object(value) || Object.keys(value).some(name => !['dueAfterMinutes', 'pauseModeKey', 'actionKey', 'nextNodeKey'].includes(name)) ||
      !Number.isInteger(value.dueAfterMinutes) || Number(value.dueAfterMinutes) < 1 || Number(value.dueAfterMinutes) > 525600 ||
      !['freeze', 'elapsed'].includes(String(value.pauseModeKey)) || !['continue', 'route'].includes(String(value.actionKey)) ||
      (value.actionKey === 'route' ? !key(value.nextNodeKey) : value.nextNodeKey !== undefined)) return invalid();
  return { dueAfterMinutes: Number(value.dueAfterMinutes), pauseModeKey: value.pauseModeKey as 'freeze' | 'elapsed', actionKey: value.actionKey as 'continue' | 'route',
    ...(value.actionKey === 'route' ? { nextNodeKey: String(value.nextNodeKey) } : {}) };
}
