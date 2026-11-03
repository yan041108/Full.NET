const zh = {
  open: '附件与文档', title: '企业申请交付', refresh: '刷新状态', close: '关闭', file: '选择附件',
  upload: '上传附件', attachments: '附件', reports: '报表任务', create: '生成报表', download: '下载',
  remove: '删除', cancel: '取消任务', print: '打印预览', printNow: '打印', empty: '暂无记录',
  failure: '操作未完成，请刷新状态后重试。', invalid: '请选择不超过 10 MiB 的非空文件。',
  masked: '文档金额字段按当前权限提供。', queued: '后台处理中；请刷新查看结果。',
  refreshing: '任务状态将自动刷新。', refreshPaused: '自动刷新已暂停，请刷新状态查看最新结果。',
  sensitive: '此文档包含金额。下载前服务端会再次检查权限和源版本。',
  status: '状态', ready: '就绪', processing: '处理中', failed: '失败', cancelled: '已取消', expired: '已过期', deleted: '已删除',
  queuedStatus: '排队中', deleting: '删除中', unknownStatus: '状态待确认',
  expiresAt: '结果有效期至', expiryUnknown: '有效期待确认',
  expiryPolicy: '到期后请重新生成报表。下载时会再次检查有效期和当前权限。',
  expiredResult: '此结果已过期，不能下载。需要时可重新生成报表。',
  cleanupPending: '结果已不可用，文件正在清理；任务记录会保留。',
  cancelConfirmTitle: '确认取消报表', cancelConfirmDescription: '取消后结果立即不可下载，文件将继续清理。确定取消此任务？', keepTask: '保留任务',
  writeUnconfirmed: '取消结果或任务版本未确认，请先刷新状态，再执行新的写操作。',
  limit: '最多 20 个附件；报表最多 500 行，打印最多 20 行。', more: '下一页', previous: '上一页'
};
const en: Record<keyof typeof zh, string> = {
  open: 'Attachments and documents', title: 'Request delivery', refresh: 'Refresh status', close: 'Close', file: 'Choose attachment',
  upload: 'Upload attachment', attachments: 'Attachments', reports: 'Report tasks', create: 'Generate workbook', download: 'Download',
  remove: 'Delete', cancel: 'Cancel task', print: 'Print preview', printNow: 'Print', empty: 'No records',
  failure: 'The operation did not finish. Refresh status before retrying.', invalid: 'Choose a nonempty file no larger than 10 MiB.',
  masked: 'Document amounts follow your current permissions.', queued: 'Processing in the background. Refresh to check progress.',
  refreshing: 'Task status refreshes automatically.', refreshPaused: 'Automatic refresh is paused. Refresh the status to see the latest result.',
  sensitive: 'This document includes amounts. The server rechecks access and source version before download.',
  status: 'Status', ready: 'Ready', processing: 'Processing', failed: 'Failed', cancelled: 'Cancelled', expired: 'Expired', deleted: 'Deleted',
  queuedStatus: 'Queued', deleting: 'Deleting', unknownStatus: 'Status unconfirmed',
  expiresAt: 'Result expires at', expiryUnknown: 'Expiry unconfirmed',
  expiryPolicy: 'Generate a new workbook after expiry. Expiry and current access are checked again on download.',
  expiredResult: 'This result has expired and cannot be downloaded. Generate a new workbook if needed.',
  cleanupPending: 'The result is unavailable and its file is being cleaned up. The task record is retained.',
  cancelConfirmTitle: 'Confirm workbook cancellation', cancelConfirmDescription: 'The result becomes unavailable immediately after cancellation; file cleanup continues. Cancel this task?', keepTask: 'Keep task',
  writeUnconfirmed: 'The cancellation result or task version is unconfirmed. Refresh the status before another write operation.',
  limit: 'Up to 20 attachments, 500 workbook lines, and 20 printed lines.', more: 'Next', previous: 'Previous'
};
export const enterpriseDeliveryText = (locale: string, key: keyof typeof zh): string =>
  (locale.startsWith('en') ? en : zh)[key];
