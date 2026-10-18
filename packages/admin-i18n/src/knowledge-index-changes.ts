import type { SupportedLocale } from './locale.js';

const labels = {
  'zh-CN': {
    title: '已确认来源', hint: '这里只展示最新已确认的来源，不表示索引已就绪。查看索引状态后，可按现有权限显式执行操作。',
    empty: '暂无已确认来源，可先上传文件或恢复增量意图。', name: '文档', version: '来源版本', updated: '确认更新时间',
    view: '查看索引', previous: '上一页', next: '下一页', page: '第 {page} 页', refresh: '从头刷新'
  },
  'en-US': {
    title: 'Confirmed sources', hint: 'Latest confirmed sources are shown here. Index readiness is checked separately; subsequent actions require explicit confirmation and permission.',
    empty: 'No confirmed sources. Upload a file or recover index intents first.', name: 'Document', version: 'Source version', updated: 'Confirmation updated',
    view: 'View index', previous: 'Previous', next: 'Next', page: 'Page {page}', refresh: 'Refresh from start'
  }
} as const;

/** 来源目录与任务状态分别表述，避免把可靠确认误读为已完成索引。 */
export function translateKnowledgeIndexChanges(locale: SupportedLocale, key: keyof typeof labels['zh-CN']): string {
  return labels[locale][key];
}
