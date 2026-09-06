/** 生成审批动作幂等键；优先使用平台 crypto，旧运行时回退到时间戳随机串。 */
export function createIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  return `idem-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
