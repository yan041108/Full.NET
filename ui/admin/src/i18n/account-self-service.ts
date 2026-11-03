const zh = {
  registerTitle: '创建账号', recoverTitle: '找回密码', displayName: '显示名称', email: '邮箱',
  password: '密码', newPassword: '新密码', code: '邮箱验证码', send: '发送验证码',
  register: '注册', recover: '更新密码', back: '返回登录', required: '请先获取并填写邮箱验证码。',
  sent: '若此邮箱符合条件，验证码已发送，请使用最新验证码。',
  recoverySent: '若账号存在，找回密码验证码已发送，请使用最新验证码。',
  sendFailed: '验证码发送未完成，请重新获取验证码后再试。',
  registerFailed: '注册失败，请检查输入后重试。', recoverFailed: '密码更新失败，请检查验证码后重试。',
  registered: '账号已创建，请登录。', recovered: '密码已更新，请使用新密码登录。',
  invalidInvitation: '邀请无效或已过期。'
} as const;
const en: Record<keyof typeof zh, string> = {
  registerTitle: 'Create account', recoverTitle: 'Recover password', displayName: 'Display name', email: 'Email',
  password: 'Password', newPassword: 'New password', code: 'Email verification code', send: 'Send code',
  register: 'Register', recover: 'Update password', back: 'Back to sign in', required: 'Request and enter an email verification code first.',
  sent: 'If the address is eligible, a code was sent. Use the latest code.',
  recoverySent: 'If the account exists, a recovery code was sent. Use the latest code.',
  sendFailed: 'The code request did not complete. Request a new code before trying again.',
  registerFailed: 'Registration failed. Check your input and try again.', recoverFailed: 'Password update failed. Check the code and try again.',
  registered: 'Account created. You can sign in now.', recovered: 'Password updated. Sign in with the new password.',
  invalidInvitation: 'Invitation is invalid or expired.'
};
export function accountSelfServiceText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
