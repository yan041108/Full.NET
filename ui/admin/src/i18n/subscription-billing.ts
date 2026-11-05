import type { SupportedLocale } from '@fullnet/admin-i18n';

// 资金和权益状态使用独立固定文案，不能把渠道成功误显示为已完成授权。
const zh = {
  repurchase: '重新购买原套餐', sourceSubscription: '历史订阅', repurchasePolicy: '本次创建独立的新账单与新周期，读取当前报价；原订阅历史继续保留，付款后按新账单履约。',
  repurchaseUnavailable: '原套餐已停用或不在当前目录，请核对后选择其他可用套餐。',
  trialPackage: '试用套餐', trialConfirm: '确认开通试用', cancelConfirm: '确认取消订阅', confirmOperation: '确认本次操作',
  trialDuration: '本次试用为 30 天，提交前请核对套餐与起止时间。', activeSubscriptionHint: '已有试用、有效或待付款订阅，不能重复开通试用。',
  cancelPolicy: '取消当前订阅不会自动发起渠道退款；请核对原订阅与当前周期，退款沿独立订单流程处理。',
  historyRefresh: '刷新订阅历史', historyFailed: '订阅历史读取失败，请重新读取后再操作。',
  operationUnknown: '操作结果未确认，请刷新订阅历史核对；不会自动重试。', operationRejected: '操作被拒绝，请刷新订阅历史后重新确认。', operationConfirmed: '操作已确认完成。',
  statusTrial: '试用中', statusActive: '有效', statusPastDue: '待付款', statusCancelled: '已取消', statusExpired: '已到期',
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
  reconfirmQuote: '重新读取并确认报价', offerChanged: '报价已变更，本次未创建账单。请重新读取并确认报价。', invoiceRejected: '请求被拒绝。请核对当前订阅和报价，再决定是否恢复原请求。',
  recoverInvoice: '恢复原账单', recoverCheckout: '恢复原账单下单',
  invoiceRecovery: '已提交原请求。关闭后重新打开仍保留原报价、时间和请求身份；恢复只使用同一请求，不创建新身份。',
  invoiceUnknown: '账单结果未确认。请恢复原账单，或在订阅列表核对，勿另建购买。',
  checkoutUnknown: '下单结果未确认。请恢复原账单下单；不会自动重试或更换账单。',
  fulfillmentUnknown: '履约结果未确认。请先刷新当前订单，再决定是否重试。',
  responseMismatch: '响应与原目标不一致，未接入结果；请核对原订阅或订单。',
  safeRetry: '网络重试复用原账单；履约重试不会再次扣款。'
};
const en: Record<keyof typeof zh, string> = {
  repurchase: 'Buy this plan again', sourceSubscription: 'Previous subscription', repurchasePolicy: 'Create a new invoice and period using the current offer. The previous subscription remains in history; fulfillment follows the new invoice after payment.',
  repurchaseUnavailable: 'The previous plan is inactive or unavailable. Review the available plans before purchasing.',
  trialPackage: 'Trial package', trialConfirm: 'Confirm trial', cancelConfirm: 'Confirm subscription cancellation', confirmOperation: 'Confirm operation',
  trialDuration: 'The trial lasts 30 days. Review the package and period before confirming.', activeSubscriptionHint: 'A trial, active or unpaid subscription already exists. Another trial cannot be started.',
  cancelPolicy: 'Cancellation does not automatically issue a payment refund. Review the original subscription and period; refunds use the separate order process.',
  historyRefresh: 'Refresh subscriptions', historyFailed: 'Unable to read subscriptions. Reload before taking action.',
  operationUnknown: 'Operation result unconfirmed. Refresh subscriptions to check; no automatic retry occurs.', operationRejected: 'Operation rejected. Refresh subscriptions before confirming again.', operationConfirmed: 'Operation confirmed.',
  statusTrial: 'Trial', statusActive: 'Active', statusPastDue: 'Awaiting payment', statusCancelled: 'Cancelled', statusExpired: 'Expired',
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
  reconfirmQuote: 'Reload and confirm offer', offerChanged: 'The offer changed. No invoice was created; reload and confirm the offer.', invoiceRejected: 'The request was rejected. Check the subscription and offer before recovering the original request.',
  recoverInvoice: 'Recover original invoice', recoverCheckout: 'Recover invoice checkout',
  invoiceRecovery: 'The original request is retained when closing and reopening. Recovery reuses the same offer, time and request identity.',
  invoiceUnknown: 'Invoice result unconfirmed. Recover the original invoice or check subscriptions before purchasing again.',
  checkoutUnknown: 'Checkout result unconfirmed. Recover checkout for the same invoice; no automatic retry occurs.',
  fulfillmentUnknown: 'Fulfillment result unconfirmed. Refresh this order before deciding to retry.',
  responseMismatch: 'The response does not match the original target. Check the original subscription or order.',
  safeRetry: 'Network retries reuse the original invoice. Fulfillment retries never charge again.'
};
export type SubscriptionBillingTextKey = keyof typeof zh;
export const subscriptionBillingText = (locale: SupportedLocale, key: SubscriptionBillingTextKey): string => (locale === 'en-US' ? en : zh)[key];
export function subscriptionPhaseText(locale: SupportedLocale, phase: string): string {
  return ['Creating', 'Awaiting', 'ReadyPaid', 'Applied', 'ReadyRefund', 'Refunded', 'Review'].includes(phase)
    ? subscriptionBillingText(locale, phase as SubscriptionBillingTextKey) : subscriptionBillingText(locale, 'pending');
}
