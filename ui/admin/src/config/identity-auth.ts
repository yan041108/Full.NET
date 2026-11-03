import { resolveAdminIdentityAuthMode } from '@fullnet/client-contracts';

/** 管理端身份认证模式；默认 legacy，T08 可通过 VITE_IDENTITY_AUTH_MODE=oidc-center 启用。 */
export const adminIdentityAuthMode = resolveAdminIdentityAuthMode(
  import.meta.env.VITE_IDENTITY_AUTH_MODE
);

/** BFF 机密客户端的公开标识；secret 仅由服务端配置。 */
export function resolveAdminOidcClientId(): string {
  const configured = import.meta.env.VITE_IDENTITY_OIDC_CLIENT_ID;
  return typeof configured === 'string' && configured.length > 0
    ? configured
    : 'fullnet-admin-bff';
}
