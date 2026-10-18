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

describe('服务端 UTF-16 字符分类契约', () => {
  it.each(['Äbcdefghijk1!', 'Abcdefghijk١!', 'Äbcdefghijk1𝒜'])('accepts supported Unicode password %s', password => expect(isIdentityPasswordValid(password)).toBe(true));
  it.each(['Abcdefghijk1Ä', 'Abcdefghijk²!', '𝒜bcdefghijk1!'])('rejects missing server character categories %s', password => expect(isIdentityPasswordValid(password)).toBe(false));
});
