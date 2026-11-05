import type { SupportedLocale } from './locale.js';

// 仅对账页面消费，避免功能专用长文案进入登录和概览的静态依赖图。
const messages = {
  "zh-CN": {
    "title": "确认原订单对账",
    "confirm": "确认查询渠道并同步",
    "hint": "先核对原订单、资金归属与本地快照，再确认查询渠道。对账可能推进本地支付及履约状态，不会创建新支付或退款；最终以服务端当前校验为准。",
    "mismatch": "对账订单回执与原资金身份或版本不一致，请保留原订单核对。",
    "unconfirmed": "对账结果未确认，原请求可能仍在处理。关闭或取消不代表回滚；先显式重读原订单，再决定是否重新确认。旧快照不能证明本次结果。",
    "accepted": "本次对账回执已核对。页面显示返回的实际状态，支付与履约请分别核实；再次操作前请重读。",
    "read": "重读原订单",
    "readAt": "本地观察完成时间（非渠道资金时间）",
    "ineligible": "当前原订单状态或退款渠道不支持此对账操作，仅可重读核对。"
  },
  "en-US": {
    "title": "Confirm original order reconciliation",
    "confirm": "Confirm provider query and sync",
    "hint": "Check the original order, payment ownership and local snapshot before confirming the provider query. Reconciliation may advance local payment and fulfillment states. It creates no payment or refund; the server validates current conditions.",
    "mismatch": "The reconciliation receipt does not match the original payment identity or version. Retain the original order for verification.",
    "unconfirmed": "Reconciliation is unconfirmed and the original request may still be running. Closing or cancelling does not roll it back. Explicitly reread the original order before another confirmation. The old snapshot cannot establish this outcome.",
    "accepted": "The reconciliation receipt is verified. The returned state is shown; verify payment and fulfillment separately. Reread before another operation.",
    "read": "Reread original order",
    "readAt": "Local observation completed at (not provider funds time)",
    "ineligible": "The original order state or refund channel does not support this operation. Reread to verify the record."
  }
} as const;

/** 按规范语言和稳定词条键显示人工对账文案，不以译文驱动业务。
 * @param locale 规范 BCP 47 语言。
 * @param key 稳定对账词条键。
 * @returns 当前语言的纯文本。
 */
export function translatePaymentReconciliation(locale: SupportedLocale, key: keyof typeof messages['zh-CN']): string {
  return messages[locale][key];
}
