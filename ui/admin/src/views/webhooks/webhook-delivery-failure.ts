import type { AdminI18n } from '../../i18n/adminI18n';

/** 只展示服务端登记的安全分类，区分确认未发与可能已到达的超时。 */
export function webhookDeliveryFailure(t: AdminI18n['t'], code: string | null | undefined, status: number | null | undefined): string {
  switch (code) {
    case 'webhooks.delivery.http_error': return t('webhooks.history.httpError', { status: status ?? '' });
    case 'webhooks.delivery.timeout': return t('webhooks.history.timeout');
    case 'webhooks.delivery.tenant_inactive': return t('webhooks.history.tenantInactive');
    case 'webhooks.delivery.tenant_state_unavailable': return t('webhooks.history.tenantStateUnavailable');
    case 'webhooks.delivery.subscription_inactive': return t('webhooks.history.subscriptionInactive');
    case null:
    case undefined: return '—';
    default: return t('webhooks.history.otherError');
  }
}
