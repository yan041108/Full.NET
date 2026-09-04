import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import HostJobHealthView from './HostJobHealthView.vue';
import { useSessionStore } from '../auth/session';
import { getHostJobHealth } from '../api/host-job-health';

vi.mock('../api/host-job-health', () => ({
  getHostJobHealth: vi.fn()
}));

const healthMock = vi.mocked(getHostJobHealth);

const health = {
  registeredHandlers: ['ping', 'http'],
  backlog: {
    pendingCount: 2,
    oldestClaimableCreatedAtUtc: '2026-07-26T00:00:00Z',
    dueRetryCount: 0,
    oldestDueRetryAtUtc: null
  },
  workers: [{
    instanceId: '01912345-6789-7abc-8def-0123456789ab',
    hostProfile: 'api',
    startedAtUtc: '2026-07-26T00:00:00Z',
    lastHeartbeatAtUtc: '2026-07-26T00:01:00Z',
    workerVersion: '1.0.0',
    isStale: false
  }]
};

function mountWithPermissions(permissions: string[]) {
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
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(HostJobHealthView, { global: { plugins: [pinia] } });
}

describe('HostJobHealthView', () => {
  beforeEach(() => {
    healthMock.mockReset().mockResolvedValue(health);
  });

  it('无 read 权限时不加载健康状态', async () => {
    mountWithPermissions(['jobs.executions.read']);
    await flushPromises();
    expect(healthMock).not.toHaveBeenCalled();
  });

  it('有 read 权限时展示积压、Handler 与 Worker 心跳', async () => {
    const wrapper = mountWithPermissions(['jobs.health.read']);
    await flushPromises();
    expect(healthMock).toHaveBeenCalled();
    expect(wrapper.text()).toContain('ping');
    expect(wrapper.text()).toContain('api');
  });
});
