const zh = {
  open: '申请助手完成', title: '独立审批后完成日程', hint: '仅完成当前准确版本，不修改内容或时间。此静态流程不会调用模型。选择运行配置后创建建议，由另一位有权人员审批，再恢复原运行。',
  model: '运行配置', create: '创建完成建议', refresh: '刷新原运行', request: '申请独立审批', resume: '恢复已批准运行', reconcile: '按原操作核对回执',
  confirm: '确认提交当前日程及版本的完成建议？申请审批本身不会改变日程。', approval: '审批ID（交由另一位操作者在AI运行页面核对）',
  run: '原运行', operation: '原操作', target: '日程', version: '准确版本', close: '关闭',
  queued: '原运行排队中', running: '原运行处理中', awaiting_approval: '等待独立审批', runAccepted: '运行已提交意图，请核对接收方实际结果',
  accepted: '意图已接受，尚未确认完成', unknown: '实际结果未知，只能按原操作对账', completed: '接收方已确认本操作完成', rejected: '接收方已拒绝本操作',
  unavailable: '当前操作或权限已不可用，请刷新核对。不要以新操作重试未知结果。', reason: '接收方原因', actualVersion: '实际完成版本',
  authorization_unavailable: '原会话或写权限已失效', intent_expired: '原意图已过期', target_changed: '目标状态或准确版本已改变',
  closeHint: '关闭窗口不会取消已提交的原操作。', receiptHint: '运行完成不代表日程完成，请核对接收方准确回执。', review: '前往AI运行页面', failed: '原运行已停止，请核对原操作结果'
};
const en: Record<keyof typeof zh, string> = {
  open: 'Request assistant completion', title: 'Complete after independent approval', hint: 'Complete only this exact pending version without editing content or time. This static flow does not call a model. Select a run configuration, create a proposal, obtain independent approval, then resume the original run.',
  model: 'Run configuration', create: 'Create completion proposal', refresh: 'Refresh original run', request: 'Request independent approval', resume: 'Resume approved run', reconcile: 'Check original operation receipt',
  confirm: 'Submit this schedule and exact version for independent approval? Requesting approval does not change the schedule.', approval: 'Approval ID (another operator reviews it on the AI runs page)',
  run: 'Original run', operation: 'Original operation', target: 'Schedule', version: 'Exact version', close: 'Close',
  queued: 'Original run queued', running: 'Original run processing', awaiting_approval: 'Awaiting independent approval', runAccepted: 'The run submitted an intent; check the receiver result',
  accepted: 'Intent accepted; completion not confirmed', unknown: 'Actual result unknown; reconcile only the original operation', completed: 'Receiver confirmed this operation completed', rejected: 'Receiver rejected this operation',
  unavailable: 'The operation or access is unavailable. Refresh the original operation; do not create another operation to retry an unknown result.', reason: 'Receiver reason', actualVersion: 'Completed version',
  authorization_unavailable: 'Original session or write access is unavailable', intent_expired: 'Original intent expired', target_changed: 'Target state or exact version changed',
  closeHint: 'Closing does not cancel an operation already submitted.', receiptHint: 'A completed run does not prove the schedule completed. Check the receiver receipt.', review: 'Open AI runs', failed: 'Original run stopped; check its operation result'
};
/** 完成建议、意图与业务回执分开命名，不能用运行终态翻译成日程已完成。 */
export const personalScheduleCompletionText = (locale: string, key: keyof typeof zh): string => locale === 'en-US' ? en[key] : zh[key];
