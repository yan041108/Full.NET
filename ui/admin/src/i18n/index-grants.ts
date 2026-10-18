import type { SupportedLocale } from '@fullnet/admin-i18n';
const zh = {
  title: '增量索引收费授权', open: '管理增量收费授权', hint: '默认不授权。开启后，原登录可在明确期限和额度内为本知识库新确认来源创建收费任务。知识库批准变化、原登录失效、撤权或到期即停止后续阶段；不接管未知收费任务。已发生费用不能撤回，失败不返还授权次数或预留额度。',
  minutes: '授权分钟数（2–1440）', documents: '最多文档版本任务数（1–64）', tokens: '每任务累计输入预留上限（1–1000000）',
  refresh: '刷新', create: '确认授权收费派发', revoke: '撤销授权', close: '关闭', previous: '上一页', next: '下一页',
  expires: '到期时间', admitted: '已准入 / 上限', version: '修订', state: '状态', revoked: '已撤销', pending: '须实时复核',
  confirm: '确认允许原登录在下列边界内为最新已确认来源产生模型费用？此数值限制累计输入预留，不是金额硬限。', revokeConfirm: '确认停止此授权的后续派发？已发生或未知费用不会自动退还。',
  stale: '操作结果未知或发生修订冲突；请刷新当前授权后再操作。', saved: '授权已保存。', stopped: '授权已撤销。'
};
const en: Record<keyof typeof zh, string> = {
  title: 'Paid incremental indexing grants', open: 'Manage paid indexing grants', hint: 'No grant is enabled by default. The original login may create paid tasks for newly confirmed sources within explicit time and quota bounds. Approval changes, login expiry, permission revocation or expiry stop subsequent stages. Unknown paid tasks are never taken over. Incurred costs cannot be undone; failures do not restore grant counts or reservations.',
  minutes: 'Grant minutes (2–1440)', documents: 'Maximum document-version tasks (1–64)', tokens: 'Cumulative input reservation per task (1–1000000)',
  refresh: 'Refresh', create: 'Authorize paid dispatch', revoke: 'Revoke grant', close: 'Close', previous: 'Previous', next: 'Next',
  expires: 'Expires', admitted: 'Admitted / limit', version: 'Revision', state: 'State', revoked: 'Revoked', pending: 'Requires live checks',
  confirm: 'Allow the original login to incur model charges for the latest confirmed sources within these bounds? Input reservations are bounded; this is not a hard monetary cap.', revokeConfirm: 'Stop subsequent dispatch under this grant? Incurred or unknown costs are not automatically refunded.',
  stale: 'The outcome is unknown or a revision conflicts. Refresh the grants before another operation.', saved: 'Grant saved.', stopped: 'Grant revoked.'
};
export const indexGrantText = (locale: SupportedLocale, key: keyof typeof zh) => (locale === 'en-US' ? en : zh)[key];
