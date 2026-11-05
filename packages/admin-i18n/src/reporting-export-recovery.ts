import type { SupportedLocale } from './locale.js';

// 结果恢复词条仅随导出页面加载，稳定机器码和任务身份不依赖译文。
const messages = {
  'zh-CN': { action: '按原参数再次导出', title: '确认独立导出任务', source: '原导出任务 ID', version: '原发布版本', nullValue: '未提供（null）', emptyValue: '空字符串',
    lookupId: '导出任务 ID', readById: '按 ID 读取', invalidId: '请输入非零导出任务 UUID。', lookupHint: '读取当前企业内获准可见的原导出任务，支持当前页之外的 ID；列表分页保持原条件。', requester: '发起人 ID',
    terminalNotice: '原任务已失败或取消，历史记录保留。本次按原发布版本和原参数创建独立任务，仍须通过当前授权及参数校验。请先修正原失败原因；若需修改参数，使用普通新建并核对当前目录。确认前不会创建，响应不明后请先核对任务列表。',
    notice: '原结果文件已明确不可用。本次按原发布版本和原参数创建独立任务，不恢复旧文件、不自动切换最新版本；当前权限及版本可用性仍由服务端校验。确认前不会创建，响应不明后请先核对任务列表。',
    changed: '原导出任务、文件或参数已变化，不能据旧结果重新生成；请刷新原任务核对。' },
  'en-US': { action: 'Export again with original parameters', title: 'Confirm a separate export task', source: 'Original export task ID', version: 'Original published version', nullValue: 'Not provided (null)', emptyValue: 'Empty string',
    lookupId: 'Export task ID', readById: 'Read by ID', invalidId: 'Enter a nonzero export task UUID.', lookupHint: 'Read an export task visible to you in the current tenant, including IDs outside this page. List paging retains its current conditions.', requester: 'Requested by user ID',
    terminalNotice: 'The original task failed or was cancelled. Its history is retained. This creates a separate task with its original published version and parameters, subject to current authorization and parameter validation. Fix the original failure first. To change parameters, use a new export and review the current catalog. Nothing is created before confirmation. Check the task list after an uncertain response.',
    notice: 'The original result file is explicitly unavailable. This creates a separate task using the original published version and parameters. It neither restores the old file nor switches to the latest version. The server rechecks current permissions and version availability. Nothing is created before confirmation. Check the task list after an uncertain response.',
    changed: 'The original task, file or parameters changed. Refresh the original task before regenerating.' }
} as const;

/** 按当前规范语言显示导出结果恢复文案。
 * @param locale 规范 BCP 47 语言。
 * @param key 稳定词条键。
 * @returns 当前语言的纯文本。
 */
export function translateReportingExportRecovery(locale: SupportedLocale, key: keyof typeof messages['zh-CN']): string { return messages[locale][key]; }
