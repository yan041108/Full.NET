import type { WorkflowFormField } from '@fullnet/client-contracts';
/** 派生结果只绑定显式可写布尔字段；结构检查不替代服务端发布时的Schema绑定。 */
export function readWorkflowResultCallback(config: Record<string, unknown>, kind: string, fields?: readonly WorkflowFormField[]): string | undefined {
  if (!Object.hasOwn(config, 'resultCallbackFieldKey')) return undefined;
  const invalid = (): never => { throw new Error('client.invalid_workflow_result_callback'); };
  const key = config.resultCallbackFieldKey; const grants = config.fieldPolicies;
  if (!['human.approval', 'human.group_approval', 'human.task', 'human.vote', 'human.claim'].includes(kind) || typeof key !== 'string' ||
    !/^[A-Za-z][A-Za-z0-9_.-]{0,63}$/u.test(key) || ['__proto__', 'prototype', 'constructor'].includes(key) ||
    !grants || typeof grants !== 'object' || Array.isArray(grants) || !Object.hasOwn(grants, key)) return invalid();
  const access = (grants as Record<string, unknown>)[key]; if (access !== 'editable' && access !== 'required') return invalid();
  if (fields !== undefined) { const matches = fields.filter(field => field.fieldKey === key); if (matches.length !== 1 || matches[0]!.fieldTypeKey !== 'switch') return invalid(); }
  return key;
}
