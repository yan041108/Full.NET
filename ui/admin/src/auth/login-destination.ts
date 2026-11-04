const key = 'fullnet.admin.login.destination';
const destination = '/tenant-context';
const lifetimeMs = 15 * 60_000;
let transient: string | null | undefined;

/** 当前只有邀请/注册接续需要指定落点，拒绝把任意 URL 当成登录后的跳转地址。 */
export function resolveLoginDestination(value: unknown): string | undefined {
  return value === destination ? destination : undefined;
}
export function rememberLoginDestination(value: unknown): void {
  const path = resolveLoginDestination(value);
  transient = path ? JSON.stringify({ path, expiresAt: Date.now() + lifetimeMs }) : null;
  try { if (transient) sessionStorage.setItem(key, transient); else sessionStorage.removeItem(key); }
  catch { /* 受限存储只保留当前应用内存，不改写 URL 或持久存储。 */ }
}
export function takeLoginDestination(): string | undefined {
  let raw = transient;
  try { if (raw === undefined) raw = sessionStorage.getItem(key); sessionStorage.removeItem(key); }
  catch { /* 已明确消费后不能因存储删除失败重新接续。 */ }
  transient = null;
  try {
    const value: unknown = JSON.parse(raw ?? 'null');
    if (typeof value !== 'object' || value === null || !('path' in value) || !('expiresAt' in value)
      || typeof value.expiresAt !== 'number' || !Number.isSafeInteger(value.expiresAt)
      || value.expiresAt <= Date.now() || value.expiresAt > Date.now() + lifetimeMs) return undefined;
    return resolveLoginDestination(value.path);
  } catch { return undefined; }
}
