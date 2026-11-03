const zh = {
  mode: '注册模式', disabled: '禁用注册', invited: '仅邀请注册', open: '开放注册', save: '保存注册策略',
  refresh: '重新读取注册策略', loadFailed: '注册策略读取失败，请重试。', saveFailed: '注册策略保存未确认。',
  unconfirmed: '结果或版本未确认，请重新读取策略后再修改；不会自动重放保存请求。'
} as const;
const en: Record<keyof typeof zh, string> = {
  mode: 'Registration mode', disabled: 'Registration disabled', invited: 'Invitation only', open: 'Open registration', save: 'Save registration policy',
  refresh: 'Reload registration policy', loadFailed: 'Could not load the registration policy. Try again.', saveFailed: 'The policy save is unconfirmed.',
  unconfirmed: 'The result or version is unconfirmed. Reload before making changes. Saving is never replayed automatically.'
};
export function registrationPolicyText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
