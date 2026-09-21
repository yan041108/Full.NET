import type { FullNetProblemDetails } from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';

/** 登录页展示用文案：将常见 ProblemDetails 映射为可操作的本地化说明。 */
export function resolveLoginProblemMessageKey(
  problem: FullNetProblemDetails
): MessageKey | undefined {
  switch (problem.code) {
    case 'authorization.permission_denied':
      return 'auth.insufficientPermissions';
    case 'identity.password_change_required':
      return 'auth.passwordChangeRequired';
    default:
      return undefined;
  }
}

export function resolveLoginProblemTitle(
  problem: FullNetProblemDetails,
  translate: (key: MessageKey) => string
): string {
  const key = resolveLoginProblemMessageKey(problem);
  if (key !== undefined) {
    return translate(key);
  }

  if (problem.title && problem.title.length > 0) {
    return problem.title;
  }

  if (problem.detail && problem.detail.length > 0) {
    return problem.detail;
  }

  return translate('auth.loginFailed');
}
