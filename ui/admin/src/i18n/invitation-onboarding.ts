const zh = {
  way: '新账号注册方式（可选）', wayHint: '选择后可生成注册邀请；已有账号仍使用原账号登录。',
  noWay: '仅邀请已有账号', waysFailed: '注册方式加载失败，请重新打开邀请表单。',
  link: '注册邀请链接', copy: '复制', copyFailed: '复制失败，请手动选择并复制。',
  processing: '已接受，等待成员激活。可稍后刷新或重试。', refresh: '刷新邀请与企业', retry: '重试激活',
  existing: '已有账号？使用原账号登录并接受邀请',
  tokenHint: '尚未绑定的邀请请使用邀请令牌接受。资料邮箱相同不会自动显示邀请。'
} as const;
const en: Record<keyof typeof zh, string> = {
  way: 'Registration option for a new account (optional)', wayHint: 'Select to create a registration invitation. Existing users keep their original sign-in.',
  noWay: 'Invite an existing account only', waysFailed: 'Registration options could not load. Reopen the invitation form.',
  link: 'Registration invitation link', copy: 'Copy', copyFailed: 'Copy failed. Select and copy the value manually.',
  processing: 'Accepted, awaiting membership activation. Refresh or retry later.', refresh: 'Refresh invitations and organizations', retry: 'Retry activation',
  existing: 'Already have an account? Sign in and accept the invitation',
  tokenHint: 'Use the invitation token for an unbound invitation. A matching profile email does not list invitations automatically.'
};
export function invitationOnboardingText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
