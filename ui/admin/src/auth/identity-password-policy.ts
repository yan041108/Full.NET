/** 与 Identity 模块 Host 用户密码策略保持一致的最小长度。 */
export const IDENTITY_PASSWORD_MIN_LENGTH = 12;

/** 以下模式需与服务端密码策略保持同语义，避免前后端校验结果漂移。 */
const UPPERCASE_PATTERN = /\p{Lu}/u;
const LOWERCASE_PATTERN = /\p{Ll}/u;
const DIGIT_PATTERN = /\p{Nd}/u;
const NON_ALPHANUMERIC_PATTERN = /[^\p{L}\p{Nd}]/u;

/** 校验密码是否满足平台 Identity 密码策略。 */
export function isIdentityPasswordValid(password: string): boolean {
  if (!password || password.length < IDENTITY_PASSWORD_MIN_LENGTH) {
    return false;
  }

  // 服务端按 char（UTF-16 单元）分类；不能将代理对合并为 Unicode 码点。
  const characters = password.split('');
  if (!characters.some(character => UPPERCASE_PATTERN.test(character))) {
    return false;
  }

  if (!characters.some(character => LOWERCASE_PATTERN.test(character))) {
    return false;
  }

  if (!characters.some(character => DIGIT_PATTERN.test(character))) {
    return false;
  }

  if (!characters.some(character => NON_ALPHANUMERIC_PATTERN.test(character))) {
    return false;
  }

  return true;
}
