/** 与 Identity 模块 Host 用户密码策略保持一致的最小长度。 */
export const IDENTITY_PASSWORD_MIN_LENGTH = 12;

const UPPERCASE_PATTERN = /[A-Z]/;
const LOWERCASE_PATTERN = /[a-z]/;
const DIGIT_PATTERN = /\d/;
const NON_ALPHANUMERIC_PATTERN = /[^A-Za-z0-9]/;

/** 校验密码是否满足平台 Identity 密码策略。 */
export function isIdentityPasswordValid(password: string): boolean {
  if (!password || password.length < IDENTITY_PASSWORD_MIN_LENGTH) {
    return false;
  }

  if (!UPPERCASE_PATTERN.test(password)) {
    return false;
  }

  if (!LOWERCASE_PATTERN.test(password)) {
    return false;
  }

  if (!DIGIT_PATTERN.test(password)) {
    return false;
  }

  if (!NON_ALPHANUMERIC_PATTERN.test(password)) {
    return false;
  }

  return true;
}
