import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ReportingExportTasksView from './ReportingExportTasksView.vue';
import { useSessionStore } from '../auth/session';
import { listReportingDefinitions } from '../api/reporting-definitions';
import { listReportingExportTasks } from '../api/reporting-export-tasks';

vi.mock('../api/reporting-definitions', () => ({
  listReportingDefinitions: vi.fn()
}));

vi.mock('../api/reporting-export-tasks', () => ({
  listReportingExportTasks: vi.fn(),
  createReportingExportTask: vi.fn(),
  downloadReportingExportTask: vi.fn()
}));

const definitionsMock = vi.mocked(listReportingDefinitions);
const tasksMock = vi.mocked(listReportingExportTasks);

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    actorScope: 'tenant',
    scope: 'tenant',
    isSuperAdministrator: false,
    passwordChangeRequired: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf298',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(ReportingExportTasksView, { global: { plugins: [pinia] } });
}

describe('ReportingExportTasksView', () => {
  beforeEach(() => {
    definitionsMock.mockResolvedValue([]);
    tasksMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  });

  it('shows create action when create permission is granted', async () => {
    const wrapper = mountWithPermissions([
      'reporting.export_tasks.read',
      'reporting.export_tasks.create'
    ]);
    await flushPromises();
    expect(wrapper.find('[data-testid="reporting-export-create"]').exists()).toBe(true);
  });
});
