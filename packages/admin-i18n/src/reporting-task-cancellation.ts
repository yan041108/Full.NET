export const reportCancellationZh = {
  'reportingExportTasks.cancel': '取消待执行导出',
  'reportingExportTasks.cancelConfirm': '取消后不再生成导出文件。确定取消此排队任务？',
  'reportingExportTasks.cancelConfirmAction': '确定取消',
  'reportingExportTasks.cancelSuccess': '已停止后续导出',
  'reportingExportTasks.cancelReload': '取消结果或任务版本未确认，请先刷新详情再操作。',
  'reportingExportTasks.cancelDone': '任务已取消，不会继续调度，也没有可下载结果。',
  'reportingExportTasks.cancelProcessing': '文件正在生成，暂不能取消。请等待当前生成结束后刷新。',
  'reportingExportTasks.status.canceled': '已取消'
} as const;
export const reportCancellationEn: Record<keyof typeof reportCancellationZh, string> = {
  'reportingExportTasks.cancel': 'Cancel pending export',
  'reportingExportTasks.cancelConfirm': 'No export file will be generated. Cancel this queued task?',
  'reportingExportTasks.cancelConfirmAction': 'Confirm cancellation',
  'reportingExportTasks.cancelSuccess': 'Further export canceled',
  'reportingExportTasks.cancelReload': 'The cancellation result or task version is unconfirmed. Refresh details before acting.',
  'reportingExportTasks.cancelDone': 'This task is canceled and will not be scheduled again. No result is available for download.',
  'reportingExportTasks.cancelProcessing': 'A file is being generated and cannot be canceled. Refresh after generation finishes.',
  'reportingExportTasks.status.canceled': 'Canceled'
};
