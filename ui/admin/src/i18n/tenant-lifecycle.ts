const zh = {
  title: '企业生命周期', status: '当前状态', Active: '正常', Suspended: '已暂停', Closing: '关闭中', Closed: '已关闭',
  inactive: '运营开关已停用', refresh: '刷新状态', suspend: '暂停企业', reactivate: '恢复企业', close: '关闭企业',
  cancel: '取消', confirm: '确认操作', failed: '企业状态读取或操作失败，请刷新后重试。', success: '企业状态已更新。',
  suspendHint: '暂停企业访问并保留数据；之后可恢复。', reactivateHint: '恢复前将重新检查当前套餐和订阅条件。',
  closeHint: '关闭后不可恢复，保留历史数据。本操作不会自动退款或删除全局账号。',
  identifier: '输入企业编码以确认关闭', conflict: '企业状态已变化，请查看最新状态后重新确认。', version: '版本', readOnly: '当前权限仅允许查看。'
} as const;
const en: Record<keyof typeof zh, string> = {
  title: 'Organization lifecycle', status: 'Current status', Active: 'Active', Suspended: 'Suspended', Closing: 'Closing', Closed: 'Closed',
  inactive: 'Operational access disabled', refresh: 'Refresh status', suspend: 'Suspend organization', reactivate: 'Reactivate organization', close: 'Close organization',
  cancel: 'Cancel', confirm: 'Confirm action', failed: 'Could not read or update the organization. Refresh to retry.', success: 'Organization status updated.',
  suspendHint: 'Suspend organization access and retain its data. It can be reactivated later.', reactivateHint: 'Current package and subscription requirements will be checked again.',
  closeHint: 'Closure cannot be undone. Historical data is retained. This action does not refund payments or delete global accounts.',
  identifier: 'Enter the organization identifier to confirm closure', conflict: 'The organization changed. Review its current status and confirm again.', version: 'Version', readOnly: 'Your permissions allow viewing only.'
};
export function tenantLifecycleText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
