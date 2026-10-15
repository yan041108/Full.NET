import type { SupportedLocale } from './locale.js';

/** 来源增量状态只说明处理意图与入队，不代表活动索引已可检索。 */
const labels = {
  'zh-CN': {
    pending_processing: '等待处理', awaiting_index_confirmation: '待确认索引',
    index_admitted: '索引任务已创建', unknown: '状态待刷新'
  },
  'en-US': {
    pending_processing: 'Pending processing', awaiting_index_confirmation: 'Awaiting index confirmation',
    index_admitted: 'Index task created', unknown: 'Refresh status'
  }
} as const;

/** 按稳定机器码显示当前语言文本；未知版本不显示服务端内部状态内容。 */
export function translateKnowledgeProcessing(locale: SupportedLocale, state: string): string {
  const localized = labels[locale];
  return Object.hasOwn(localized, state) ? localized[state as keyof typeof localized] : localized.unknown;
}

const recoveryLabels = {
  'zh-CN': {
    recover: '恢复增量意图', continueRecovery: '继续恢复下一批', recoveryHint: '分批补齐历史或停用期间的来源意图，不创建收费索引任务。',
    recoveryPageDone: '本批来源意图已复核，可继续下一批。', recoveryDone: '来源意图恢复完成；并发变更或失败后可从头重扫。'
  },
  'en-US': {
    recover: 'Recover index intents', continueRecovery: 'Recover next batch', recoveryHint: 'Restore historical or disabled-period source intents in batches without creating paid index tasks.',
    recoveryPageDone: 'This batch was reviewed. Continue with the next batch.', recoveryDone: 'Source intent recovery completed. Restart after concurrent changes or failures.'
  }
} as const;

/** 恢复动作提示与处理状态分离，新增按钮不会扩大服务端状态码白名单。 */
export function translateKnowledgeRecovery(locale: SupportedLocale, key: string): string {
  const localized = recoveryLabels[locale];
  return Object.hasOwn(localized, key) ? localized[key as keyof typeof localized] : localized.recoveryHint;
}
