import type { MessageKey } from '@fullnet/admin-i18n';

/** 只翻译已知业务状态；未知协议值保留原文，不猜测其可执行操作。 */
export function requestStatusLabel(status: string, t: (key: MessageKey) => string): string {
  switch (status) {
    case 'Draft': return t('enterpriseRequests.status.Draft');
    case 'Submitted': return t('enterpriseRequests.status.Submitted');
    case 'Approved': return t('enterpriseRequests.status.Approved');
    case 'Rejected': return t('enterpriseRequests.status.Rejected');
    case 'Cancelled': return t('enterpriseRequests.status.Cancelled');
    default: return status;
  }
}

/** 空值和异常时间不进入 Intl，避免单个字段阻断整个详情展示。 */
export function requestTime(value: string | null, locale: string): string {
  if (!value || !Number.isFinite(Date.parse(value))) return '—';
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(value));
}
