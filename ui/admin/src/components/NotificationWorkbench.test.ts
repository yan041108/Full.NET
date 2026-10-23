import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import NotificationWorkbench from './NotificationWorkbench.vue';
import { useSessionStore } from '../auth/session';
import { listMyHostAnnouncements } from '../api/my-host-announcements';
import { listMyWorkflowTodos } from '../api/workflow-todos';

vi.mock('../api/my-host-announcements', () => ({ listMyHostAnnouncements: vi.fn() }));
vi.mock('../api/workflow-todos', () => ({ listMyWorkflowTodos: vi.fn() }));

function mountWorkbench(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  useSessionStore().currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296', username: 'reader', displayName: 'Reader',
    tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions, sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN', profileVersion: 1
  };
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] });
  return mount(NotificationWorkbench, { global: { plugins: [pinia, router] } });
}

describe('通知工作台组合', () => {
  beforeEach(() => vi.resetAllMocks());

  it('没有来源权限时不读取公告或待办', async () => {
    const wrapper = mountWorkbench(['notifications.inbox.read']);
    await flushPromises();
    expect(listMyHostAnnouncements).not.toHaveBeenCalled();
    expect(listMyWorkflowTodos).not.toHaveBeenCalled();
    expect(wrapper.find('h2').exists()).toBe(false);
  });

  it('公告失败仍展示授权待办，并明确提示来源不可用', async () => {
    vi.mocked(listMyHostAnnouncements).mockRejectedValue(new Error('offline'));
    vi.mocked(listMyWorkflowTodos).mockResolvedValue({ items: [{ id: 'todo', businessTitle: '<script>alert(1)</script>', nodeKey: 'review' }], page: 1, pageSize: 5, total: 1 } as Awaited<ReturnType<typeof listMyWorkflowTodos>>);
    const wrapper = mountWorkbench(['notifications.announcements.received.read', 'workflow.todos.read']);
    await flushPromises();
    expect(wrapper.find('[role="status"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('<script>alert(1)</script>');
    expect(wrapper.find('script').exists()).toBe(false);
  });
});
