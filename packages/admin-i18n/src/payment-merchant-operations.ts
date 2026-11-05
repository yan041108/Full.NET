import type { SupportedLocale } from './locale.js';

// 商户运营专用词条随功能页面加载，保留首屏和概览既有静态预算。
const messages = {
  'zh-CN': {
    all: '全部', nameSearch: '商户名称包含', search: '查询', reset: '重置', id: '商户配置 ID', confirm: '确认', close: '关闭',
    detail: '详情', detailTitle: '商户配置只读详情', readById: '按 ID 读取', invalidId: '请输入非零商户配置 UUID。',
    detailHint: '只读当前配置，不代表历史订单的商户快照。密钥仅显示是否已配置，不读取或回显正文；地址仅作文本展示，不自动访问。',
    apiKeyConfigured: 'API v3 密钥已配置', privateKeyConfigured: '商户私钥已配置', version: '记录版本', createdAt: '创建时间', updatedAt: '更新时间',
    observedAt: '本地读取完成时间（非渠道认证时间）',
    hint: '提交筛选后生效；分页和刷新使用已提交条件。空租户表示全部（含 Host）。列表只展示脱敏配置。',
    invalid: '请输入非零租户 UUID、支持的渠道/启用状态和不超过 128 字符的名称筛选。',
    mismatch: '商户配置响应与请求的身份、筛选或版本不一致，请重新读取。',
    changed: '原商户配置已变化或已离开当前结果，请重新读取后再确认停用。'
  },
  'en-US': {
    all: 'All', nameSearch: 'Merchant name contains', search: 'Search', reset: 'Reset', id: 'Merchant configuration ID', confirm: 'Confirm', close: 'Close',
    detail: 'Details', detailTitle: 'Read-only merchant configuration', readById: 'Read by ID', invalidId: 'Enter a nonzero merchant configuration UUID.',
    detailHint: 'Reads current configuration, not the merchant snapshot of a historical order. Only key presence is shown; key contents are neither read nor echoed. Addresses are plain text and are not opened automatically.',
    apiKeyConfigured: 'API v3 key configured', privateKeyConfigured: 'Merchant private key configured', version: 'Record version', createdAt: 'Created at', updatedAt: 'Updated at',
    observedAt: 'Local read completed at (not provider certification time)',
    hint: 'Submit filters to apply them. Paging and refresh retain the submitted conditions. An empty tenant includes all tenants and Host. The list shows masked configuration only.',
    invalid: 'Enter a nonzero tenant UUID, supported channel/enabled state and a name filter of at most 128 characters.',
    mismatch: 'The merchant response does not match the requested identity, filters or version. Read it again.',
    changed: 'The original merchant configuration changed or left the current results. Read it again before confirming disable.'
  }
} as const;

/** 按规范语言和稳定词条键显示商户运营文案。
 * @param locale 规范 BCP 47 语言。
 * @param key 稳定词条键。
 * @returns 当前语言的纯文本。
 */
export function translatePaymentMerchantOperations(locale: SupportedLocale, key: keyof typeof messages['zh-CN']): string {
  return messages[locale][key];
}
