import { describe, expect, it } from 'vitest';
import { isIdentityPasswordValid } from './identity-password-policy';

describe('Identity 密码策略', () => {
  it('接受满足长度、大小写、数字与特殊字符的密码', () => {
    expect(isIdentityPasswordValid('FullNet!2026')).toBe(true);
  });

  it('拒绝过短或缺少字符类别的密码', () => {
    expect(isIdentityPasswordValid('')).toBe(false);
    expect(isIdentityPasswordValid('short')).toBe(false);
    expect(isIdentityPasswordValid('nouppercase1!')).toBe(false);
    expect(isIdentityPasswordValid('NOLOWERCASE1!')).toBe(false);
    expect(isIdentityPasswordValid('NoDigitsHere!')).toBe(false);
    expect(isIdentityPasswordValid('NoSpecialChar1')).toBe(false);
  });
});
