const key = 'fullnet.registration.invitation';
export interface RegistrationInvitation { invitationId: string; invitationToken: string }
// undefined 表示尚未读取，null 表示本应用已明确清除，避免受限存储重新带回旧凭据。
let transient: RegistrationInvitation | null | undefined;

/** 凭据仅保留在当前标签页；受限浏览器中仍可在当前页面完成注册。 */
export function rememberRegistrationInvitation(value: RegistrationInvitation): void {
  transient = value;
  try { sessionStorage.setItem(key, JSON.stringify(value)); } catch { /* 存储不可用时不写入其他持久介质。 */ }
}
export function readRegistrationInvitation(): RegistrationInvitation | undefined {
  if (transient !== undefined) return transient ?? undefined;
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(key) ?? 'null');
    if (typeof value === 'object' && value !== null && 'invitationId' in value && 'invitationToken' in value
      && typeof value.invitationId === 'string' && typeof value.invitationToken === 'string') return value as RegistrationInvitation;
  } catch { /* 损坏或不可访问的存储不构成邀请证明。 */ }
  return undefined;
}
export function forgetRegistrationInvitation(): void {
  transient = null;
  try { sessionStorage.removeItem(key); } catch { /* 服务端仍负责令牌一次性与撤销校验。 */ }
}
