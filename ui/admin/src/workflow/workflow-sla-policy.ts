export type WorkflowSlaPolicy = { dueAfterMinutes: number; pauseModeKey: 'elapsed' | 'freeze'; actionKey: 'suspend' | 'auto_reject' | 'auto_pass' };
/** 活动截止与提醒分别配置；系统通过不能伪装为投票、办理或领取。 */
export function readWorkflowSlaPolicy(value: unknown, nodeTypeKey: string): WorkflowSlaPolicy {
  const invalid = (): never => { throw new Error('client.invalid_workflow_sla_policy'); };
  if (!value || typeof value !== 'object' || Array.isArray(value) || !['human.approval', 'human.group_approval', 'human.task', 'human.vote', 'human.claim'].includes(nodeTypeKey)) return invalid();
  const item = value as Record<string, unknown>;
  if (Object.keys(item).some(key => !['dueAfterMinutes', 'pauseModeKey', 'actionKey'].includes(key)) || !Number.isSafeInteger(item.dueAfterMinutes) ||
    Number(item.dueAfterMinutes) < 1 || Number(item.dueAfterMinutes) > 525600 || item.pauseModeKey !== 'elapsed' && item.pauseModeKey !== 'freeze' ||
    item.actionKey !== 'suspend' && item.actionKey !== 'auto_reject' && item.actionKey !== 'auto_pass' ||
    item.actionKey === 'auto_pass' && !['human.approval', 'human.group_approval'].includes(nodeTypeKey)) return invalid();
  return { dueAfterMinutes: Number(item.dueAfterMinutes), pauseModeKey: item.pauseModeKey, actionKey: item.actionKey };
}
