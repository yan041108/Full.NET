import type { SupportedLocale } from '@fullnet/admin-i18n';
const zh = {
  previewTitle: '全局配额预检', previewMetric: '预检指标', previewStart: '预检本批', previewNext: '预检下一批', previewReset: '从头重新预检',
  previewHint: '检查所有活动企业的用量基线，每批最多 25 家。此入口只统计候选，不会修复或写入配额。',
  previewSkipped: '存在预留或尚未结算的业务操作时会跳过对应企业。各批独立读取，结果不是全局一致快照，也不是全部企业总数。',
  previewBatch: '当前批次', previewCandidates: '本批候选数', previewApplied: '实际修复数', previewSamples: '本批候选企业标识样本',
  previewComplete: '当前扫描已到末尾。', previewMore: '尚有下一批；请显式读取。', previewEmpty: '本批未返回候选样本。',
  previewMismatch: '响应无法核对为有效预检批次，未推进游标，请重试或从头预检。',
  previewFailed: '预检失败。已有结果为上次成功的批次，可重试读取下一批。', previewForbidden: '当前会话无权执行配额预检，请核对权限。',
  previewUnsupported: '当前指标无法预检，请确认对应业务用量服务已启用。',
  title: '配额用量', refresh: '刷新用量', close: '关闭', seats: '成员席位', storage: '文件存储', seatUnit: '单位：个', storageUnit: '单位：字节',
  limit: '账面上限', used: '已确认占用', reserved: '尚未确认预留', remaining: '账面余额', readAt: '读取时间', active: '企业为活动状态', inactive: '企业为非活动状态',
  hint: '账面余额按上限减去已用和预留计算。新增操作仍需通过当前企业、订阅及业务授权校验。',
  missing: '当前未配置此项指标，不能视为零用量或无限额度。', exceeded: '已用与预留超过账面上限，请核对当前订阅及用量。',
  failed: '读取失败；已有结果为上次成功读取的数据，请重新读取。', forbidden: '当前会话无权读取该企业配额，请核对权限。',
  absent: '当前企业不存在或已不可读取，请刷新企业列表。', mismatch: '返回用量无法与原企业及正式指标核对，请重新读取。'
};
const en: Record<keyof typeof zh, string> = {
  previewTitle: 'Global quota preview', previewMetric: 'Preview metric', previewStart: 'Preview batch', previewNext: 'Preview next batch', previewReset: 'Restart preview',
  previewHint: 'Check the usage baseline of all active tenants, up to 25 per batch. This console only reports candidates; it never repairs or writes quota.',
  previewSkipped: 'Tenants with pending reservations or unsettled operations are skipped. Each batch is a separate read, not a globally consistent snapshot or a total tenant count.',
  previewBatch: 'Current batch', previewCandidates: 'Candidates in this batch', previewApplied: 'Repairs applied', previewSamples: 'Sample candidate tenant IDs for this batch',
  previewComplete: 'The current scan has reached the end.', previewMore: 'Another batch is available; read it explicitly.', previewEmpty: 'This batch returned no candidate samples.',
  previewMismatch: 'The response is not a valid preview batch. The cursor was not advanced; retry or restart.',
  previewFailed: 'Preview failed. Any existing result is the last successful batch; retry the next batch.', previewForbidden: 'This session cannot preview quota. Check your permissions.',
  previewUnsupported: 'This metric cannot be previewed. Check that its business usage service is enabled.',
  title: 'Quota usage', refresh: 'Refresh usage', close: 'Close', seats: 'Member seats', storage: 'File storage', seatUnit: 'Unit: seats', storageUnit: 'Unit: bytes',
  limit: 'Ledger limit', used: 'Confirmed usage', reserved: 'Pending reservations', remaining: 'Ledger balance', readAt: 'Read at', active: 'Tenant is active', inactive: 'Tenant is inactive',
  hint: 'Ledger balance is the limit minus used and reserved amounts. New operations still require current tenant, subscription and business authorization.',
  missing: 'This metric is not configured. It does not imply zero usage or an unlimited allowance.', exceeded: 'Used and reserved amounts exceed the ledger limit. Review the subscription and usage.',
  failed: 'The read failed. Any existing result is the last successful read; refresh to check.', forbidden: 'This session cannot read the tenant quota. Check your permissions.',
  absent: 'The tenant is missing or unavailable. Refresh the tenant list.', mismatch: 'The usage response does not match the original tenant and official metrics. Reload to check.'
};
export const tenantQuotaText = (locale: SupportedLocale, key: keyof typeof zh): string => (locale === 'en-US' ? en : zh)[key];
