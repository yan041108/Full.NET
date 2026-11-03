const zh = {
  draft: '草稿', submitted: '已提交', approved: '已批准', rejected: '已驳回', cancelled: '已取消',
  create: '创建主从单据', open: '主从明细', title: '申请单与明细', organization: '所属组织 ID', number: '申请编号', subject: '申请标题',
  applicant: '申请人 ID', total: '申请总金额', status: '状态', lines: '申请明细', add: '添加明细', remove: '移除',
  line: '行号', description: '事项说明', quantity: '数量', price: '单价', amount: '行金额', save: '保存主从单据', close: '关闭',
  failure: '申请单操作未完成，请核对结果后重试。', invalid: '请填写有效的组织、申请人、必填字段及金额；最多 128 行，数量最多四位小数，金额最多两位。',
  saved: '主单和明细已保存。', readonly: '当前状态或权限仅允许查看。', limit: '最多 128 行。金额按十进制字符串提交；保存时同时校验主单和明细版本。'
};
const en: Record<keyof typeof zh, string> = {
  draft: 'Draft', submitted: 'Submitted', approved: 'Approved', rejected: 'Rejected', cancelled: 'Cancelled',
  create: 'Create with lines', open: 'Request lines', title: 'Request and lines', organization: 'Organization ID', number: 'Request number', subject: 'Title',
  applicant: 'Applicant ID', total: 'Total amount', status: 'Status', lines: 'Request lines', add: 'Add line', remove: 'Remove',
  line: 'Line number', description: 'Description', quantity: 'Quantity', price: 'Unit price', amount: 'Line amount', save: 'Save request and lines', close: 'Close',
  failure: 'The request did not finish. Check the outcome before retrying.', invalid: 'Enter valid IDs, required fields and decimals. Up to 128 lines, four quantity decimals and two amount decimals.',
  saved: 'The request and lines were saved.', readonly: 'This status or your permissions allow viewing only.', limit: 'Up to 128 lines. Decimals stay strings; saving checks both request and line versions.'
};
export const enterpriseEditorText = (locale: string, key: keyof typeof zh): string => (locale.startsWith('en') ? en : zh)[key];
