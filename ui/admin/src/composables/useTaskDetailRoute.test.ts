import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, reactive, ref } from 'vue';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { routeLocationKey, routerKey } from 'vue-router';
import { useTaskDetailRoute } from './useTaskDetailRoute';
const wrappers: VueWrapper[] = [];
const id = '019a45b7-2356-7000-8000-000000000001';
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
function render(query: Record<string, unknown> = { taskId: id }) {
  const route = reactive({ name: 'imports', query }), enabled = ref(true), context = ref('tenant|user|session');
  const open = vi.fn().mockResolvedValue(undefined), close = vi.fn(), replace = vi.fn(); let clear!: () => void;
  const component = defineComponent({ setup() { ({ clear } = useTaskDetailRoute({ routeName: 'imports', enabled: () => enabled.value,
    context: () => context.value, open, close })); return () => h('div'); } });
  const wrapper = mount(component, { global: { provide: { [routeLocationKey as symbol]: route, [routerKey as symbol]: { replace } } } }); wrappers.push(wrapper);
  return { route, enabled, context, open, close, replace, clear: () => clear() };
}
describe('来源详情深链', () => {
  it('初始有效目标只打开一次，重复渲染不重复请求', async () => {
    const state = render(); await flushPromises(); expect(state.open).toHaveBeenCalledExactlyOnceWith(id);
    state.route.query = { taskId: id, other: 'kept' }; await flushPromises(); expect(state.open).toHaveBeenCalledTimes(1);
    state.clear(); expect(state.replace).toHaveBeenCalledWith({ query: { other: 'kept' } });
  });
  it('主体变化清除旧目标，权限失效及离开路由关闭详情', async () => {
    const state = render(); state.context.value = 'other|user|session'; await flushPromises();
    expect(state.close).toHaveBeenCalled(); expect(state.replace).toHaveBeenCalledWith({ query: {} }); expect(state.open).toHaveBeenCalledTimes(1);
    state.enabled.value = false; await flushPromises(); state.route.name = 'other'; await flushPromises(); state.clear();
    expect(state.replace).toHaveBeenCalledTimes(1);
  });
  it('不把数组、任意路径或损坏的标识交给来源', async () => {
    for (const taskId of [[id], '/admin/users', 'invalid']) {
      const state = render({ taskId }); await flushPromises(); expect(state.open).not.toHaveBeenCalled();
    }
  });
});
