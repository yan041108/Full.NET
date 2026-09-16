/** Vue 管理端身份认证模式；默认保持 legacy 直至 T08 显式启用 OIDC 中心登录。 */
export type AdminIdentityAuthMode = 'legacy' | 'oidc-center';

/**
 * 解析管理端身份认证模式。
 * 仅接受显式 `oidc-center`；空值、未知值与未配置均回落到 legacy。
 */
export function resolveAdminIdentityAuthMode(
  configuredValue: string | undefined | null
): AdminIdentityAuthMode {
  if (configuredValue === 'oidc-center') {
    return 'oidc-center';
  }

  return 'legacy';
}