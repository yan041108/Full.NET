export interface WorkflowActivityListener {
  listenerKey: string;
  eventKey: 'completed' | 'rejected';
  actionKey: 'notify.initiator';
  actionVersion: 1;
}

/** 只接纳服务端已注册的固定版本动作，来源脚本和未知属性不能被静默丢弃。 */
export function readWorkflowActivityListeners(value: unknown): WorkflowActivityListener[] {
  const invalid = () => { throw new Error('client.invalid_workflow_activity_listeners'); };
  if (!Array.isArray(value) || value.length > 8) return invalid();
  const keys = new Set<string>();
  return value.map((item: unknown) => {
    if (typeof item !== 'object' || item === null || Array.isArray(item)) return invalid();
    const row = item as Record<string, unknown>;
    if (Object.keys(row).length !== 4 || Object.keys(row).some(key => !['listenerKey', 'eventKey', 'actionKey', 'actionVersion'].includes(key)) ||
      typeof row.listenerKey !== 'string' || !/^[A-Za-z][A-Za-z0-9_.-]{0,63}$/.test(row.listenerKey) || keys.has(row.listenerKey) ||
      typeof row.eventKey !== 'string' || !['completed', 'rejected'].includes(row.eventKey) || row.actionKey !== 'notify.initiator' || row.actionVersion !== 1) return invalid();
    keys.add(row.listenerKey);
    return { listenerKey: row.listenerKey, eventKey: row.eventKey as WorkflowActivityListener['eventKey'], actionKey: 'notify.initiator', actionVersion: 1 };
  });
}
