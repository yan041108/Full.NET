import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import HostJobExecutionsView from './HostJobExecutionsView.vue';
import { useSessionStore } from '../auth/session';
import { getHostJobExecution, listHostJobDefinitions, listHostJobExecutions } from '../api/host-jobs';
import { listHostJobSchedules } from '../api/host-job-schedules';

vi.mock('../api/host-jobs', () => ({
  getHostJobExecution: vi.fn(),
  listHostJobDefinitions: vi.fn(),
  listHostJobExecutions: vi.fn()
}));
vi.mock('../api/host-job-schedules', () => ({
  listHostJobSchedules: vi.fn()
}));

const listExecutionsMock = vi.mocked(listHostJobExecutions);
const listDefinitionsMock = vi.mocked(listHostJobDefinitions);
const listSchedulesMock = vi.mocked(listHostJobSchedules);
const getExecutionMock = vi.mocked(getHostJobExecution);

const schedule = {
  id: '01912345-6789-7abc-8def-0123456789ad',
  jobDefinitionId: '01912345-6789-7abc-8def-0123456789ab',
  jobDefinitionJobKey: 'jobs.ping',
  jobDefinitionDisplayName: 'Ping job',
  triggerKind: 'cron',
  cronExpression: '0 9 * * *',
  timeZoneId: 'UTC',
  oneTimeAtUtc: null,
  misfirePolicy: 'skip',
  isEnabled: true,
  nextExecutionAtUtc: '2026-08-03T09:00:00Z',
  lastExecutionAtUtc: null,
  completedAtUtc: null,
  numberOfRuns: 0,
  numberOfErrors: 0,
  startTime: null,
  endTime: null,
  args: null,
  createdAtUtc: '2026-07-26T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

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
      items: [{
        id: schedule.jobDefinitionId,
        jobKey: schedule.jobDefinitionJobKey,
        handlerKind: 'ping',
        args: null,
        displayName: schedule.jobDefinitionDisplayName,
        description: null,
        groupName: null,
        isEnabled: true,
        allowConcurrentExecutions: false,
        createdAtUtc: '2026-07-26T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 100,
      total: 1
    });
    listSchedulesMock.mockReset().mockResolvedValue({
      items: [schedule],
      page: 1,
      pageSize: 100,
      total: 1
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
    expect(listSchedulesMock).toHaveBeenCalled();
  });

  it('查询时把计划筛选参数发给服务端', async () => {
    const wrapper = mountWithPermissions(['jobs.executions.read']);
    await flushPromises();
    await wrapper
      .get('[data-testid="host-job-executions-filter-schedule"]')
      .findComponent({ name: 'ElSelect' })
      .setValue(schedule.id);
    await wrapper.get('[data-testid="host-job-executions-search"]').trigger('click');
    await flushPromises();
    expect(listExecutionsMock).toHaveBeenLastCalledWith(
      expect.objectContaining({
        jobScheduleId: schedule.id
      })
    );
  });

  it('详情抽屉展示耗时并隐藏非机器码错误原文', async () => {
    getExecutionMock.mockResolvedValue({
      ...execution,
      status: 'failed',
      errorMessage: 'System.InvalidOperationException: upstream timeout',
      startedAtUtc: '2026-07-26T00:00:01Z',
      finishedAtUtc: '2026-07-26T00:00:03Z'
    });
    const wrapper = mountWithPermissions(['jobs.executions.read']);
    await flushPromises();
    await wrapper.findComponent({ name: 'ElTable' }).vm.$emit('row-click', execution);
    await flushPromises();
    expect(getExecutionMock).toHaveBeenCalledWith(execution.id);
    const drawer = document.body.querySelector('.host-job-executions-detail');
    expect(drawer?.textContent).toContain('失败原因不可展示');
    expect(drawer?.textContent).not.toContain('upstream timeout');
    expect(drawer?.textContent).toMatch(/2(\.00)? 秒|2000 毫秒/);
    wrapper.unmount();
  });
});
