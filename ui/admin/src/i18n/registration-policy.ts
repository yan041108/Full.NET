const zh = {
  mode: '注册模式', disabled: '禁用注册', invited: '仅邀请注册', open: '开放注册', save: '保存注册策略',
  refresh: '重新读取注册策略', loadFailed: '注册策略读取失败，请重试。', saveFailed: '注册策略保存未确认。',
  unconfirmed: '结果或版本未确认，请重新读取策略后再修改；不会自动重放保存请求。',
  link: '注册链接', copy: '复制注册链接', copied: '注册链接已复制。',
  copyFailed: '无法自动复制，请选中链接后手动复制。', close: '关闭',
  linkHelp: '仅在开放注册且此注册方式启用时可用；服务端会重新校验目标租户和注册规则。'
} as const;
const en: Record<keyof typeof zh, string> = {
  mode: 'Registration mode', disabled: 'Registration disabled', invited: 'Invitation only', open: 'Open registration', save: 'Save registration policy',
  refresh: 'Reload registration policy', loadFailed: 'Could not load the registration policy. Try again.', saveFailed: 'The policy save is unconfirmed.',
  unconfirmed: 'The result or version is unconfirmed. Reload before making changes. Saving is never replayed automatically.',
  link: 'Registration link', copy: 'Copy registration link', copied: 'Registration link copied.',
  copyFailed: 'Automatic copying is unavailable. Select the link and copy it manually.', close: 'Close',
  linkHelp: 'Available only with open registration and an enabled registration way. The server rechecks the target tenant and registration rules.'
};
export function registrationPolicyText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
