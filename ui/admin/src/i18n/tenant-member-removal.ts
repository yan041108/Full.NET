const zh = {
  title: '移除企业成员', close: '关闭', confirm: '确认移除', refresh: '核对当前成员状态',
  hint: '仅移除该成员在当前企业的身份，不删除全局账号或其他企业的身份。',
  loading: '正在核对成员当前状态，可关闭取消等待。', saving: '正在移除。关闭仅停止等待，服务端仍可能完成。',
  unknown: '移除结果尚未确认。请先核对当前成员状态，不会自动重发。',
  failed: '无法确认当前成员状态，请核对后再操作。', owner: '所有者受到保护，不能在此移除。',
  removed: '当前成员状态为已移除，无需再次移除。', success: '成员已移除。', version: '成员版本'
} as const;
const en: Record<keyof typeof zh, string> = {
  title: 'Remove organization member', close: 'Close', confirm: 'Confirm removal', refresh: 'Read current membership',
  hint: 'Remove this membership only. The global account and other organization memberships remain active.',
  loading: 'Reading current membership. Close to stop waiting.', saving: 'Removing. Closing stops waiting; the server may still complete the request.',
  unknown: 'The removal result is unconfirmed. Read the current membership first. No automatic retry will occur.',
  failed: 'The current membership could not be confirmed. Read it again before continuing.', owner: 'The owner is protected and cannot be removed here.',
  removed: 'This membership is already removed. No further removal is needed.', success: 'Member removed.', version: 'Membership revision'
};
export function tenantMemberRemovalText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
