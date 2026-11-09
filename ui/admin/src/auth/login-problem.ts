import { FullNetNetworkError, isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import type { MessageKey, MessageParameters } from '@fullnet/admin-i18n';

/** 登录页保留服务器的 code/traceId，连接失败只使用稳定的本地诊断码。 */
export function toLoginProblem(error: unknown, fallbackCode: string): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) return error;
  if (error instanceof FullNetNetworkError) return { status: 0, code: 'client.network_unavailable' };
  return { status: 500, code: fallbackCode };
}

/** 登录页展示用文案：将常见 ProblemDetails 映射为可操作的本地化说明。 */
export function resolveLoginProblemMessageKey(
  problem: FullNetProblemDetails
): MessageKey | undefined {
  switch (problem.code) {
    case 'authorization.permission_denied':
      return 'auth.insufficientPermissions';
    case 'identity.password_change_required':
      return 'auth.passwordChangeRequired';
    case 'identity.csrf_validation_failed':
      return 'auth.loginCsrfFailed';
    case 'identity.session_not_active':
    case 'identity.session_context_conflict':
    case 'identity.invalid_refresh_token':
    case 'identity.refresh_token_reuse_detected':
    case 'identity.browser_session_unavailable':
      return 'auth.loginSessionUnavailable';
    case 'client.network_unavailable':
      return 'auth.loginNetworkUnavailable';
    case 'client.oidc_login_failed':
      return 'auth.oidcCenterFailed';
    default:
      return problem.status === 429 ? 'auth.loginRateLimited' : undefined;
  }
}

export function resolveLoginProblemTitle(
  problem: FullNetProblemDetails,
  translate: (key: MessageKey, parameters?: MessageParameters) => string
): string {
  if (problem.status === 429 && problem.retryAfterSeconds && problem.retryAfterSeconds > 0) {
    return translate('auth.loginRateLimitedWithDelay', { seconds: problem.retryAfterSeconds });
  }
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
