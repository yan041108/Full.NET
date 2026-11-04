const zh = {
  refreshCatalog: '刷新可选目录（保留草稿）',
  resetParameters: '按当前查询端口重置参数', resetParametersConfirm: '放弃当前参数的名称、默认值和必填设置，使用当前查询端口的参数定义？其他草稿输入会保留。',
  draftIncompatible: '当前分组、数据源或参数与查询端口不匹配，请核对后再保存。',
  historyRetry: '重新读取发布历史',
  draftProcessing: '正在保存草稿…', draftConfirmed: '草稿保存已确认', draftUnknown: '草稿保存结果尚未确认，请核对目录后再操作。', draftRejected: '草稿保存被拒绝，可修正后手动重试。',
  closeSaving: '关闭只停止本窗口等待，服务端保存仍可能完成；请核对目录。',
  creationBlocked: '上一次新建结果尚未确认，已保留原始输入。刷新或重开不会自动再次新建。',
  recovery: '我已核对新建结果', recoveryConfirm: '请先核对目录，确认上次新建未完成且仍需新建。确认仅解锁下一次手动保存，不会自动提交。',
  deleteNotApplied: '服务端未确认删除，请刷新核对后再操作。',
  publishProcessing: '正在发布报表版本…', publishUnknown: '发布结果尚未确认，请刷新定义和发布历史核对；不会自动重发。', publishRejected: '发布被拒绝，可修正后手动重试。', draftVersion: '草稿并发版本', close: '关闭', closePublishing: '关闭只停止本窗口等待，服务端发布仍可能完成；请刷新核对。',
  hostOnly: '报表配置仅在 Host 上下文开放，需要读取报表定义权限。',
  refresh: '刷新配置', unconfirmed: '操作结果或草稿版本未确认，请先刷新。编辑内容会保留，确认重新载入草稿后再保存。',
  reload: '重新载入草稿', discard: '放弃本地输入，重新读取服务端草稿？',
  publish: '发布报表版本', publishHelp: '将当前服务端草稿发布为不可变版本。保存草稿不会修改已发布内容。',
  note: '版本说明', cancel: '取消', catalogMissing: '新建定义需要可用分组及读取分组、静态查询和数据源目录的权限。'
} as const;
const en: Record<keyof typeof zh, string> = {
  refreshCatalog: 'Reload choices and keep draft input',
  resetParameters: 'Reset parameters from current query port', resetParametersConfirm: 'Discard current parameter labels, defaults, and required settings and use the current query port definition? Other draft input will be preserved.',
  draftIncompatible: 'The group, data source, or parameters do not match the query port. Review them before saving.',
  historyRetry: 'Reload publication history',
  draftProcessing: 'Saving draft…', draftConfirmed: 'Draft save confirmed', draftUnknown: 'Draft save is unconfirmed. Review the directory before another operation.', draftRejected: 'Draft save was rejected. Correct it and retry manually.',
  closeSaving: 'Closing stops this window from waiting. The server may still save the draft; review the directory.',
  creationBlocked: 'The previous creation is unconfirmed. Original input is preserved. Reloading or reopening will not create it again.',
  recovery: 'I reviewed the creation result', recoveryConfirm: 'Review the directory first and confirm the previous creation did not complete and is still needed. Confirming only unlocks the next manual save; it does not submit.',
  deleteNotApplied: 'The server did not confirm deletion. Reload to review before another operation.',
  publishProcessing: 'Publishing report version…', publishUnknown: 'Publication is unconfirmed. Reload definitions and history to review; it will not retry automatically.', publishRejected: 'Publication was rejected. Correct it and retry manually.', draftVersion: 'Draft concurrency version', close: 'Close', closePublishing: 'Closing stops this window from waiting. Publication may still complete; reload to review.',
  hostOnly: 'Report configuration requires the Host context and permission to read definitions.',
  refresh: 'Refresh configuration', unconfirmed: 'The operation result or draft version is unconfirmed. Refresh first. Local edits are preserved; explicitly reload the draft before saving again.',
  reload: 'Reload draft', discard: 'Discard local input and reload the server draft?',
  publish: 'Publish report version', publishHelp: 'Publish the current server draft as an immutable version. Saving a draft does not modify published content.',
  note: 'Version note', cancel: 'Cancel', catalogMissing: 'Creating a definition requires an available group and permission to read groups, static queries, and data sources.'
};
export function reportingConfigurationText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
