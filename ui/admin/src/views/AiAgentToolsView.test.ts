import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import AiAgentToolsView from './AiAgentToolsView.vue';
import { listAiAgentTools } from '../api/ai-agent-tools';

vi.mock('../api/ai-agent-tools', () => ({
  listAiAgentTools: vi.fn(),
  listAiAgentToolCalls: vi.fn()
}));

const listCatalogMock = vi.mocked(listAiAgentTools);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(AiAgentToolsView, { global: { plugins: [pinia] } });
}

describe('AiAgentToolsView', () => {
  beforeEach(() => {
    listCatalogMock.mockResolvedValue([]);
  });

  it('loads static catalog on mount', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    await Promise.resolve();
    expect(listCatalogMock).toHaveBeenCalled();
  });
});
