import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ReportingDefinitionsView from './ReportingDefinitionsView.vue';
import {
  listReportingDefinitions,
  listReportingGroups,
  listReportingQueryPorts
} from '../api/reporting-definitions';
import { listReportingDataSources } from '../api/reporting-data-sources';

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

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
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
  });
});
