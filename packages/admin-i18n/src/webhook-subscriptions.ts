export const webhookSubscriptionsZh = {
  'webhooks.status': '状态',
  'navigation.webhooks.title': 'Webhook 订阅', 'navigation.webhooks.caption': '管理当前企业的事件回调',
  'webhooks.title': 'Webhook 订阅', 'webhooks.tenantRequired': '请先进入企业，再管理该企业的订阅。',
  'webhooks.create': '创建订阅', 'webhooks.event': '事件类型', 'webhooks.target': 'HTTPS 接收地址',
  'webhooks.secret': '签名密钥', 'webhooks.secretHint': '使用 16～128 字符的独立随机密钥。保存后不再显示，请在接收端安全配置。',
  'webhooks.active': '已启用', 'webhooks.paused': '已暂停', 'webhooks.pause': '暂停订阅', 'webhooks.resume': '恢复订阅',
  'webhooks.statusHint': '暂停阻止后续事件扇出和待投递领取，保留历史记录；已领取的投递及已发出的 HTTP 请求可能仍会结束。恢复后按原队列继续，不重新生成历史事件。',
  'webhooks.empty': '当前企业暂无 Webhook 订阅。', 'webhooks.failed': '无法完成 Webhook 操作，请核对后重试。',
  'webhooks.saved': '已保存订阅。', 'webhooks.statusSaved': '已确认订阅状态。',
  'webhooks.unconfirmed': '写入结果未知。不会自动重发，请先手动刷新并核对原订阅。',
  'webhooks.successReceipt': '操作已确认', 'webhooks.unknownReceipt': '操作结果未知',
  'webhooks.createBlocked': '请核对列表是否已创建相同事件及地址的订阅，再决定是否允许重新创建。',
  'webhooks.reviewCreate': '核对后允许重新创建', 'webhooks.reviewConfirm': '确认已核对当前企业的订阅列表？本次只允许再次创建，不会自动提交。',
  'webhooks.inputInvalid': '请填写事件类型、有效的 HTTPS 地址及符合长度要求的签名密钥。'
} as const;
export const webhookSubscriptionsEn: Record<keyof typeof webhookSubscriptionsZh, string> = {
  'webhooks.status': 'Status',
  'navigation.webhooks.title': 'Webhook subscriptions', 'navigation.webhooks.caption': 'Manage event callbacks for the current tenant',
  'webhooks.title': 'Webhook subscriptions', 'webhooks.tenantRequired': 'Enter a tenant before managing its subscriptions.',
  'webhooks.create': 'Create subscription', 'webhooks.event': 'Event type', 'webhooks.target': 'HTTPS receiver URL',
  'webhooks.secret': 'Signing secret', 'webhooks.secretHint': 'Use a separate random secret of 16–128 characters. It is never shown after saving; configure it securely at the receiver.',
  'webhooks.active': 'Active', 'webhooks.paused': 'Paused', 'webhooks.pause': 'Pause subscription', 'webhooks.resume': 'Resume subscription',
  'webhooks.statusHint': 'Pausing stops future fanout and pending delivery claims while preserving history. Already claimed deliveries and HTTP requests already sent may still finish. Resuming continues the existing queue without recreating past events.',
  'webhooks.empty': 'This tenant has no Webhook subscriptions.', 'webhooks.failed': 'The Webhook operation could not be completed. Check before retrying.',
  'webhooks.saved': 'Subscription saved.', 'webhooks.statusSaved': 'Subscription state confirmed.',
  'webhooks.unconfirmed': 'The write outcome is unknown. It will not be replayed. Refresh manually and check the original subscription.',
  'webhooks.successReceipt': 'Operation confirmed', 'webhooks.unknownReceipt': 'Operation outcome unknown',
  'webhooks.createBlocked': 'Check the list for a subscription with the same event and URL before allowing another creation.',
  'webhooks.reviewCreate': 'Allow another creation after review', 'webhooks.reviewConfirm': 'Have you checked the current tenant subscription list? This only allows another creation and does not submit it.',
  'webhooks.inputInvalid': 'Enter an event type, a valid HTTPS URL, and a signing secret within the required length.'
};
