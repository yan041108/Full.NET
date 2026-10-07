import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ReportingDefinitionsView from './ReportingDefinitionsView.vue';
import {
  listReportingDefinitions,
  listReportingGroups,
  listReportingQueryPorts
} from '../api/reporting-definitions';
import { listReportingDataSources } from '../api/reporting-data-sources';
import { useSessionStore } from '../auth/session';

vi.mock('../api/reporting-definitions', () => ({
  listReportingGroups: vi.fn(),
  createReportingGroup: vi.fn(),
  updateReportingGroup: vi.fn(),
  deleteReportingGroup: vi.fn(),
  listReportingQueryPorts: vi.fn(),
  listReportingDefinitions: vi.fn(),
  getReportingDefinition: vi.fn(),
  createReportingDefinition: vi.fn(),
  updateReportingDefinition: vi.fn(),
  deleteReportingDefinition: vi.fn(),
  publishReportingDefinition: vi.fn(),
  listReportingDefinitionVersions: vi.fn()
}));

vi.mock('../api/reporting-data-sources', () => ({
  listReportingDataSources: vi.fn()
}));

const groupsMock = vi.mocked(listReportingGroups);
const definitionsMock = vi.mocked(listReportingDefinitions);
const queryPortsMock = vi.mocked(listReportingQueryPorts);
const dataSourcesMock = vi.mocked(listReportingDataSources);

function mountView(permissions: string[] = []) {
  const pinia = createPinia();
  setActivePinia(pinia);
  useSessionStore().state = 'authenticated';
  useSessionStore().currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296', username: 'reader', displayName: '查看者',
    tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297', preferredLocale: 'zh-CN', profileVersion: 1
  };
  return mount(ReportingDefinitionsView, { global: { plugins: [pinia] } });
}

describe('ReportingDefinitionsView', () => {
  beforeEach(() => {
    groupsMock.mockResolvedValue([]);
    definitionsMock.mockResolvedValue([]);
    queryPortsMock.mockResolvedValue([]);
    dataSourcesMock.mockResolvedValue({ items: [], page: 1, pageSize: 200, total: 0 });
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="reporting-definition-create"]').exists()).toBe(false);
    wrapper.unmount();
  });
  it('opens the definition dialog with the exact permission through the real header', async () => {
    groupsMock.mockResolvedValueOnce([{ id: '019bc2b1-2a40-7cc3-8992-a80de51bf298',
      parentId: null, name: '报表组', sortOrder: 0, isEnabled: true,
      createdAtUtc: '2026-10-07T00:00:00Z', updatedAtUtc: null, version: 1 }]);
    const wrapper = mountView(['reporting.definitions.create']);
    await flushPromises();
    await wrapper.get('[data-testid="reporting-definition-create"]').trigger('click');
    expect(wrapper.findAllComponents({ name: 'ArtFormDialog' }).some(dialog => dialog.props('open'))).toBe(true);
    wrapper.unmount();
  });
});
