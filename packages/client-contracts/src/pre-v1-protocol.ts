const legacyToCanonical = {
  'identity.bootstrap.invalid-password': 'identity.bootstrap.invalid_password',
  'identity.bootstrap.invalid-profile': 'identity.bootstrap.invalid_profile',
  'identity.login-succeeded': 'identity.login_succeeded',
  'tenancy.domain-exists': 'tenancy.domain_exists',
  'tenancy.host-not-found': 'tenancy.host_not_found',
  'tenancy.identifier-exists': 'tenancy.identifier_exists',
  'tenancy.not-found': 'tenancy.not_found',
} as const;

/** 将 Pre-v1 legacy error_code 规范化为 canonical；未知值原样返回。 */
export function normalizePreV1ErrorCode(code: string): string {
  return legacyToCanonical[code as keyof typeof legacyToCanonical] ?? code;
}

/** 判断客户端是否应把两个 error_code 视为同一语义（迁移期双识别）。 */
export function areEquivalentPreV1ErrorCodes(left: string, right: string): boolean {
  return normalizePreV1ErrorCode(left) === normalizePreV1ErrorCode(right);
}
