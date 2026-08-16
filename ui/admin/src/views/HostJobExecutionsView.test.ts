import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import HostJobExecutionsView from './HostJobExecutionsView.vue';
import { useSessionStore } from '../auth/session';
import { getHostJobExecution, listHostJobDefinitions, listHostJobExecutions } from '../api/host-jobs';

vi.mock('../api/host-jobs', () => ({
  getHostJobExecution: vi.fn(),
  listHostJobDefinitions: vi.fn(),
  listHostJobExecutions: vi.fn()
}));

const listExecutionsMock = vi.mocked(listHostJobExecutions);
const listDefinitionsMock = vi.mocked(listHostJobDefinitions);

const execution = {
  id: '01912345-6789-7abc-8def-0123456789ac',
  jobDefinitionId: '01912345-6789-7abc-8def-0123456789ab',
  jobScheduleId: null,
  status: 'succeeded' as const,
  triggerKind: 'manual',
  scheduledForUtc: null,
  errorMessage: null,
  startedAtUtc: '2026-07-26T00:00:01Z',
  finishedAtUtc: '2026-07-26T00:00:02Z',
  nextAttemptAtUtc: null,
  attemptCount: 1,
  createdAtUtc: '2026-07-26T00:00:00Z'
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
  return mount(HostJobExecutionsView, { global: { plugins: [pinia] } });
}

describe('HostJobExecutionsView', () => {
  beforeEach(() => {
    listDefinitionsMock.mockReset().mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 100,
      total: 0
    });
    listExecutionsMock.mockReset().mockResolvedValue({
      items: [execution],
      page: 1,
      pageSize: 20,
      total: 1
    });
  });

  it('无 read 权限时不加载执行记录', async () => {
    mountWithPermissions(['jobs.definitions.read']);
    await flushPromises();
    expect(listExecutionsMock).not.toHaveBeenCalled();
  });

  it('有 read 权限时加载列表', async () => {
    mountWithPermissions(['jobs.executions.read']);
    await flushPromises();
    expect(listExecutionsMock).toHaveBeenCalled();
  });
});
