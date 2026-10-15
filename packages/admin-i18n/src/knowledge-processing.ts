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
