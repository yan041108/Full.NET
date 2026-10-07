import { mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ReportingExecuteView from './ReportingExecuteView.vue';
import { listReportingPublishedDefinitions } from '../api/reporting-definitions';
import { createOutputSession } from '../test/data-output-fixtures';

vi.mock('../api/reporting-definitions', () => ({
  listReportingPublishedDefinitions: vi.fn()
}));

vi.mock('../api/reporting-executions', () => ({
  executeReportingDefinition: vi.fn()
}));

const definitionsMock = vi.mocked(listReportingPublishedDefinitions);

function mountView() {
  const { pinia } = createOutputSession(['reporting.definitions.read', 'reporting.executions.run']);
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
