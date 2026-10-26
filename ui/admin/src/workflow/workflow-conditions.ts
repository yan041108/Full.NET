export interface WorkflowConditionConfig {
  readonly fieldKey?: string;
  readonly operator: string;
  readonly value?: unknown;
  readonly conditions?: readonly WorkflowConditionConfig[];
}

const operators = new Set(['equals', 'notEquals', 'greaterThan', 'greaterThanOrEqual', 'lessThan',
  'lessThanOrEqual', 'isEmpty', 'isNotEmpty', 'in', 'notIn', 'containsAny', 'containsAll']);
const keyPattern = /^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u;
const invalid = (): never => { throw new Error('client.invalid_workflow_gateway'); };

/** 与服务端共享同一有界形状；字段类型和实际候选值仍由发布版本绑定校验。 */
export function readWorkflowCondition(value: unknown): WorkflowConditionConfig {
  let remaining = 64;
  function read(candidate: unknown, depth: number): WorkflowConditionConfig {
    if (depth >= 8 || --remaining < 0 || typeof candidate !== 'object' || candidate === null || Array.isArray(candidate)) return invalid();
    const item = candidate as Record<string, unknown>;
    if (item.operator === 'and' || item.operator === 'or') {
      if (Object.keys(item).some(key => !['operator', 'conditions'].includes(key)) || !Array.isArray(item.conditions)
        || item.conditions.length < 2 || item.conditions.length > 16) return invalid();
      return { operator: item.operator, conditions: item.conditions.map(child => read(child, depth + 1)) };
    }
    if (typeof item.fieldKey !== 'string' || !keyPattern.test(item.fieldKey)
      || typeof item.operator !== 'string' || !operators.has(item.operator)) return invalid();
    const empty = ['isEmpty', 'isNotEmpty'].includes(item.operator);
    if (Object.keys(item).some(key => !['fieldKey', 'operator', ...(empty ? [] : ['value'])].includes(key))
      || (empty ? 'value' in item : item.value === undefined)) return invalid();
    if (['in', 'notIn', 'containsAny', 'containsAll'].includes(item.operator)) {
      if (!Array.isArray(item.value) || item.value.length < 1 || item.value.length > 32
        || item.value.some(entry => !['string', 'number', 'boolean'].includes(typeof entry) || typeof entry === 'number' && !Number.isFinite(entry))
        || new Set(item.value.map(entry => JSON.stringify(entry))).size !== item.value.length) return invalid();
    }
    return { fieldKey: item.fieldKey, operator: item.operator,
      ...(empty ? {} : { value: JSON.parse(JSON.stringify(item.value)) as unknown }) };
  }
  return read(value, 0);
}

/** 树形设计器保留旧叶子卡片，并为组合树保存完整表达式。 */
export function projectWorkflowCondition(condition: WorkflowConditionConfig): Record<string, unknown> {
  return condition.conditions === undefined ? { ...condition } : { condition };
}
