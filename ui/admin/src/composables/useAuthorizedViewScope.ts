import { onActivated, onBeforeUnmount, onDeactivated, onMounted, watch } from 'vue';
import type { useSessionStore } from '../auth/session';

type Session = Pick<ReturnType<typeof useSessionStore>, 'state' | 'currentUser' | 'can'>;

/** 把异步结果限定到当前账号、会话、租户、权限与页面激活代次，取消不承诺回滚服务端写入。 */
export function useAuthorizedViewScope(session: Session, reset: () => void, resume: () => void | Promise<void>) {
  let generation = 0; let active = true; let mounted = false;
  let resumedGeneration = -1;
  const controllers = new Set<AbortController>();
  function resumeCurrent(): void {
    if (!mounted || !active || session.state !== 'authenticated' || resumedGeneration === generation) return;
    // 激活回调与上下文更新可能落在同一轮，当前代次只允许恢复一次。
    resumedGeneration = generation;
    void resume();
  }
  function scheduleResume(): void {
    const ticket = generation;
    // Store 同步替换多项快照时先全部失效，再在同一轮写入结束后仅恢复最新代次。
    queueMicrotask(() => { if (ticket === generation) resumeCurrent(); });
  }
  function invalidate(): void {
    generation++;
    for (const controller of controllers) controller.abort();
    controllers.clear(); reset();
  }
  function begin(permission: string) {
    if (!active || !mounted || !session.can(permission)) return undefined;
    const controller = new AbortController(); controllers.add(controller); const ticket = generation;
    return {
      signal: controller.signal,
      current: () => active && mounted && ticket === generation && !controller.signal.aborted && session.can(permission),
      cancel: () => { controller.abort(); controllers.delete(controller); },
      finish: () => controllers.delete(controller)
    };
  }
  // 同步失效，确保撤权或上下文切换之后的 Promise continuation 无法接入旧结果。
  watch(() => JSON.stringify([session.state, session.currentUser?.id, session.currentUser?.sessionId,
    session.currentUser?.tenantId, session.currentUser?.scope, session.currentUser?.actorScope, session.currentUser?.permissions]), () => {
    invalidate(); scheduleResume();
  }, { flush: 'sync' });
  const suspend = () => { active = false; invalidate(); };
  onMounted(() => { mounted = true; resumeCurrent(); });
  onActivated(() => { if (!active) { active = true; resumeCurrent(); } });
  onDeactivated(suspend);
  onBeforeUnmount(() => { mounted = false; suspend(); });
  return { begin, invalidate };
}
