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
    adapterKind: 'serilog-elasticsearch',
    isEnabled: false,
    isSinkRegistered: false,
    indexFormat: 'logs-{0:yyyy.MM.dd}',
    nodeEndpoints: [],
    openTelemetryOtlpEndpointConfigured: false,
    pipelineNotice: 'Legacy sink disabled',
    clusterStatus: 'disabled',
    clusterName: null,
    numberOfNodes: null,
    probeErrorMessage: null,
    deliveryStatus,
    deliveryConfirmationBoundary: 'configuration-only'
  };
}

describe('日志管道配置状态', () => {
  beforeEach(() => getHealth.mockReset());

  it.each(['external-collector', 'application-kafka', 'disabled'] as const)(
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

  it('兼容直写仍展示旧版 Sink 与集群诊断', async () => {
    getHealth.mockResolvedValue({ ...health('legacy-direct'), isEnabled: true });
    const wrapper = mount(ObservabilityElasticsearchHealthView, {
      global: { plugins: [createPinia()] }
    });
    await flushPromises();
    expect(wrapper.text()).toContain('Sink 已注册');
    expect(wrapper.text()).toContain('索引格式');
    wrapper.unmount();
  });

  it('旧版服务未提供入口状态时仍保留既有诊断', async () => {
    getHealth.mockResolvedValue({
      ...health(undefined),
      deliveryStatus: undefined,
      deliveryConfirmationBoundary: undefined
    });
    const wrapper = mount(ObservabilityElasticsearchHealthView, {
      global: { plugins: [createPinia()] }
    });
    await flushPromises();
    expect(wrapper.text()).toContain('旧版服务未提供');
    expect(wrapper.text()).toContain('Sink 已注册');
    wrapper.unmount();
  });
});
