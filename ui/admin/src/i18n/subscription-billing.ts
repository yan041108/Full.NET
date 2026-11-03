import type { SupportedLocale } from '@fullnet/admin-i18n';

// 资金和权益状态使用独立固定文案，不能把渠道成功误显示为已完成授权。
const zh = {
  offer: '付费报价', seller: '收款租户 ID', merchant: '商户配置 ID', amount: '金额（最小货币单位）',
  currency: '币种', seats: '席位上限', storage: '存储字节上限', days: '有效天数', features: '功能权益', active: '允许新购买',
  save: '保存', close: '关闭', failure: '操作失败，请刷新后重试。', saved: '报价已保存', purchase: '创建付费订阅',
  package: '套餐', quote: '读取报价', create: '确认报价并创建账单', checkout: '按账单下单',
  start: '生效时间（UTC）', end: '结束时间（UTC）', invoice: '账单 ID', order: '支付订单 ID',
  funds: '资金状态', fulfillment: '订阅履约', refresh: '刷新履约状态', retry: '重试履约',
  pending: '等待处理', Creating: '正在创建订单', Awaiting: '等待支付', ReadyPaid: '已收款，等待发放',
  Applied: '权益已发放', ReadyRefund: '已退款，等待撤销', Refunded: '退款已完成履约', Review: '需要人工对账',
  noBinding: '此订单未关联付费订阅', paymentLink: '支付渠道链接', validation: '请填写有效标识、金额和期限。',
  paidHint: '只有已核实的收款事实才能发放权益；本页不能手工标记付款成功。',
  safeRetry: '网络重试复用原账单；履约重试不会再次扣款。'
};
const en: Record<keyof typeof zh, string> = {
  offer: 'Paid offer', seller: 'Payment tenant ID', merchant: 'Merchant configuration ID', amount: 'Amount in minor units',
  currency: 'Currency', seats: 'Seat limit', storage: 'Storage byte limit', days: 'Duration in days', features: 'Feature entitlements', active: 'Allow new purchases',
  save: 'Save', close: 'Close', failure: 'Operation failed. Refresh and retry.', saved: 'Offer saved', purchase: 'Create paid subscription',
  package: 'Package', quote: 'Load offer', create: 'Confirm offer and create invoice', checkout: 'Checkout invoice',
  start: 'Effective from (UTC)', end: 'Effective until (UTC)', invoice: 'Invoice ID', order: 'Payment order ID',
  funds: 'Funds status', fulfillment: 'Subscription fulfillment', refresh: 'Refresh fulfillment', retry: 'Retry fulfillment',
  pending: 'Pending', Creating: 'Creating order', Awaiting: 'Awaiting payment', ReadyPaid: 'Paid, awaiting fulfillment',
  Applied: 'Entitlements applied', ReadyRefund: 'Refunded, awaiting revocation', Refunded: 'Refund fulfillment complete', Review: 'Manual reconciliation required',
  noBinding: 'This order is not associated with a paid subscription', paymentLink: 'Payment provider link', validation: 'Enter valid IDs, amount and duration.',
  paidHint: 'Only verified funds can grant entitlements. This page cannot manually mark a payment as paid.',
  safeRetry: 'Network retries reuse the original invoice. Fulfillment retries never charge again.'
};
export type SubscriptionBillingTextKey = keyof typeof zh;
export const subscriptionBillingText = (locale: SupportedLocale, key: SubscriptionBillingTextKey): string => (locale === 'en-US' ? en : zh)[key];
export function subscriptionPhaseText(locale: SupportedLocale, phase: string): string {
  return ['Creating', 'Awaiting', 'ReadyPaid', 'Applied', 'ReadyRefund', 'Refunded', 'Review'].includes(phase)
    ? subscriptionBillingText(locale, phase as SubscriptionBillingTextKey) : subscriptionBillingText(locale, 'pending');
}
