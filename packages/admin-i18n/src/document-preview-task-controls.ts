export const previewTaskControlsZh = {
  'documentPreviewTasks.cancelConfirmTitle': '确认取消预览转换',
  'documentPreviewTasks.cancelledMessage': '预览转换已取消。',
  'documentPreviewTasks.cancelConfirmDescription': '取消后，此任务不能再提交转换结果。已开始的转换可能仍在退出，临时文件由后台释放；人工重试将新建任务。',
  'documentPreviewTasks.writeUnconfirmed': '操作结果或任务版本未确认，请先手动刷新列表。取消、创建和重试不会自动重放。',
  'documentPreviewTasks.refreshPaused': '自动刷新已暂停，请手动刷新列表继续查看最新状态。'
} as const;
export const previewTaskControlsEn: Record<keyof typeof previewTaskControlsZh, string> = {
  'documentPreviewTasks.cancelConfirmTitle': 'Confirm preview cancellation',
  'documentPreviewTasks.cancelledMessage': 'The preview conversion was canceled.',
  'documentPreviewTasks.cancelConfirmDescription': 'This task can no longer commit conversion results after cancellation. A running converter may still be exiting; temporary files are released in the background. A manual retry creates a new task.',
  'documentPreviewTasks.writeUnconfirmed': 'The operation result or task version is unconfirmed. Refresh the list manually. Cancellation, creation and retry are never replayed automatically.',
  'documentPreviewTasks.refreshPaused': 'Automatic refresh is paused. Refresh the list manually to see the latest state.'
};
