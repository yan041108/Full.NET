import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ReportingDataSourcesView from './ReportingDataSourcesView.vue';
import { listReportingDataSources } from '../api/reporting-data-sources';
import { useSessionStore } from '../auth/session';

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

function mountView(permissions: string[] = []) {
  const pinia = createPinia();
  setActivePinia(pinia);
  useSessionStore().currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296', username: 'reader', displayName: '查看者',
    tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297', preferredLocale: 'zh-CN', profileVersion: 1
  };
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
    wrapper.unmount();
  });
  it('opens the create dialog with the exact permission through the real header', async () => {
    const wrapper = mountView(['reporting.data_sources.create']);
    await flushPromises();
    await wrapper.get('[data-testid="reporting-data-source-create"]').trigger('click');
    expect(wrapper.getComponent({ name: 'ArtFormDialog' }).props('open')).toBe(true);
    wrapper.unmount();
  });
  it('renders a usable name filter and resets the applied query', async () => {
    const wrapper = mountView();
    await flushPromises();
    const search = wrapper.getComponent({ name: 'ArtSearchBar' });
    await search.get('input').setValue('  north  ');
    await search.findAll('button').find(button => button.text() === '查询')!.trigger('click');
    await flushPromises();
    expect(listMock).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, nameContains: 'north' });
    await search.findAll('button').find(button => button.text() === '重置')!.trigger('click');
    await flushPromises();
    expect(listMock).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, nameContains: undefined });
    wrapper.unmount();
  });
});
