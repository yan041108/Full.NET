import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ObservabilityServerMonitorView from './ObservabilityServerMonitorView.vue';
import { useSessionStore } from '../auth/session';
import {
  getObservabilityServerRuntime,
  listObservabilityServerInstances
} from '../api/observability-server-monitor';

vi.mock('../api/observability-server-monitor', () => ({
  getObservabilityServerRuntime: vi.fn(),
  listObservabilityServerInstances: vi.fn()
}));

const listMock = vi.mocked(listObservabilityServerInstances);
const runtimeMock = vi.mocked(getObservabilityServerRuntime);

function mountView(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    passwordChangeRequired: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(ObservabilityServerMonitorView, { global: { plugins: [pinia] } });
}

describe('服务器监控页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue([
      {
        instanceKey: 'api-primary',
        displayName: 'API 主实例',
        hostRole: 'Api',
        isCurrent: true,
        runtimeQueryability: 'local'
      },
      {
        instanceKey: 'worker-1',
        displayName: 'Worker 1',
        hostRole: 'Worker',
        isCurrent: false,
        runtimeQueryability: 'catalog_only'
      }
    ]);
    runtimeMock.mockReset().mockResolvedValue({
      instanceKey: 'api-primary',
      displayName: 'API 主实例',
      hostRole: 'Api',
      machineName: 'HOST',
      processId: 1,
      frameworkDescription: '.NET 10.0',
      applicationVersion: '1.0.0',
      operatingSystemDescription: 'Windows',
      processArchitecture: 'X64',
      processStartedAtUtc: '2026-09-06T00:00:00Z',
      capturedAtUtc: '2026-09-06T01:00:00Z',
      uptimeSeconds: 3600,
      metrics: [
        {
          key: 'cpu_usage_percent',
          label: 'CPU 使用率',
          longValue: null,
          doubleValue: 12.5,
          unit: 'percent',
          availability: 'available',
          unavailableReason: null
        }
      ]
    });
  });

  it('读取权限会加载实例目录并自动查询当前实例运行时', async () => {
    const wrapper = mountView(['observability.server.read']);
    await flushPromises();

    expect(listMock).toHaveBeenCalledTimes(1);
    expect(runtimeMock).toHaveBeenCalledWith('api-primary');
    expect(wrapper.text()).toContain('API 主实例');
    expect(wrapper.text()).toContain('12.50%');
  });
});
