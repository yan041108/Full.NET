import { onScopeDispose, ref, watch } from 'vue';

/** 仅轮询权威读取结果；上下文变化取消并丢弃旧响应，不重放写操作。 */
export function useBoundedPolling<T>(options: {
  enabled: () => boolean;
  pending: () => boolean;
  key: () => string;
  read: (signal: AbortSignal) => Promise<T>;
  accept: (value: T) => void;
  failed: (error: unknown) => void;
  intervalMs?: number;
}) {
  const paused = ref(false);
  let timer: ReturnType<typeof setTimeout> | undefined, timeout: ReturnType<typeof setTimeout> | undefined;
  let cancellation: AbortController | undefined, generation = 0, deadline = 0;
  function stop() {
    generation++; if (timer !== undefined) clearTimeout(timer); if (timeout !== undefined) clearTimeout(timeout);
    timer = undefined; timeout = undefined; cancellation?.abort(); cancellation = undefined;
  }
  function eligible() {
    if (!options.enabled() || !options.pending() || paused.value || document.hidden) return false;
    if (Date.now() >= deadline) { paused.value = true; return false; }
    return true;
  }
  function schedule() {
    if (eligible()) timer = setTimeout(() => { timer = undefined; void poll(); }, options.intervalMs ?? 2000);
  }
  async function poll() {
    if (!eligible()) return;
    const current = generation, controller = new AbortController(); cancellation = controller;
    timeout = setTimeout(() => {
      if (current !== generation) return;
      paused.value = true; controller.abort(); options.failed(new Error('client.background_refresh_timeout'));
    }, Math.min(30_000, Math.max(1, deadline - Date.now())));
    try {
      const result = await options.read(controller.signal);
      if (current === generation && !controller.signal.aborted && options.enabled()) options.accept(result);
    } catch (error) {
      if (current === generation && !controller.signal.aborted) { paused.value = true; options.failed(error); }
    } finally {
      if (current === generation) {
        if (timeout !== undefined) clearTimeout(timeout);
        timeout = undefined; cancellation = undefined; schedule();
      }
    }
  }
  function restart() { stop(); paused.value = false; deadline = Date.now() + 5 * 60_000; schedule(); }
  watch([options.enabled, options.pending, options.key], restart, { immediate: true, flush: 'sync' });
  // 隐藏期间保留原截止时间，恢复可见不会无限延长本轮预算。
  function visibilityChanged() { stop(); schedule(); }
  document.addEventListener('visibilitychange', visibilityChanged);
  onScopeDispose(() => { stop(); document.removeEventListener('visibilitychange', visibilityChanged); });
  return { paused, restart, stop };
}
