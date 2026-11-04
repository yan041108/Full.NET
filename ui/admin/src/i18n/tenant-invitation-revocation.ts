const zh = {
  title: '撤销企业邀请', close: '关闭', confirm: '确认撤销', refresh: '核对当前邀请状态', version: '邀请版本',
  role: '受邀身份', admin: '管理员', member: '成员', created: '签发时间', expires: '有效期至',
  hint: '仅撤销尚未接受的邀请。已加入企业的成员应使用成员移除流程处理。',
  loading: '正在读取当前邀请，可关闭取消等待。', saving: '正在撤销。关闭仅停止等待，服务端仍可能完成。',
  unknown: '撤销结果尚未确认。请先核对当前邀请状态，不会自动重发。',
  failed: '无法确认当前邀请状态，请核对后再操作。', accepted: '邀请已接受，不能通过撤销移除成员。',
  expired: '邀请已过期，无需再次撤销。', observed: '当前邀请状态为已撤销，无需再次提交。', success: '邀请已撤销。'
} as const;
const en: Record<keyof typeof zh, string> = {
  title: 'Revoke organization invitation', close: 'Close', confirm: 'Confirm revocation', refresh: 'Read current invitation', version: 'Invitation revision',
  role: 'Invited role', admin: 'Administrator', member: 'Member', created: 'Issued at', expires: 'Expires at',
  hint: 'Revoke an invitation that has not been accepted. Use member removal for an existing membership.',
  loading: 'Reading the current invitation. Close to stop waiting.', saving: 'Revoking. Closing stops waiting; the server may still complete the request.',
  unknown: 'The revocation result is unconfirmed. Read the current invitation first. No automatic retry will occur.',
  failed: 'The current invitation could not be confirmed. Read it again before continuing.', accepted: 'The invitation was accepted. Revocation cannot remove a member.',
  expired: 'The invitation expired. No further revocation is needed.', observed: 'This invitation is already revoked. No further submission is needed.', success: 'Invitation revoked.'
};
export function tenantInvitationRevocationText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
