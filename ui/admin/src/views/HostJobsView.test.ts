import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import HostJobsView from './HostJobsView.vue';
import { useSessionStore } from '../auth/session';
import {
  createHostJobDefinition,
  disableHostJobDefinition,
  listHostJobDefinitions,
  listHostJobExecutions,
  triggerHostJobDefinition,
  updateHostJobDefinition
} from '../api/host-jobs';

vi.mock('../api/host-jobs', () => ({
  createHostJobDefinition: vi.fn(),
  disableHostJobDefinition: vi.fn(),
  listHostJobDefinitions: vi.fn(),
  listHostJobExecutions: vi.fn(),
  triggerHostJobDefinition: vi.fn(),
  updateHostJobDefinition: vi.fn()
}));

const listMock = vi.mocked(listHostJobDefinitions);

// 中文注释：HostJobDefinition.groupName 与 C# JobDefinition.GroupName 公共契约对齐，默认空字符串或 null
const enabledDefinition = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  jobKey: 'jobs.ping',
  displayName: 'enabled-job',
  description: 'desc',
  groupName: '',
  isEnabled: true,
  allowConcurrentExecutions: false,
  createdAtUtc: '2026-07-26T00:00:00Z',
  updatedAtUtc: '2026-07-26T00:00:00Z',
  version: 1
};

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '\u7ba1\u7406\u5458',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(HostJobsView, { global: { plugins: [pinia] } });
}

describe('Vue Host \u4efb\u52a1\u8c03\u5ea6\u9875', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [enabledDefinition],
      page: 1,
      pageSize: 20,
      total: 1
    });
  });

  it('\u4ec5\u6709 read \u65f6\u4e0d\u663e\u793a\u521b\u5efa\u8868\u5355\u4e0e\u884c\u5185\u64cd\u4f5c', async () => {
    const wrapper = mountWithPermissions(['jobs.definitions.read']);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-jobs-submit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-trigger"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-disable"]').exists()).toBe(false);
  });

  it('create-only \u53ea\u663e\u793a\u521b\u5efa\u8868\u5355', async () => {
    const wrapper = mountWithPermissions([
      'jobs.definitions.read',
      'jobs.definitions.create'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-jobs-submit"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-jobs-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-trigger"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-disable"]').exists()).toBe(false);
  });

  it('update-only \u53ea\u663e\u793a\u7f16\u8f91\u6309\u94ae', async () => {
    const wrapper = mountWithPermissions([
      'jobs.definitions.read',
      'jobs.definitions.update'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-jobs-submit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-edit"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-jobs-trigger"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-disable"]').exists()).toBe(false);
  });

  it('trigger-only \u53ea\u663e\u793a\u89e6\u53d1\u6309\u94ae', async () => {
    const wrapper = mountWithPermissions([
      'jobs.definitions.read',
      'jobs.definitions.trigger'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-jobs-submit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-trigger"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-jobs-disable"]').exists()).toBe(false);
  });

  it('disable-only \u53ea\u663e\u793a\u7981\u7528\u6309\u94ae', async () => {
    const wrapper = mountWithPermissions([
      'jobs.definitions.read',
      'jobs.definitions.disable'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-jobs-submit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-trigger"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-jobs-disable"]').exists()).toBe(true);
  });

  it('read-only \u65f6\u4e0d\u663e\u793a\u5141\u8bb8\u91cd\u53e0\u6267\u884c\u5f00\u5173', async () => {
    const wrapper = mountWithPermissions(['jobs.definitions.read']);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-jobs-allow-concurrent"]').exists()).toBe(false);
  });

  it('create \u8868\u5355\u9ed8\u8ba4\u5173\u95ed\u5141\u8bb8\u91cd\u53e0\u6267\u884c', async () => {
    const wrapper = mountWithPermissions([
      'jobs.definitions.read',
      'jobs.definitions.create'
    ]);
    await flushPromises();
    await wrapper.find('[data-testid="host-jobs-submit"]').trigger('click');
    await flushPromises();

    const toggle = wrapper.find('[data-testid="host-jobs-allow-concurrent"]');
    expect(toggle.exists()).toBe(true);
    expect((toggle.element as HTMLInputElement).checked).toBe(false);
  });
});