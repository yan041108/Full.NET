import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import type { ElasticsearchLogPipelineHealth } from '@fullnet/client-contracts';
import ObservabilityElasticsearchHealthView from './ObservabilityElasticsearchHealthView.vue';
import { getElasticsearchLogPipelineHealth } from '../api/observability-elasticsearch-health';

vi.mock('../auth/session', () => ({
  useSessionStore: () => ({ can: () => true })
}));
vi.mock('../api/observability-elasticsearch-health', () => ({
  getElasticsearchLogPipelineHealth: vi.fn()
}));

const getHealth = vi.mocked(getElasticsearchLogPipelineHealth);

function health(deliveryStatus: ElasticsearchLogPipelineHealth['deliveryStatus']): ElasticsearchLogPipelineHealth {
  return {
  deliveryStatus,
  deliveryConfirmationBoundary: 'configuration-only',
  elasticsearchQueryEnabled: false,
  elasticsearchEndpoint: null,
  elasticsearchIndexPattern: null,
  openTelemetryOtlpEndpointConfigured: false
  };
}

describe('日志管道配置状态', () => {
  beforeEach(() => { getHealth.mockReset(); });

  it.each(['external-collector', 'application-kafka', 'local'] as const)(
    '%s 模式不把旧版 Sink 未注册显示成故障', async deliveryStatus => {
      getHealth.mockResolvedValue(health(deliveryStatus));
      const wrapper = mount(ObservabilityElasticsearchHealthView, {
        global: { plugins: [createPinia()] }
      });
      await flushPromises();
      expect(wrapper.text()).toContain('仅配置，未确认远端投递');
      expect(wrapper.text()).not.toContain('Sink 已注册');
      expect(wrapper.text()).not.toContain('索引格式');
      wrapper.unmount();
    }
  );

  it('ES 查询配置单独展示，不声称写入或交付成功', async () => {
    getHealth.mockResolvedValue({ ...health('application-kafka'), elasticsearchQueryEnabled: true,
      elasticsearchEndpoint: 'https://search.example.test:9243', elasticsearchIndexPattern: 'fn-logs-*' });
    const wrapper = mount(ObservabilityElasticsearchHealthView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    expect(wrapper.text()).toContain('https://search.example.test:9243');
    expect(wrapper.text()).toContain('仅配置，未确认远端投递');
    expect(wrapper.text()).not.toContain('Sink 已注册');
    wrapper.unmount();
  });

  it('读取失败显示错误，不伪造健康状态', async () => {
    getHealth.mockResolvedValueOnce(health('external-collector'));
    const wrapper = mount(ObservabilityElasticsearchHealthView, { global: { plugins: [createPinia()] } });
    await flushPromises();
    expect(wrapper.text()).toContain('仅配置，未确认远端投递');
    getHealth.mockRejectedValue({ type: 'about:blank', title: '读取失败', status: 503, code: 'observability.unavailable' });
    await wrapper.find('button').trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('读取失败');
    expect(wrapper.text()).not.toContain('仅配置，未确认远端投递');
    wrapper.unmount();
  });
});
