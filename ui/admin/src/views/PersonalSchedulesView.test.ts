import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import PersonalSchedulesView from './PersonalSchedulesView.vue';
import { useSessionStore } from '../auth/session';
import { listPersonalSchedules } from '../api/personal-schedules';

vi.mock('../api/personal-schedules', () => ({
  createPersonalSchedule: vi.fn(),
  deletePersonalSchedule: vi.fn(),
  listPersonalSchedules: vi.fn(),
  setPersonalScheduleStatus: vi.fn(),
  updatePersonalSchedule: vi.fn()
}));

const listMock = vi.mocked(listPersonalSchedules);

const pendingSchedule = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  content: '晨会',
  startAtUtc: '2026-09-06T01:00:00.000Z',
  endAtUtc: '2026-09-06T02:00:00.000Z',
  status: 'pending' as const,
  completedAtUtc: null,
  createdAtUtc: '2026-09-05T00:00:00.000Z',
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
    profileVersion: 1,
    passwordChangeRequired: false
  };
  return mount(PersonalSchedulesView, { global: { plugins: [pinia] } });
}

describe('Vue 个人日程页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [pendingSchedule],
      page: 1,
      pageSize: 500,
      total: 1
    });
  });

  it('仅有 read 时不显示创建与行内操作', async () => {
    const wrapper = mountWithPermissions(['calendar.personal_schedules.read']);
    await flushPromises();

    expect(listMock).toHaveBeenCalledOnce();
    expect(wrapper.find('[data-testid="personal-schedules-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="personal-schedules-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="personal-schedules-toggle-status"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="personal-schedules-delete"]').exists()).toBe(false);
  });

  it('create 权限显示创建入口', async () => {
    const wrapper = mountWithPermissions([
      'calendar.personal_schedules.read',
      'calendar.personal_schedules.create'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="personal-schedules-create"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="personal-schedules-edit"]').exists()).toBe(false);
  });
});
