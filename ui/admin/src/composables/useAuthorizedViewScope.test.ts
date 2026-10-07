import { afterEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { createOutputSession } from '../test/data-output-fixtures';
import { useAuthorizedViewScope } from './useAuthorizedViewScope';
let wrapper: VueWrapper | undefined;
afterEach(() => { wrapper?.unmount(); wrapper = undefined; });
function fixture(keepAlive = false) {
  const { pinia, session } = createOutputSession(['output.read']); const reset = vi.fn(); const resume = vi.fn();
  let scope!: ReturnType<typeof useAuthorizedViewScope>; const show = ref(true);
  const View = defineComponent({ setup() { scope = useAuthorizedViewScope(session, reset, resume); return () => h('div'); } });
  const Other = defineComponent({ setup: () => () => h('span') });
  wrapper = mount(keepAlive ? defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => h(show.value ? View : Other) }) }) : View, { global: { plugins: [pinia] } });
  return { scope, session, reset, resume, show };
}
describe('受保护页面请求归属', () => {
  it('KeepAlive 激活与同步上下文替换同轮发生时只恢复一次', async () => {
    const { show, session, resume } = fixture(true); show.value = false; await flushPromises();
    show.value = true; session.currentUser!.tenantId = 'new-tenant'; session.currentUser!.sessionId = 'new-session'; await flushPromises();
    expect(resume).toHaveBeenCalledTimes(2);
  });
  it('未知权限不创建请求；结束后的请求不参与后续取消', () => {
    const { scope, session } = fixture(); expect(scope.begin('output.write')).toBeUndefined();
    const request = scope.begin('output.read')!; request.finish(); session.currentUser!.tenantId = 'changed';
    expect(request.current()).toBe(false); expect(request.signal.aborted).toBe(false);
  });
  it.each(['state', 'sessionId', 'tenantId', 'permissions'] as const)('%s 变化同步取消旧请求并清理结果', async field => {
    const { scope, session, reset } = fixture(); const request = scope.begin('output.read')!;
    if (field === 'state') session.state = 'anonymous';
    else if (field === 'permissions') session.currentUser!.permissions = [];
    else session.currentUser![field] = 'changed';
    expect(request.signal.aborted).toBe(true); expect(request.current()).toBe(false); expect(reset).toHaveBeenCalled(); await flushPromises();
  });
  it('同一轮快照的多字段更新只恢复最新代次', async () => {
    const { session, resume } = fixture(); expect(resume).toHaveBeenCalledTimes(1);
    session.currentUser!.tenantId = 'changed'; session.currentUser!.sessionId = 'changed-session'; await flushPromises();
    expect(resume).toHaveBeenCalledTimes(2);
  });
  it('KeepAlive 离开取消请求，回到页面只恢复一次', async () => {
    const { scope, show, resume } = fixture(true); expect(resume).toHaveBeenCalledTimes(1); const request = scope.begin('output.read')!;
    show.value = false; await flushPromises(); expect(request.signal.aborted).toBe(true); expect(scope.begin('output.read')).toBeUndefined();
    show.value = true; await flushPromises(); expect(resume).toHaveBeenCalledTimes(2); expect(scope.begin('output.read')?.current()).toBe(true);
  });
});
