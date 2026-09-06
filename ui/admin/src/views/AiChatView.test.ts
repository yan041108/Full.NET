import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import AiChatView from './AiChatView.vue';
import { listAiChatSessions } from '../api/ai-chat';
import { listAiModelConfigs } from '../api/ai-model-configs';

vi.mock('../api/ai-chat', () => ({
  listAiChatSessions: vi.fn(),
  getAiChatSession: vi.fn(),
  createAiChatSession: vi.fn(),
  updateAiChatSession: vi.fn(),
  deleteAiChatSession: vi.fn(),
  cancelAiChatGeneration: vi.fn(),
  streamAiChatMessage: vi.fn()
}));

vi.mock('../api/ai-model-configs', () => ({
  listAiModelConfigs: vi.fn()
}));

const listSessionsMock = vi.mocked(listAiChatSessions);
const listModelsMock = vi.mocked(listAiModelConfigs);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(AiChatView, { global: { plugins: [pinia] } });
}

describe('AiChatView', () => {
  beforeEach(() => {
    listSessionsMock.mockResolvedValue({ items: [], page: 1, pageSize: 50, total: 0 });
    listModelsMock.mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0 });
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="ai-chat-create"]').exists()).toBe(false);
  });
});
