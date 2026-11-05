import type { SupportedLocale } from '@fullnet/admin-i18n';
const zh = {
  title: '配额用量', refresh: '刷新用量', close: '关闭', seats: '成员席位', storage: '文件存储', seatUnit: '单位：个', storageUnit: '单位：字节',
  limit: '账面上限', used: '已确认占用', reserved: '尚未确认预留', remaining: '账面余额', readAt: '读取时间', active: '企业为活动状态', inactive: '企业为非活动状态',
  hint: '账面余额按上限减去已用和预留计算。新增操作仍需通过当前企业、订阅及业务授权校验。',
  missing: '当前未配置此项指标，不能视为零用量或无限额度。', exceeded: '已用与预留超过账面上限，请核对当前订阅及用量。',
  failed: '读取失败；已有结果为上次成功读取的数据，请重新读取。', forbidden: '当前会话无权读取该企业配额，请核对权限。',
  absent: '当前企业不存在或已不可读取，请刷新企业列表。', mismatch: '返回用量无法与原企业及正式指标核对，请重新读取。'
};
const en: Record<keyof typeof zh, string> = {
  title: 'Quota usage', refresh: 'Refresh usage', close: 'Close', seats: 'Member seats', storage: 'File storage', seatUnit: 'Unit: seats', storageUnit: 'Unit: bytes',
  limit: 'Ledger limit', used: 'Confirmed usage', reserved: 'Pending reservations', remaining: 'Ledger balance', readAt: 'Read at', active: 'Tenant is active', inactive: 'Tenant is inactive',
  hint: 'Ledger balance is the limit minus used and reserved amounts. New operations still require current tenant, subscription and business authorization.',
  missing: 'This metric is not configured. It does not imply zero usage or an unlimited allowance.', exceeded: 'Used and reserved amounts exceed the ledger limit. Review the subscription and usage.',
  failed: 'The read failed. Any existing result is the last successful read; refresh to check.', forbidden: 'This session cannot read the tenant quota. Check your permissions.',
  absent: 'The tenant is missing or unavailable. Refresh the tenant list.', mismatch: 'The usage response does not match the original tenant and official metrics. Reload to check.'
};
export const tenantQuotaText = (locale: SupportedLocale, key: keyof typeof zh): string => (locale === 'en-US' ? en : zh)[key];
