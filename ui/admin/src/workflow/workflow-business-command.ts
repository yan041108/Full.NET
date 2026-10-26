export interface WorkflowBusinessCommandConfiguration {
  businessCommandPolicy: { actionKey: string; actionVersion: number };
  boundBusinessActionHash?: string;
}
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
/** 业务节点只能选择固定动作，目标及快照由实际业务请求授权确定。 */
export function readWorkflowBusinessCommand(value: unknown): WorkflowBusinessCommandConfiguration {
  const invalid = (): never => { throw new Error('client.invalid_workflow_business_command'); };
  if (!object(value) || !object(value.businessCommandPolicy)) return invalid();
  const policy = value.businessCommandPolicy;
  if (Object.keys(policy).length !== 2 || Object.keys(policy).some(key => !['actionKey', 'actionVersion'].includes(key)) ||
    typeof policy.actionKey !== 'string' || !/^[a-z][a-z0-9_.-]{0,63}$/u.test(policy.actionKey) ||
    !Number.isSafeInteger(policy.actionVersion) || Number(policy.actionVersion) < 1 || Number(policy.actionVersion) > 65535) return invalid();
  if (value.boundBusinessActionHash !== undefined && (typeof value.boundBusinessActionHash !== 'string' || !/^[a-f0-9]{64}$/u.test(value.boundBusinessActionHash))) return invalid();
  return { businessCommandPolicy: { actionKey: policy.actionKey, actionVersion: Number(policy.actionVersion) },
    ...(value.boundBusinessActionHash === undefined ? {} : { boundBusinessActionHash: value.boundBusinessActionHash as string }) };
}
