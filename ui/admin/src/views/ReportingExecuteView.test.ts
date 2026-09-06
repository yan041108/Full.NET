import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ReportingExecuteView from './ReportingExecuteView.vue';
import { listReportingDefinitions } from '../api/reporting-definitions';

vi.mock('../api/reporting-definitions', () => ({
  listReportingDefinitions: vi.fn()
}));

vi.mock('../api/reporting-executions', () => ({
  executeReportingDefinition: vi.fn()
}));

const definitionsMock = vi.mocked(listReportingDefinitions);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(ReportingExecuteView, { global: { plugins: [pinia] } });
}

describe('ReportingExecuteView', () => {
  beforeEach(() => {
    definitionsMock.mockResolvedValue([]);
  });

  it('disables run action without definitions', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    const button = wrapper.find('[data-testid="reporting-execute-run"]');
    expect(button.attributes('disabled')).toBeDefined();
  });
});
