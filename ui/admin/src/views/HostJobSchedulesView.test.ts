import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import HostJobSchedulesView from './HostJobSchedulesView.vue';
import { useSessionStore } from '../auth/session';
import { listHostJobDefinitions } from '../api/host-jobs';
import { listHostJobSchedules } from '../api/host-job-schedules';

vi.mock('../api/host-jobs', () => ({
  listHostJobDefinitions: vi.fn()
}));

vi.mock('../api/host-job-schedules', () => ({
  createHostJobSchedule: vi.fn(),
  listHostJobSchedules: vi.fn(),
  pauseHostJobSchedule: vi.fn(),
  resumeHostJobSchedule: vi.fn(),
  updateHostJobSchedule: vi.fn()
}));

const definitionsMock = vi.mocked(listHostJobDefinitions);
const schedulesMock = vi.mocked(listHostJobSchedules);

const definition = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  jobKey: 'jobs.ping',
  displayName: 'enabled-job',
  description: 'desc',
  isEnabled: true,
  createdAtUtc: '2026-07-26T00:00:00Z',
  updatedAtUtc: '2026-07-26T00:00:00Z',
  version: 1
};

const enabledSchedule = {
  id: '01912345-6789-7abc-8def-0123456789ac',
  jobDefinitionId: definition.id,
  triggerKind: 'cron',
  cronExpression: '0 9 * * *',
  timeZoneId: 'UTC',
  oneTimeAtUtc: null,
  misfirePolicy: 'skip',
  isEnabled: true,
  nextExecutionAtUtc: '2026-08-03T09:00:00Z',
  lastExecutionAtUtc: null,
  completedAtUtc: null,
  createdAtUtc: '2026-07-26T00:00:00Z',
  updatedAtUtc: null,
  version: 1
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
  return mount(HostJobSchedulesView, { global: { plugins: [pinia] } });
}

describe('Vue Host 任务计划页', () => {
  beforeEach(() => {
    definitionsMock.mockReset().mockResolvedValue({
      items: [definition],
      page: 1,
      pageSize: 20,
      total: 1
    });
    schedulesMock.mockReset().mockResolvedValue({
      items: [enabledSchedule],
      page: 1,
      pageSize: 20,
      total: 1
    });
  });

  it('仅有 read 时不显示创建表单与行内操作', async () => {
    definitionsMock.mockRejectedValue({
      status: 403,
      code: 'authorization.permission_denied',
      title: 'Forbidden'
    });
    const wrapper = mountWithPermissions(['jobs.schedules.read']);
    await flushPromises();

    expect(definitionsMock).not.toHaveBeenCalled();
    expect(schedulesMock).toHaveBeenCalledOnce();
    expect(wrapper.text()).toContain(enabledSchedule.jobDefinitionId);
    expect(wrapper.find('[data-testid="host-job-schedules-submit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-job-schedules-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-job-schedules-pause"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-job-schedules-resume"]').exists()).toBe(false);
  });

  it('create 与 definitions.read 同时具备时显示创建表单', async () => {
    const wrapper = mountWithPermissions([
      'jobs.schedules.read',
      'jobs.schedules.create',
      'jobs.definitions.read'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-job-schedules-submit"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-job-schedules-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-job-schedules-pause"]').exists()).toBe(false);
  });

  it('缺少 definition 目录读取权限时不显示不可用的创建表单', async () => {
    const wrapper = mountWithPermissions([
      'jobs.schedules.read',
      'jobs.schedules.create'
    ]);
    await flushPromises();

    expect(definitionsMock).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="host-job-schedules-submit"]').exists()).toBe(false);
  });

  it('update-only 只显示编辑按钮', async () => {
    const wrapper = mountWithPermissions([
      'jobs.schedules.read',
      'jobs.schedules.update'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-job-schedules-submit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-job-schedules-edit"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-job-schedules-pause"]').exists()).toBe(false);
  });

  it('pause-only 只显示暂停按钮', async () => {
    const wrapper = mountWithPermissions([
      'jobs.schedules.read',
      'jobs.schedules.pause'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-job-schedules-submit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-job-schedules-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-job-schedules-pause"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-job-schedules-resume"]').exists()).toBe(false);
  });

  it('resume-only 在暂停计划上不显示恢复（当前列表项为启用）', async () => {
    const wrapper = mountWithPermissions([
      'jobs.schedules.read',
      'jobs.schedules.resume'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-job-schedules-resume"]').exists()).toBe(false);
  });

  it('列表加载失败时向用户显示稳定错误码', async () => {
    schedulesMock.mockRejectedValue({
      type: 'about:blank',
      status: 503,
      code: 'jobs.schedules.unavailable',
      title: 'Schedules unavailable'
    });

    const wrapper = mountWithPermissions(['jobs.schedules.read']);
    await flushPromises();

    expect(wrapper.get('[role="alert"]').text()).toContain('jobs.schedules.unavailable');
  });
});
