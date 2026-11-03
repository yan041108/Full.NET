const zh = {
  linear: '会话标题建议与人工审批', parallel: '并行评审会话标题', target: '选择本人目标聊天会话',
  targetHint: '仅处理所选会话的标题；并行分支共用本次运行额度，重命名仍需人工审批。',
  previous: '上一页', next: '下一页', refresh: '刷新', unavailable: '当前不可用，请刷新后重试。',
  proposal: '待审批提案', readProposal: '查看待审批提案', current: '当前标题', proposed: '拟议标题',
  request: '发起人工审批', confirmRequest: '确认将此提案提交人工审批？提交审批不会执行重命名。',
  requested: '审批已创建。待有权人员作出决定后，由原请求人恢复运行。', approval: '人工审批',
  approvalId: '审批 ID', lookup: '读取审批', approve: '批准', reject: '拒绝',
  confirmApprove: '确认批准当前展示的变更？批准后仍需恢复原运行才能执行。',
  confirmReject: '确认拒绝当前展示的变更？', decision: '审批状态', pending: '待决定', approved: '已批准', rejected: '已拒绝',
  consumed: '审批已消费；实际执行结果请查看对应运行。', notConsumed: '审批尚未消费。', expires: '有效期至',
  unknown: '未知', validation: '请先选择模型和本人目标聊天会话。', action: '动作', scope: '影响范围',
  decisionUnknown: '决定结果未确认，请重新读取审批状态。', readOnly: '当前只有读取权限。',
  approvalTarget: '操作目标', approvalChange: '变更内容'
};
const en: Record<keyof typeof zh, string> = {
  linear: 'Chat title proposal and approval', parallel: 'Parallel chat title review', target: 'Choose your target chat session',
  targetHint: 'Only the selected title is changed. Parallel roles share this run budget. Renaming requires human approval.',
  previous: 'Previous', next: 'Next', refresh: 'Refresh', unavailable: 'Unavailable. Refresh before retrying.',
  proposal: 'Proposal awaiting approval', readProposal: 'Read pending proposal', current: 'Current title', proposed: 'Proposed title',
  request: 'Request human approval', confirmRequest: 'Submit this proposal for human approval? This does not rename the session.',
  requested: 'Approval created. After an authorized decision, the original requester can resume the run.', approval: 'Human approval',
  approvalId: 'Approval ID', lookup: 'Read approval', approve: 'Approve', reject: 'Reject',
  confirmApprove: 'Approve the displayed change? The original run must still be resumed to execute it.',
  confirmReject: 'Reject the displayed change?', decision: 'Approval state', pending: 'Pending', approved: 'Approved', rejected: 'Rejected',
  consumed: 'Approval consumed. Check the original run for the execution result.', notConsumed: 'Approval has not been consumed.', expires: 'Expires at',
  unknown: 'Unknown', validation: 'Choose a model and one of your chat sessions first.', action: 'Action', scope: 'Scope',
  decisionUnknown: 'The decision outcome is unconfirmed. Read the approval again.', readOnly: 'You currently have read access.',
  approvalTarget: 'Target', approvalChange: 'Change'
};
export const aiWorkflowText = (locale: string, key: keyof typeof zh): string => (locale.startsWith('en') ? en : zh)[key];
