const en = { title: 'Transfer owner responsibility', action: 'Transfer owner', hint: 'The current owner becomes an administrator. Business role permissions remain managed separately.',
  initialize: 'No owner responsibility exists yet. An authorized platform administrator must verify their identity to establish the first owner.',
  source: 'Current owner', target: 'Receiving member', version: 'Version', password: 'Current password', totp: 'Current TOTP code',
  loading: 'Reading current owner and member…', saving: 'Submitting this operation…', confirm: 'Verify and transfer', close: 'Close',
  refresh: 'Read current members', check: 'Check original operation', unknown: 'The result is unknown. Check the original operation before taking another action.',
  missing: 'No committed receipt is visible yet. The original request may still be running; do not submit it again.',
  denied: 'Your current account cannot establish or transfer this owner responsibility.', failed: 'The operation could not be completed. Review the current members.',
  success: 'The owner responsibility was transferred.', operation: 'Original operation', none: 'Not established', limit: 'Review your pending ownership operations before starting another.' };
const zh: typeof en = { title: '交接所有者职责', action: '交接所有者', hint: '原所有者转为管理员；业务角色权限仍通过角色授权独立管理。',
  initialize: '当前尚无所有者职责，需要具备资格的平台管理员验证身份后建立首位所有者。', source: '当前所有者', target: '接收成员', version: '版本',
  password: '当前密码', totp: '当前 TOTP 验证码', loading: '正在读取当前所有者和接收成员…', saving: '正在提交本次操作…', confirm: '验证身份并交接', close: '关闭',
  refresh: '读取当前成员', check: '核对原操作', unknown: '本次结果未知，请先核对原操作，不要再次提交。',
  missing: '暂未读取到已提交回执，原请求可能仍在执行，请勿再次提交。', denied: '当前账号不能建立或交接这项所有者职责。',
  failed: '操作未完成，请重新核对当前成员。', success: '所有者职责已交接。', operation: '原操作标识', none: '尚未建立', limit: '请先核对尚未确认的所有者交接操作。' };
export const tenantOwnershipText = (locale: string, key: keyof typeof en) => (locale.startsWith('zh') ? zh : en)[key];
