import { onActivated, onBeforeUnmount, onDeactivated, onMounted, watch } from 'vue';

/** 仅为可见且激活的在途任务页面定期读取；串行等待，失败后由手动刷新恢复。 */
export function useTaskStatusRefresh(enabled: () => boolean, refresh: (current: () => boolean) => Promise<void>): void {
  // 五秒是页面观察间隔，不改变服务端任务执行或重试频率。
  const intervalMilliseconds = 5_000;
  let mounted = false; let active = true; let running = false; let blocked = false; let generation = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const eligible = () => mounted && active && !document.hidden && !blocked && enabled();
  function cancelSchedule(): void {
    generation++;
    if (timer !== undefined) { clearTimeout(timer); timer = undefined; }
  }
  function schedule(): void {
    if (!eligible() || running || timer !== undefined) return;
    timer = setTimeout(async () => {
      timer = undefined;
      if (!eligible()) return;
      running = true; const ticket = generation;
      try { await refresh(() => ticket === generation && eligible()); }
      catch {
        // 页面负责呈现请求错误；调度边界仍观察拒绝，避免无人处理的 Promise 和重试风暴。
        if (ticket === generation) blocked = true;
      } finally {
        running = false;
        // 旧读取结束后也重新核对当前页面，不能让旧代次恢复已经停止的轮询。
        schedule();
      }
    }, intervalMilliseconds);
  }
  watch(enabled, () => { blocked = false; cancelSchedule(); schedule(); }, { flush: 'sync' });
  const visibilityChanged = () => { cancelSchedule(); schedule(); };
  const suspend = () => { active = false; cancelSchedule(); };
  onMounted(() => { mounted = true; document.addEventListener('visibilitychange', visibilityChanged); schedule(); });
  onActivated(() => { active = true; schedule(); });
  onDeactivated(suspend);
  onBeforeUnmount(() => { mounted = false; suspend(); document.removeEventListener('visibilitychange', visibilityChanged); });
}
