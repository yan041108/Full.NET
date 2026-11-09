import { afterEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
vi.mock('../api/platform-dashboard', () => ({
  getHostDashboardSummary: vi.fn().mockResolvedValue({
    activeTenantCount: 3,
    onlineSessionCount: 2,
    todayRequestCount: 10,
    todayErrorRate: 0,
    recentActivities: [],
    accessTrafficTrend: {
      fromUtc: '2026-09-23T00:00:00Z',
      toUtc: '2026-09-23T12:00:00Z',
      bucketSizeMinutes: 60,
      buckets: [{ bucketStartUtc: '2026-09-23T11:00:00Z', eventCount: 4, errorCount: 0 }]
    },
    businessEntries: [
      {
        entryKey: 'workflow.pending_todos',
        count: 2,
        routePath: '/workflow/todos',
        requiredPermission: 'workflow.todos.read'
      }
    ]
  })
}));

import OverviewView from './OverviewView.vue';
import { getHostDashboardSummary } from '../api/platform-dashboard';

const mountedWrappers: ReturnType<typeof mount>[] = [];

function mountOverview() {
  const wrapper = mount(OverviewView);
  mountedWrappers.push(wrapper);
  return wrapper;
}

afterEach(() => {
  mountedWrappers.splice(0).forEach(wrapper => wrapper.unmount());
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});

describe('Vue 管理端概览页', () => {
  it('呈现真实运营指标与最近活动，移除固定健康度', async () => {
    const wrapper = mountOverview();

    await flushPromises();
    expect(wrapper.get('[data-testid="metric-grid"]').text()).toContain('活跃租户');
    expect(wrapper.get('[data-testid="open-api-docs"]').attributes('href')).toBe(
      import.meta.env.VITE_API_PROXY_TARGET
        ? `${String(import.meta.env.VITE_API_PROXY_TARGET).replace(/\/$/, '')}/scalar/v1`
        : '/scalar/v1'
    );
    expect(wrapper.text()).not.toContain('98.6');
    expect(wrapper.text()).not.toContain('实时运行中');
    expect(wrapper.text()).toContain('待办事项');
    expect(wrapper.text()).toContain('最近活动');
    expect(wrapper.text()).toContain('运行态势');
    expect(wrapper.text()).toContain('工作流待办');
  });

  it('区分访问统计不可用与已采集的空窗口', async () => {
    vi.stubEnv('VITE_ENABLE_API_DOCUMENTATION', '0');
    const summary = {
      activeTenantCount: 3, onlineSessionCount: null, todayRequestCount: null, todayErrorRate: null,
      recentActivities: [], accessTrafficTrend: null, businessEntries: []
    };
    vi.mocked(getHostDashboardSummary)
      .mockResolvedValueOnce(summary)
      .mockResolvedValueOnce({
        ...summary,
        todayRequestCount: 0,
        accessTrafficTrend: {
          fromUtc: '2026-09-23T00:00:00Z',
          toUtc: '2026-09-23T12:00:00Z',
          bucketSizeMinutes: 60,
          buckets: []
        }
      });
    const unavailable = mountOverview();
    const empty = mountOverview();
    await flushPromises();

    expect(unavailable.text()).toContain('访问流量统计暂不可用');
    expect(unavailable.find('[data-testid="open-api-docs"]').exists()).toBe(false);
    expect(unavailable.text()).not.toContain('今日请求');
    expect(empty.text()).toContain('暂无可视化数据');
    expect(empty.text()).toContain('今日请求');
  });

  it('接口失败时展示稳定错误码和 TraceId', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: 403,
      code: 'authorization.denied',
      traceId: 'trace-parity'
    }), {
      status: 403,
      headers: { 'content-type': 'application/problem+json' }
    })));

    const wrapper = mountOverview();

    await flushPromises();
    await wrapper.get('[data-testid="load-current-user"]').trigger('click');
    await flushPromises();

    expect(wrapper.get('[data-testid="error-code"]').text())
      .toBe('authorization.denied');
    expect(wrapper.get('[data-testid="trace-id"]').text())
      .toBe('trace-parity');
  });

  it('会话检查进行中时拒绝重复提交', async () => {
    let resolveRequest!: (response: Response) => void;
    const pendingResponse = new Promise<Response>((resolve) => {
      resolveRequest = resolve;
    });
    const fetchMock = vi.fn().mockReturnValue(pendingResponse);
    vi.stubGlobal('fetch', fetchMock);
    const wrapper = mountOverview();
    await flushPromises();
    const button = wrapper.get('[data-testid="load-current-user"]');

    const firstClick = button.trigger('click');
    const secondClick = button.trigger('click');
    await Promise.all([firstClick, secondClick]);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    resolveRequest(new Response(null, { status: 204 }));
    await flushPromises();
  });
});
