import { resolveAdminIdentityAuthMode } from '@fullnet/client-contracts';

/** 管理端身份认证模式；默认 legacy，T08 可通过 VITE_IDENTITY_AUTH_MODE=oidc-center 启用。 */
export const adminIdentityAuthMode = resolveAdminIdentityAuthMode(
  import.meta.env.VITE_IDENTITY_AUTH_MODE
);