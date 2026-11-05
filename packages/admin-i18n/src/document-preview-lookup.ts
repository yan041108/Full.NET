import type { SupportedLocale } from './locale.js';

// 精确定位文案只供预览页面使用，任务和文件身份始终使用原始 UUID。
const messages = {
  'zh-CN': { taskId: '预览任务 ID', read: '按 ID 读取', invalid: '请输入非零预览任务 UUID。', hint: '读取 Host 范围内获准可见的原预览任务，支持当前页之外的 ID；不会更改文档筛选或自动打开 PDF。',
    versionId: '原文档版本 ID', currentFile: '未指定历史版本（创建时的当前文件）', sourceFile: '原文件 ID', outputFile: '结果文件 ID', requester: '发起人 ID', revision: '任务版本', absent: '未提供', stale: '本次重读失败，以下为上次读取的快照；操作前请重新读取。' },
  'en-US': { taskId: 'Preview task ID', read: 'Read by ID', invalid: 'Enter a nonzero preview task UUID.', hint: 'Read a task visible in the Host scope, including IDs outside this page. Document filters stay unchanged and PDFs do not open automatically.',
    versionId: 'Original document version ID', currentFile: 'No historical version specified (current file at creation)', sourceFile: 'Source file ID', outputFile: 'Result file ID', requester: 'Requester ID', revision: 'Task revision', absent: 'Not provided', stale: 'The latest read failed. This is the previous snapshot; read it again before acting.' }
} as const;

/** 返回当前语言的预览定位纯文本。
 * @param locale 规范 BCP 47 语言。
 * @param key 稳定词条键。
 * @returns 当前语言的纯文本。
 */
export function translateDocumentPreviewLookup(locale: SupportedLocale, key: keyof typeof messages['zh-CN']): string { return messages[locale][key]; }
