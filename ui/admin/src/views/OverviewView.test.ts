import { afterEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import OverviewView from './OverviewView.vue';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Vue 管理端概览页', () => {
  it('呈现运营指标、系统脉搏和最近活动分区', () => {
    const wrapper = mount(OverviewView);

    expect(wrapper.get('[data-testid="metric-grid"]').text()).toContain('活跃租户');
    expect(wrapper.text()).toContain('系统脉搏');
    expect(wrapper.text()).toContain('待办事项');
    expect(wrapper.text()).toContain('最近活动');
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

    const wrapper = mount(OverviewView);

    await wrapper.get('[data-testid="load-current-user"]').trigger('click');
    await flushPromises();

    expect(wrapper.get('[data-testid="error-code"]').text())
      .toBe('authorization.denied');
    expect(wrapper.get('[data-testid="trace-id"]').text())
      .toBe('trace-parity');
  });
});
