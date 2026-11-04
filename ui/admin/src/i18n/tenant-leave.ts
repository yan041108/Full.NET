const zh = {
  close: '关闭', accountHint: '仅退出当前企业，不删除全局账号，也不影响其他企业的成员身份。',
  loading: '正在读取当前成员状态，可关闭取消等待。', saving: '正在退出。关闭仅停止等待，服务端仍可能完成。',
  unknown: '退出结果尚未确认。请核对当前成员状态；不会自动重发退出。', refresh: '读取当前成员状态', version: '成员版本'
} as const;
const en: Record<keyof typeof zh, string> = {
  close: 'Close', accountHint: 'Leave this organization only. Your global account and other memberships remain active.',
  loading: 'Reading your membership. Close to stop waiting.', saving: 'Leaving. Closing stops waiting; the server may still complete the request.',
  unknown: 'The leave result is unconfirmed. Review your current membership. The request will not be sent again automatically.',
  refresh: 'Read current membership', version: 'Membership revision'
};
export function tenantLeaveText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
