import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import AiAgentRunsView from './AiAgentRunsView.vue';
import { getAiAgentRun, streamAiAgentRunEvents } from '../api/ai-agent-runs';
import { listAiModelConfigs } from '../api/ai-model-configs';

vi.mock('../api/ai-agent-runs', () => ({
  createAiAgentRun: vi.fn(),
  getAiAgentRun: vi.fn(),
  cancelAiAgentRun: vi.fn(),
  resumeAiAgentRun: vi.fn(),
  streamAiAgentRunEvents: vi.fn(),
  parseAgUiStateSnapshot: vi.fn()
}));

vi.mock('../api/ai-model-configs', () => ({
  listAiModelConfigs: vi.fn()
}));

const getRunMock = vi.mocked(getAiAgentRun);
const streamMock = vi.mocked(streamAiAgentRunEvents);
const listModelsMock = vi.mocked(listAiModelConfigs);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(AiAgentRunsView, { global: { plugins: [pinia] } });
}

describe('AiAgentRunsView', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  beforeEach(() => {
    listModelsMock.mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0 });
    streamMock.mockResolvedValue(undefined);
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await flushPromises();
    expect(wrapper.find('[data-testid="ai-agent-runs-create"]').exists()).toBe(false);
  });

  it('loads run and renders status after stream completes', async () => {
    getRunMock.mockResolvedValue({
      id: '11111111-1111-1111-1111-111111111111',
      statusKey: 'completed',
      definitionKey: 'fullnet-single-text-v1',
      definitionVersion: 1,
      deadlineAtUtc: '2026-09-08T01:00:00Z',
      createdAtUtc: '2026-09-08T00:00:00Z',
      updatedAtUtc: '2026-09-08T00:05:00Z'
    });
    streamMock.mockImplementation(async (_runId, handlers) => {
      handlers.onEvent({
        eventType: 'RUN_STARTED',
        payload: { type: 'RUN_STARTED', threadId: 'thread', runId: 'run' }
      });
    });

    const wrapper = mountView();
    await wrapper.get('[data-testid="ai-agent-runs-id"]').setValue('11111111-1111-1111-1111-111111111111');
    await wrapper.get('[data-testid="ai-agent-runs-load"]').trigger('click');
    await flushPromises();

    expect(getRunMock).toHaveBeenCalled();
    expect(streamMock).toHaveBeenCalled();
    expect(wrapper.text()).toContain('completed');
  });
});
