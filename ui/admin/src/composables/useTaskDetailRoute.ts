import { inject, watch } from 'vue';
import { routeLocationKey, routerKey } from 'vue-router';

/** 来源页按固定路由解释任务深链；上下文变化清除旧身份目标，不接纳任意路径。 */
export function useTaskDetailRoute(options: {
  routeName: string;
  enabled: () => boolean;
  context: () => string;
  open: (id: string) => Promise<void>;
  close: () => void;
}) {
  const route = inject(routeLocationKey, undefined), router = inject(routerKey, undefined);
  let opened: string | undefined;
  function clear() {
    if (!route || !router || route.name !== options.routeName || route.query.taskId === undefined) return;
    const { taskId: _taskId, ...query } = route.query; void router.replace({ query });
  }
  watch(() => [route?.name, route?.query.taskId, options.enabled(), options.context()] as const, (value, previous) => {
    if (!route) return;
    if (previous && previous[3] !== value[3]) { opened = undefined; options.close(); clear(); return; }
    if (!value[2] || value[0] !== options.routeName) { opened = undefined; options.close(); return; }
    const id = value[1];
    if (typeof id !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) {
      if (opened) options.close(); opened = undefined; return;
    }
    if (opened !== id) { opened = id; void options.open(id); }
  }, { immediate: true, flush: 'sync' });
  return { clear };
}
