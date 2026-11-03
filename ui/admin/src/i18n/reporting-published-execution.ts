const zh = {
  hostOnly: '报表执行需要 Host 上下文、执行报表及读取报表定义权限。', reload: '重新载入发布版本',
  version: '已载入发布版本', resultVersion: '结果发布版本', versionHelp: '执行固定为当前已载入版本；保存草稿或发布新版本不会改变这次输入。需要新版时重新载入。',
  changed: '当前输入已更改。翻页仍沿用上次执行的参数，点击执行才切换到新输入并回到第一页。',
  required: '请填写所有必填参数。', empty: '没有可执行的已发布报表。', page: '结果页'
} as const;
const en: Record<keyof typeof zh, string> = {
  hostOnly: 'Report execution requires the Host context, execution permission, and permission to read report definitions.', reload: 'Reload published version',
  version: 'Loaded publication version', resultVersion: 'Result publication version', versionHelp: 'Execution uses the loaded publication. Saving or publishing a draft does not change this input. Reload to use a newer publication.',
  changed: 'The input has changed. Pagination still uses the last executed parameters. Run again to use the new input from page one.',
  required: 'Complete all required parameters.', empty: 'There are no executable published reports.', page: 'Result page'
};
export function reportingExecutionText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
