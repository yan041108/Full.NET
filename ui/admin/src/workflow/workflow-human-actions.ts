export const workflowHumanActions = ['approve', 'reject', 'return', 'transfer', 'delegate', 'countersign', 'remind'] as const;
export const workflowTaskActions = ['complete', 'return', 'transfer', 'delegate', 'remind'] as const;
export const workflowClaimActions = ['complete', 'release', 'transfer', 'delegate', 'remind'] as const;
export const workflowVoteActions = ['vote', 'transfer', 'delegate', 'remind'] as const;
const sourceActions: Readonly<Record<string, string>> = {
  pass: 'approve', reject: 'reject', back: 'return', transfer: 'transfer', delegate: 'delegate', addSign: 'countersign', urge: 'remind'
};

/** 仅映射受控动作键；来源按钮名称不参与授权，遗漏的显式动作保持禁用。 */
export function readWorkflowOperationPermissions(value: unknown, kind = 'human.approval'): Record<string, boolean> {
  const invalid = (): never => { throw new Error('client.invalid_workflow_approval_policy'); };
  const known: readonly string[] = kind === 'human.claim' ? workflowClaimActions : kind === 'human.task' ? workflowTaskActions : kind === 'human.vote' ? workflowVoteActions : workflowHumanActions;
  const result: Record<string, boolean> = {};
  if (Array.isArray(value)) {
    if (value.length > known.length) return invalid();
    for (const entry of value) {
      if (typeof entry !== 'object' || entry === null || Array.isArray(entry)
        || Object.keys(entry).some(key => !['key', 'name', 'enable'].includes(key))
        || typeof entry.key !== 'string' || typeof entry.enable !== 'boolean'
        || entry.name !== undefined && (typeof entry.name !== 'string' || entry.name.length > 128)) return invalid();
      const key = entry.key === 'pass' && (kind === 'human.task' || kind === 'human.claim') ? 'complete' : entry.key === 'pass' && kind === 'human.vote' ? 'vote' : entry.key === 'release' && kind === 'human.claim' ? 'release' : sourceActions[entry.key];
      if (key === undefined || !known.includes(key) || key in result) return invalid();
      result[key] = entry.enable;
    }
    return result;
  }
  if (typeof value !== 'object' || value === null) return invalid();
  for (const [key, enabled] of Object.entries(value)) {
    if (!known.includes(key) || typeof enabled !== 'boolean') return invalid();
    result[key] = enabled;
  }
  return result;
}
