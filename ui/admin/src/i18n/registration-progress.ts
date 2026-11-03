const zh = {
  title: '注册入驻进度', description: '账号已创建。企业成员开通完成后，即可进入企业。',
  empty: '暂无注册入驻记录。', fallback: '注册入驻', refresh: '刷新进度', continue: '继续办理',
  Requested: '等待开通', Reserved: '正在开通', Confirmed: '正在激活', Activated: '已加入企业',
  Cancelling: '正在恢复', Cancelled: '开通未完成', failed: '进度加载失败，请刷新重试。',
  waiting: '开通尚未完成，请稍后刷新或重试。', complete: '已加入企业，企业列表已更新。',
  stopped: '自动刷新已暂停，可手动刷新。', error: '暂时无法开通，请重试；如仍失败请联系企业管理员。'
} as const;
const en: Record<keyof typeof zh, string> = {
  title: 'Registration progress', description: 'Your account is ready. Enter the organization once membership is activated.',
  empty: 'No registration records.', fallback: 'Organization registration', refresh: 'Refresh progress', continue: 'Continue',
  Requested: 'Waiting', Reserved: 'Provisioning', Confirmed: 'Activating', Activated: 'Joined',
  Cancelling: 'Recovering', Cancelled: 'Not completed', failed: 'Could not load progress. Refresh to retry.',
  waiting: 'Membership is not ready. Refresh or retry later.', complete: 'Membership activated. Organizations refreshed.',
  stopped: 'Automatic refresh paused. Refresh manually.', error: 'Membership could not be activated. Retry or contact your organization administrator.'
};
export function registrationProgressText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
