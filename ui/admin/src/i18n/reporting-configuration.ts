const zh = {
  hostOnly: '报表配置仅在 Host 上下文开放，需要读取报表定义权限。',
  refresh: '刷新配置', unconfirmed: '操作结果或草稿版本未确认，请先刷新。编辑内容会保留，确认重新载入草稿后再保存。',
  reload: '重新载入草稿', discard: '放弃本地输入，重新读取服务端草稿？',
  publish: '发布报表版本', publishHelp: '将当前服务端草稿发布为不可变版本。保存草稿不会修改已发布内容。',
  note: '版本说明', cancel: '取消', catalogMissing: '新建定义需要可用分组及读取分组、静态查询和数据源目录的权限。'
} as const;
const en: Record<keyof typeof zh, string> = {
  hostOnly: 'Report configuration requires the Host context and permission to read definitions.',
  refresh: 'Refresh configuration', unconfirmed: 'The operation result or draft version is unconfirmed. Refresh first. Local edits are preserved; explicitly reload the draft before saving again.',
  reload: 'Reload draft', discard: 'Discard local input and reload the server draft?',
  publish: 'Publish report version', publishHelp: 'Publish the current server draft as an immutable version. Saving a draft does not modify published content.',
  note: 'Version note', cancel: 'Cancel', catalogMissing: 'Creating a definition requires an available group and permission to read groups, static queries, and data sources.'
};
export function reportingConfigurationText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
