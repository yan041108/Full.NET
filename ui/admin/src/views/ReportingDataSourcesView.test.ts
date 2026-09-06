import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ReportingDataSourcesView from './ReportingDataSourcesView.vue';
import { listReportingDataSources } from '../api/reporting-data-sources';

vi.mock('../api/reporting-data-sources', () => ({
  listReportingDataSources: vi.fn(),
  getReportingDataSource: vi.fn(),
  createReportingDataSource: vi.fn(),
  updateReportingDataSource: vi.fn(),
  disableReportingDataSource: vi.fn(),
  deleteReportingDataSource: vi.fn(),
  testReportingDataSource: vi.fn()
}));

const listMock = vi.mocked(listReportingDataSources);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(ReportingDataSourcesView, { global: { plugins: [pinia] } });
}

describe('ReportingDataSourcesView', () => {
  beforeEach(() => {
    listMock.mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      total: 0
    });
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="reporting-data-source-create"]').exists()).toBe(false);
  });
});
