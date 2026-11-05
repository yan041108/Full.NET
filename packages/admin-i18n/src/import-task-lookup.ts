import type { SupportedLocale } from './locale.js';

// 任务定位独立于分页；来源和操作归属始终使用稳定机器键及原始 UUID。
const messages = {
  'zh-CN': { allStatuses: '全部状态', allSchemas: '全部 Schema', submitted: '已提交条件：', apply: '应用筛选', invalidSchema: 'Schema 键不得超过 128 个字符。', invalidStatus: '请选择已支持的导入任务状态。', requestedPage: '待读取页码：', taskId: '导入任务 ID', read: '按 ID 读取', invalid: '请输入非零导入任务 UUID。', hint: '读取当前企业内获准可见的原导入任务，支持当前页之外的 ID；不会自动执行、重试或下载。',
    tenant: '企业 ID', requester: '发起人 ID', sourceFile: '原文件 ID', schemaKey: 'Schema 键', worksheetKey: '工作表键', revision: '任务版本', stale: '本次重读失败，以下为上次读取的快照，请重新读取后核对。' },
  'en-US': { allStatuses: 'All statuses', allSchemas: 'All schemas', submitted: 'Submitted filters:', apply: 'Apply filters', invalidSchema: 'The schema key must not exceed 128 characters.', invalidStatus: 'Select a supported import task state.', requestedPage: 'Requested page:', taskId: 'Import task ID', read: 'Read by ID', invalid: 'Enter a nonzero import task UUID.', hint: 'Read a task visible in the current tenant, including IDs outside this page. Execution, retries and downloads require separate actions.',
    tenant: 'Tenant ID', requester: 'Requester ID', sourceFile: 'Original file ID', schemaKey: 'Schema key', worksheetKey: 'Worksheet key', revision: 'Task revision', stale: 'The latest read failed. This is the previous snapshot; read it again before checking the result.' }
} as const;

/** 返回导入定位的当前语言纯文本。
 * @param locale 规范 BCP 47 语言。
 * @param key 稳定词条键。
 * @returns 当前语言的纯文本。
 */
export function translateImportTaskLookup(locale: SupportedLocale, key: keyof typeof messages['zh-CN']): string { return messages[locale][key]; }
