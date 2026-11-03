export const importCancellationZh = {
  'importExportTasks.cancel': '取消待执行任务',
  'importExportTasks.cancelConfirmAction': '确定取消',
  'importExportTasks.cancelConfirm': '取消后不能继续执行或恢复，已写入的业务数据会保留。确定取消？',
  'importExportTasks.cancelSuccess': '已取消后续执行',
  'importExportTasks.cancelReload': '取消结果或任务版本未确认，请先刷新详情再操作。',
  'importExportTasks.cancelExecuting': '当前批次正在执行，暂不能取消。请等待批次结束后刷新详情，已写入数据不会回滚。',
  'importExportTasks.cancelDone': '任务已取消，不会继续调度。已写入的业务数据和原回执保留。',
  'importExportTasks.status.canceled': '已取消',
  'taskCenter.action.cancel': '到详情取消'
} as const;
export const importCancellationEn: Record<keyof typeof importCancellationZh, string> = {
  'importExportTasks.cancel': 'Cancel pending task',
  'importExportTasks.cancelConfirmAction': 'Confirm cancellation',
  'importExportTasks.cancelConfirm': 'This task cannot resume after cancellation. Existing business records remain. Cancel?',
  'importExportTasks.cancelSuccess': 'Further execution canceled',
  'importExportTasks.cancelReload': 'The cancellation result or task version is unconfirmed. Refresh details before acting.',
  'importExportTasks.cancelExecuting': 'A batch is executing and cannot be canceled. Refresh after it finishes. Existing records will not be rolled back.',
  'importExportTasks.cancelDone': 'This task is canceled and will not be scheduled again. Existing business records and receipts remain.',
  'importExportTasks.status.canceled': 'Canceled',
  'taskCenter.action.cancel': 'Open cancellation details'
};
