import { useSessionStore } from './session';

/** 基于当前会话权限快照提供失败关闭的权限判断。 */
export function usePermission() {
  const session = useSessionStore();

  /** 空权限码或非字符串输入一律拒绝，避免把配置缺失误判为放行。 */
  function can(code: string): boolean {
    if (typeof code !== 'string' || code.length === 0) {
      return false;
    }

    return session.can(code);
  }

  return { can };
}
