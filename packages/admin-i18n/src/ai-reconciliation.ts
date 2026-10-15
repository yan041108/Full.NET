import type { SupportedLocale } from './locale.js';

/** 人工对账词条与后台共用规范语言，不以译文驱动业务决策。 */
const messages = {
  'zh-CN': {
    title: '未知执行人工对账', notice: '核对外部结果后关闭运行，不会重新执行。未取得完整计量时继续占用原预算预留。',
    load: '读取账本', evidence: '核证材料 SHA-256 摘要', known: '已核证完整用量', input: '输入 Token', output: '输出 Token',
    cached: '缓存输入 Token（可选）', reviewed: '我已核对外部执行结果及证据，确认关闭此运行',
    submit: '关闭并记录对账', success: '对账已记录，运行已关闭', failed: '对账处理失败', reserved: '原预留 Token',
    usage: '计量状态', month: '原预留月份', unavailable: '当前运行已变化，请重新读取账本',
    usageKnown: '已知', usageReserved: '已预留', usageUnknown: '未知',
    recordedInput: '原输入 Token', recordedOutput: '原输出 Token', recordedCached: '原缓存输入 Token', notProvided: '未提供'
  },
  'en-US': {
    title: 'Reconcile unknown execution', notice: 'Close the run after checking its external result. It will not execute again. Incomplete usage keeps the original reservation.',
    load: 'Read ledger', evidence: 'SHA-256 digest of verified evidence', known: 'Complete usage verified', input: 'Input tokens', output: 'Output tokens',
    cached: 'Cached input tokens (optional)', reviewed: 'I checked the external result and evidence and confirm closing this run',
    submit: 'Close and record reconciliation', success: 'Reconciliation recorded and run closed', failed: 'Reconciliation failed', reserved: 'Originally reserved tokens',
    usage: 'Usage status', month: 'Original reservation month', unavailable: 'The run has changed. Read the ledger again.',
    usageKnown: 'Known', usageReserved: 'Reserved', usageUnknown: 'Unknown',
    recordedInput: 'Recorded input tokens', recordedOutput: 'Recorded output tokens', recordedCached: 'Recorded cached input tokens', notProvided: 'Not provided'
  }
} as const;

/** 按后台当前语言取得对账文案。
 * @param locale 规范 BCP 47 语言。
 * @param key 稳定词条键。
 * @returns 可展示的纯文本。
 */
export function translateAiReconciliation(locale: SupportedLocale, key: keyof typeof messages['zh-CN']): string {
  return messages[locale][key];
}
