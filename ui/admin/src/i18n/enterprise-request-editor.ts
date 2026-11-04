const zh = {
  commandUnavailable:'原记录已变化，请关闭确认窗口并重新读取列表。',
  writeUnconfirmed:'原主单操作结果尚未确认。取消等待不代表服务端已回滚，请先核对原申请，再决定是否进行新的写入。',
  reloadRecords:'刷新申请列表',checkedResults:'已核对原操作结果',confirmChecked:'确认已核对原申请的操作结果，并允许进行新的写入？此操作不会重发原请求。',
  draft: '草稿', submitted: '已提交', approved: '已批准', rejected: '已驳回', cancelled: '已取消',
  create: '创建主从单据', open: '主从明细', title: '申请单与明细', organization: '所属组织 ID', number: '申请编号', subject: '申请标题',
  applicant: '申请人 ID', total: '申请总金额', status: '状态', lines: '申请明细', add: '添加明细', remove: '移除',
  line: '行号', description: '事项说明', quantity: '数量', price: '单价', amount: '行金额', save: '保存主从单据', close: '关闭',
  failure: '申请单操作未完成，请核对结果后重试。', invalid: '请填写有效的组织、申请人、必填字段及金额；最多 128 行，数量最多四位小数，金额最多两位。',
  saved: '主单和明细已保存。', readonly: '当前状态或权限仅允许查看。', limit: '最多 128 行。金额按十进制字符串提交；保存时同时校验主单和明细版本。'
};
const en: Record<keyof typeof zh, string> = {
  commandUnavailable:'The original record changed. Close this confirmation and reload the list.',
  writeUnconfirmed:'The previous request operation is unconfirmed. Cancelling a request does not prove rollback. Check the original request before making another write.',
  reloadRecords:'Refresh requests',checkedResults:'Original result checked',confirmChecked:'Confirm that you checked the original operation result and want to allow a new write? This does not resend the request.',
  draft: 'Draft', submitted: 'Submitted', approved: 'Approved', rejected: 'Rejected', cancelled: 'Cancelled',
  create: 'Create with lines', open: 'Request lines', title: 'Request and lines', organization: 'Organization ID', number: 'Request number', subject: 'Title',
  applicant: 'Applicant ID', total: 'Total amount', status: 'Status', lines: 'Request lines', add: 'Add line', remove: 'Remove',
  line: 'Line number', description: 'Description', quantity: 'Quantity', price: 'Unit price', amount: 'Line amount', save: 'Save request and lines', close: 'Close',
  failure: 'The request did not finish. Check the outcome before retrying.', invalid: 'Enter valid IDs, required fields and decimals. Up to 128 lines, four quantity decimals and two amount decimals.',
  saved: 'The request and lines were saved.', readonly: 'This status or your permissions allow viewing only.', limit: 'Up to 128 lines. Decimals stay strings; saving checks both request and line versions.'
};
export const enterpriseEditorText = (locale: string, key: keyof typeof zh): string => (locale.startsWith('en') ? en : zh)[key];
