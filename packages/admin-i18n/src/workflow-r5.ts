export const workflowR5Zh = {
  'workflowR5.simulation': '发布前仿真', 'workflowR5.run': '读取并仿真',
  'workflowR5.simulationHelp': '仿真服务器已保存草稿（修订 {revision}），请先保存设计修改。以当前用户作为发起人，样例值仅用于本次求值；不会发布或启动流程。',
  'workflowR5.selectionHelp': '选择本次仿真样例的发起人员；范围固定于当前保存草稿和表单版本，不会启动流程。',
  'workflowR5.boundaries': '复用实际规则和当前成员解析，止于各分支首个人工、外部、子流程或汇合边界。成员数量只在读取时有效；不假定后续输入、审批或副作用成功，结果不能作为运行授权。',
  'workflowR5.loading': '正在读取…', 'workflowR5.failed': '读取或求值失败。请检查当前权限、已保存修订、固定表单及必填样例后重新读取。',
  'workflowR5.outcome': '求值结果', 'workflowR5.members': '当前成员 {count} 人',
  'workflowR5.operations': '节点停留与运营状态',
  'workflowR5.operationsHelp': '停留为当前激活的墙钟秒数，包含暂停时间。冻结或暂停 SLA 不计逾期；积压包含未来、已租用及失败的真实工作，可领取状态按观察时刻判断。恢复须进入原资源，重新核对授权、修订及未知副作用。',
  'workflowR5.kind': '观察范围', 'workflowR5.steps': '节点停留与 SLA', 'workflowR5.backlog': '耐久工作积压',
  'workflowR5.minimum': '最少停留秒数', 'workflowR5.overdueOnly': '仅当前逾期', 'workflowR5.refresh': '重新读取',
  'workflowR5.recovery': '进入恢复任务', 'workflowR5.close': '关闭', 'workflowR5.observed': '观察时刻',
  'workflowR5.total': '共 {count} 项', 'workflowR5.node': '流程与节点', 'workflowR5.dwell': '停留秒数', 'workflowR5.sla': 'SLA 截止与状态',
  'workflowR5.inspect': '查看实例与恢复边界', 'workflowR5.overdue': '当前逾期', 'workflowR5.paused': '已冻结',
  'workflowR5.schedule': '调度状态', 'workflowR5.ready': '可领取', 'workflowR5.attempts': '尝试次数', 'workflowR5.lease': '租约截止',
  'workflowR5.previous': '上一页', 'workflowR5.next': '下一页', 'workflowR5.yes': '是', 'workflowR5.no': '否'
} as const;
export const workflowR5En: Record<keyof typeof workflowR5Zh, string> = {
  'workflowR5.simulation': 'Pre-publication simulation', 'workflowR5.run': 'Read and simulate',
  'workflowR5.simulationHelp': 'Simulate saved server draft revision {revision}. Save design changes first. The current user is the initiator. Sample values are evaluated only for this request; no version or instance is created.',
  'workflowR5.selectionHelp': 'Choose initiator selections for this sample. The range is fixed to the saved draft and form version; no workflow is started.',
  'workflowR5.boundaries': 'Uses runtime rules and current members, stopping at each branch’s first human, external, subprocess or join boundary. Members are current only at read time. Later input, decisions and side effects are not assumed; this result grants no runtime access.',
  'workflowR5.loading': 'Reading…', 'workflowR5.failed': 'Read or evaluation failed. Check current permissions, saved revision, fixed form and required sample values, then read again.',
  'workflowR5.outcome': 'Outcome', 'workflowR5.members': '{count} current members',
  'workflowR5.operations': 'Node dwell and operations',
  'workflowR5.operationsHelp': 'Dwell is wall-clock time for the current activation, including pauses. Frozen or paused SLA items are not overdue. Backlog includes future, leased and failed work; readiness is evaluated at observation time. Recovery requires fresh resource authorization, revisions and unknown-side-effect checks.',
  'workflowR5.kind': 'Scope', 'workflowR5.steps': 'Node dwell and SLA', 'workflowR5.backlog': 'Durable work backlog',
  'workflowR5.minimum': 'Minimum dwell seconds', 'workflowR5.overdueOnly': 'Currently overdue only', 'workflowR5.refresh': 'Read again',
  'workflowR5.recovery': 'Open recovery tasks', 'workflowR5.close': 'Close', 'workflowR5.observed': 'Observed at',
  'workflowR5.total': '{count} items', 'workflowR5.node': 'Workflow and node', 'workflowR5.dwell': 'Dwell seconds', 'workflowR5.sla': 'SLA deadline and state',
  'workflowR5.inspect': 'Inspect instance and recovery boundaries', 'workflowR5.overdue': 'Currently overdue', 'workflowR5.paused': 'Frozen',
  'workflowR5.schedule': 'Schedule', 'workflowR5.ready': 'Ready to claim', 'workflowR5.attempts': 'Attempts', 'workflowR5.lease': 'Lease expires',
  'workflowR5.previous': 'Previous', 'workflowR5.next': 'Next', 'workflowR5.yes': 'Yes', 'workflowR5.no': 'No'
};
