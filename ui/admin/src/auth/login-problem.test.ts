import { describe, expect, it } from 'vitest';
import { resolveLoginProblemTitle } from './login-problem';

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
});
