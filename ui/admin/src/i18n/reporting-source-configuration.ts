const zh = {
  hostOnly: '数据源配置仅在 Host 上下文开放，需要读取报表数据源权限。', refresh: '刷新数据源',
  unconfirmed: '操作结果或版本尚未确认，请先刷新。编辑时请确认放弃本地输入并重新载入，不能直接重试保存或连接测试。',
  reload: '重新载入数据源', discard: '放弃本地输入（包括新输入的密码），重新读取数据源？',
  invalid: '请完整填写名称、服务器、数据库和用户名；端口必须是 1–65535 的整数，新建时必须提供密码。',
  passwordHelp: '编辑时密码留空会保留原凭据。关闭或重新载入会清除本次输入的密码。'
} as const;
const en: Record<keyof typeof zh, string> = {
  hostOnly: 'Data source configuration requires the Host context and permission to read report data sources.', refresh: 'Refresh data sources',
  unconfirmed: 'The operation result or version is unconfirmed. Refresh first. Explicitly discard and reload local edits before saving again. Do not replay a connection test.',
  reload: 'Reload data source', discard: 'Discard local edits, including the newly entered password, and reload this data source?',
  invalid: 'Enter a name, server, database, and username. The port must be an integer from 1 to 65535. A password is required for a new source.',
  passwordHelp: 'Leave the password empty to keep the existing credentials. Closing or reloading clears the password entered here.'
};
export function reportingSourceText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
