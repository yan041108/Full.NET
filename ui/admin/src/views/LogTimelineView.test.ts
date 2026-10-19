import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import LogTimelineView from './LogTimelineView.vue';
import { useSessionStore } from '../auth/session';
import { getLogTimeline } from '../api/log-timeline';

vi.mock('../api/log-timeline', () => ({ getLogTimeline: vi.fn() }));
vi.mock('vue-router', () => ({ useRoute: () => ({ fullPath: '/auditing/log-timeline', query: {
  traceId: 'a'.repeat(32), fromUtc: '2026-10-10T00:00:00Z', toUtc: '2026-10-10T02:00:00Z'
} }) }));
const getTimeline = vi.mocked(getLogTimeline);
function setup(permissions = ['auditing.timeline.read']) {
  const pinia = createPinia(); setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = { id: '01912345-6789-7abc-8def-0123456789ab', username: 'reader', displayName: 'reader',
    tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false,
    permissions, sessionId: '01912345-6789-7abc-8def-0123456789ab', preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(LogTimelineView, { global: { plugins: [pinia] } });
  return { session, wrapper };
}
describe('日志时间线页', () => {
  beforeEach(() => getTimeline.mockReset().mockResolvedValue({
    fromUtc: '2026-10-10T00:00:00Z', toUtc: '2026-10-10T02:00:00Z', entries: [],
    sources: [{ source: 'runtime.local_file', status: 'bounded_scan' }], partial: true
  }));
  it('沿用详情页事件时间窗口并展示来源覆盖状态', async () => {
    const { wrapper } = setup(); await flushPromises();
    expect(getTimeline).toHaveBeenCalledWith(expect.objectContaining({
      traceId: 'a'.repeat(32), fromUtc: '2026-10-10T00:00:00Z', toUtc: '2026-10-10T02:00:00Z'
    }), expect.any(AbortSignal));
    expect(wrapper.text()).toContain('bounded_scan');
    wrapper.unmount();
  });
  it('没有入口权限时不发查询', async () => {
    const { wrapper } = setup([]); await flushPromises();
    expect(getTimeline).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('权限撤销时取消在途请求并丢弃迟到响应', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof getLogTimeline>>) => void;
    getTimeline.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const { session, wrapper } = setup(); await flushPromises();
    const signal = getTimeline.mock.calls[0]![1]!;
    session.currentUser!.permissions = []; await flushPromises();
    expect(signal.aborted).toBe(true);
    resolve({ fromUtc: '', toUtc: '', entries: [], sources: [{ source: 'sensitive-source', status: 'available' }], partial: false });
    await flushPromises(); expect(wrapper.text()).not.toContain('sensitive-source'); wrapper.unmount();
  });
});
