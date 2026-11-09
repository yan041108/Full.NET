import { describe, expect, it } from 'vitest';
import { resolveLoginProblemTitle, toLoginProblem } from './login-problem';

describe('resolveLoginProblemTitle', () => {
  it('maps permission_denied to actionable copy', () => {
    const title = resolveLoginProblemTitle({
      status: 403,
      code: 'authorization.permission_denied',
      title: '当前身份没有所需权限。'
    }, key => key === 'auth.insufficientPermissions'
      ? '未分配角色'
      : 'fallback');

    expect(title).toBe('未分配角色');
  });

  it.each([
    ['identity.csrf_validation_failed', 'auth.loginCsrfFailed'],
    ['identity.session_not_active', 'auth.loginSessionUnavailable'],
    ['identity.session_context_conflict', 'auth.loginSessionUnavailable']
  ])('保留 %s 和排障标识并选择对应说明', (code, key) => {
    const problem = { status: 403, code, traceId: 'trace-login' };
    expect(toLoginProblem(problem, 'client.login_failed')).toBe(problem);
    expect(resolveLoginProblemTitle(problem, value => value)).toBe(key);
  });

  it('从限流响应保留等待秒数', () => {
    const title = resolveLoginProblemTitle(
      { status: 429, code: 'hosting.rate_limit.exceeded', retryAfterSeconds: 8 },
      (key, parameters) => `${key} ${parameters?.seconds}`
    );
    expect(title).toBe('auth.loginRateLimitedWithDelay 8');
  });
});
