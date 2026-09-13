import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ElMessage } from 'element-plus';
import type { AiChatSession } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import AiChatView from './AiChatView.vue';
import { getAiChatSession, listAiChatSessions, streamAiChatMessage } from '../api/ai-chat';
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
  afterEach(() => { vi.restoreAllMocks(); });
  beforeEach(() => {
    listSessionsMock.mockResolvedValue({ items: [], page: 1, pageSize: 50, total: 0 });
    listModelsMock.mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0 });
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="ai-chat-create"]').exists()).toBe(false);
  });

  it.each([403, 409, 422])('发送被 HTTP %s 拒绝时展示服务端错误并恢复输入', async status => {
    const session: AiChatSession = {
      id: 'session', tenantId: null, ownerUserId: 'owner', modelConfigId: 'model', modelName: '模型',
      title: '会话', isGenerating: false, messages: [], createdAtUtc: '2026-09-08T00:00:00Z', updatedAtUtc: null, version: 1
    };
    listSessionsMock.mockResolvedValue({ items: [{ ...session, messageCount: 0, lastMessageAtUtc: null }], page: 1, pageSize: 50, total: 1 });
    vi.mocked(getAiChatSession).mockResolvedValue(session);
    vi.mocked(streamAiChatMessage).mockRejectedValue({ status, code: 'ai.chat.rejected', title: '服务端拒绝原因', type: 'about:blank' });
    const notify = vi.spyOn(ElMessage, 'error');
    const wrapper = mountView();
    vi.spyOn(useSessionStore(), 'can').mockReturnValue(true);
    await flushPromises();
    await wrapper.get('textarea').setValue('hello');
    await wrapper.get('.ai-chat-composer button').trigger('click');
    await flushPromises();
    expect(notify).toHaveBeenCalledWith('服务端拒绝原因');
    await wrapper.get('textarea').setValue('retry');
    expect(wrapper.get('.ai-chat-composer button').attributes('disabled')).toBeUndefined();
    wrapper.unmount();
  });
});
