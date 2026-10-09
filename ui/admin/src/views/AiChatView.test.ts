import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { showProblem } from '../feedback/fullNetMessage';
import type { AiChatSession } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import AiChatView from './AiChatView.vue';
import { getAiChatSession, listAiChatSessions, streamAiChatMessage, askAiKnowledge } from '../api/ai-chat';
import AiKnowledgeBaseMultiSelect from '../components/AiKnowledgeBaseMultiSelect.vue';
import AiKnowledgeSourceDialog from '../components/AiKnowledgeSourceDialog.vue';
import { ElSwitch } from 'element-plus';
import { cancelAiChatGeneration } from '../api/ai-chat';
import { listAiModelConfigs } from '../api/ai-model-configs';

vi.mock('../api/ai-chat', () => ({
  listAiChatSessions: vi.fn(),
  getAiChatSession: vi.fn(),
  createAiChatSession: vi.fn(),
  updateAiChatSession: vi.fn(),
  deleteAiChatSession: vi.fn(),
  cancelAiChatGeneration: vi.fn(),
  streamAiChatMessage: vi.fn(),
  askAiKnowledge: vi.fn()
}));

vi.mock('../api/ai-model-configs', () => ({
  listAiModelConfigs: vi.fn()
}));

vi.mock('../api/ai-knowledge', () => ({ listKnowledgeBases: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0 }), downloadKnowledgeSource: vi.fn() }));

vi.mock('../feedback/fullNetMessage', () => ({ showError: vi.fn(), showProblem: vi.fn(), showWarning: vi.fn() }));
afterEach(() => vi.restoreAllMocks());

const listSessionsMock = vi.mocked(listAiChatSessions);
const listModelsMock = vi.mocked(listAiModelConfigs);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  vi.spyOn(useSessionStore(), 'can').mockImplementation(code => ['ai.chat.sessions.read', 'ai.models.read'].includes(code));
  return mount(AiChatView, { global: { plugins: [pinia], stubs: { RouterLink: true } } });
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
    const notify = vi.mocked(showProblem);
    const wrapper = mountView();
    vi.spyOn(useSessionStore(), 'can').mockReturnValue(true);
    await flushPromises();
    await wrapper.get('textarea').setValue('hello');
    await wrapper.get('.ai-chat-composer button').trigger('click');
    await flushPromises();
    expect(notify).toHaveBeenCalledWith(expect.objectContaining({ title: '服务端拒绝原因' }), expect.any(String));
    await wrapper.get('textarea').setValue('retry');
    expect(wrapper.get('.ai-chat-composer button').attributes('disabled')).toBeUndefined();
    wrapper.unmount();
  });
});

describe('知识库问答交互边界', () => {
  it('快捷键不能绕过发送权限', async () => {
    const session: AiChatSession = { id: 'session', tenantId: null, ownerUserId: 'owner', modelConfigId: 'model', modelName: '模型', title: '会话', isGenerating: false, messages: [], createdAtUtc: 'now', updatedAtUtc: null, version: 1 };
    listSessionsMock.mockResolvedValue({ items: [{ ...session, messageCount: 0, lastMessageAtUtc: null }], page: 1, pageSize: 50, total: 1 });
    vi.mocked(getAiChatSession).mockResolvedValue(session); vi.mocked(streamAiChatMessage).mockClear();
    const wrapper = mountView(); await flushPromises();
    await wrapper.get('textarea').setValue('question');
    await wrapper.get('textarea').trigger('keydown', { key: 'Enter', ctrlKey: true }); await flushPromises();
    expect(streamAiChatMessage).not.toHaveBeenCalled(); expect(askAiKnowledge).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('重载后空的已完成助手消息有明确无回答状态', async () => {
    const session: AiChatSession = { id: 'session', tenantId: null, ownerUserId: 'owner', modelConfigId: 'model', modelName: '模型', title: '会话', isGenerating: false, messages: [{ id: 'answer', sessionId: 'session', roleKey: 'assistant', statusKey: 'completed', content: '', citations: [], promptTokens: null, completionTokens: null, createdAtUtc: 'now' }], createdAtUtc: 'now', updatedAtUtc: null, version: 1 };
    listSessionsMock.mockResolvedValue({ items: [{ ...session, messageCount: 1, lastMessageAtUtc: null }], page: 1, pageSize: 50, total: 1 }); vi.mocked(getAiChatSession).mockResolvedValue(session);
    const wrapper = mountView(); await flushPromises(); expect(wrapper.find('[data-testid="knowledge-no-answer"]').exists()).toBe(true); wrapper.unmount();
  });
  it('来源不可用的历史回答隐藏内容和引用', async () => {
    const session: AiChatSession = { id: 'session', tenantId: null, ownerUserId: 'owner', modelConfigId: 'model', modelName: '模型', title: '会话', isGenerating: false, messages: [{ id: 'answer', sessionId: 'session', roleKey: 'assistant', statusKey: 'completed', content: 'stale private content', knowledgeUnavailableCode: 'ai.knowledge.answer_unavailable', citations: [], promptTokens: null, completionTokens: null, createdAtUtc: 'now' }], createdAtUtc: 'now', updatedAtUtc: null, version: 1 };
    listSessionsMock.mockResolvedValue({ items: [{ ...session, messageCount: 1, lastMessageAtUtc: null }], page: 1, pageSize: 50, total: 1 });
    vi.mocked(getAiChatSession).mockResolvedValue(session);
    const wrapper = mountView(); await flushPromises();
    expect(wrapper.text()).not.toContain('stale private content'); expect(wrapper.text()).toContain('此回答的来源已不可用'); wrapper.unmount();
  });
});

describe('公开 Ask、拒答与取消', () => {
  const baseId = '01900000-0000-7000-8000-000000000001';
  const sessionId = '01900000-0000-7000-8000-000000000002';
  const modelId = '01900000-0000-7000-8000-000000000003';
  const answerId = '01900000-0000-7000-8000-000000000004';
  const session: AiChatSession = { id: sessionId, tenantId: null, ownerUserId: modelId, modelConfigId: modelId, modelName: '生成模型', title: '会话', isGenerating: false, messages: [], createdAtUtc: 'now', updatedAtUtc: null, version: 1 };
  const answer = { id: answerId, sessionId, roleKey: 'assistant', content: '', statusKey: 'completed', citations: [], promptTokens: null, completionTokens: null, createdAtUtc: 'now' };
  beforeEach(() => {
    vi.clearAllMocks(); listSessionsMock.mockResolvedValue({ items: [{ ...session, messageCount: 0, lastMessageAtUtc: null }], page: 1, pageSize: 50, total: 1 });
    vi.mocked(getAiChatSession).mockResolvedValue(session);
    listModelsMock.mockResolvedValue({ items: [{ id: modelId, tenantId: null, name: 'Embedding', modelId: 'text-embedding-3-small', providerKey: 'openai-compatible', maskedEndpointBaseUrl: 'local', hasApiKey: true, isDefault: false, isEnabled: true, lastTestedAtUtc: null, lastTestStatusKey: null, lastTestMessage: null, createdAtUtc: 'now', updatedAtUtc: null, version: 1 }], page: 1, pageSize: 100, total: 1 });
  });
  async function ready() {
    const wrapper = mountView(); vi.spyOn(useSessionStore(), 'can').mockReturnValue(true); await flushPromises();
    wrapper.getComponent(ElSwitch).vm.$emit('update:modelValue', true); await flushPromises();
    wrapper.getComponent(AiKnowledgeBaseMultiSelect).vm.$emit('update:modelValue', [baseId]); await flushPromises();
    await wrapper.get('textarea').setValue('检索问题'); return wrapper;
  }
  it('知识问答发送固定资源 ID 并明确展示拒答', async () => {
    vi.mocked(askAiKnowledge).mockResolvedValue({ message: answer, refused: true }); vi.mocked(getAiChatSession).mockResolvedValueOnce(session).mockResolvedValue({ ...session, messages: [answer] });
    const wrapper = await ready(); await wrapper.get('.ai-chat-composer button').trigger('click'); await flushPromises();
    expect(askAiKnowledge).toHaveBeenCalledWith(sessionId, { knowledgeBaseIds: [baseId], embeddingModelConfigId: modelId, question: '检索问题' }, expect.any(AbortSignal));
    expect(streamAiChatMessage).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-refused"]').exists()).toBe(true); wrapper.unmount();
  });

  it('对话页提供精确授权的反馈审核入口', async () => {
    const pinia = createPinia(); setActivePinia(pinia);
    vi.spyOn(useSessionStore(), 'can').mockImplementation(code => ['ai.chat.sessions.read', 'ai.knowledge_answer_feedback.review_read'].includes(code));
    const wrapper = mount(AiChatView, { global: { plugins: [pinia], stubs: { RouterLink: true } } });
    try { await flushPromises(); expect(wrapper.find('[data-testid="feedback-review-open"]').exists()).toBe(true); }
    finally { wrapper.unmount(); }
  });
  it('多库 Ask 冻结请求集合，引用必须显式选择原请求范围', async () => {
    const second = '01900000-0000-7000-8000-000000000005'; const ids = [baseId, second];
    const citation = { id: 'citation', documentId: 'document', versionId: 'version', sequence: 1, pageNumber: null, offset: 0, length: 10 };
    const cited = { ...answer, content: '依据', citations: [citation] }; let finish!: (value: { message: typeof cited; refused: boolean }) => void;
    vi.mocked(askAiKnowledge).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const wrapper = await ready(); wrapper.getComponent(AiKnowledgeBaseMultiSelect).vm.$emit('update:modelValue', ids); await flushPromises();
    await wrapper.get('.ai-chat-composer button').trigger('click'); ids.push('01900000-0000-7000-8000-000000000006');
    expect(vi.mocked(askAiKnowledge).mock.calls[0]![1].knowledgeBaseIds).toEqual([baseId, second]);
    vi.mocked(getAiChatSession).mockResolvedValue({ ...session, messages: [cited] }); finish({ message: cited, refused: false }); await flushPromises();
    await wrapper.get('[data-testid="knowledge-citation"]').trigger('click'); await flushPromises();
    const source = wrapper.getComponent(AiKnowledgeSourceDialog); expect(source.props('knowledgeBaseId')).toBeUndefined(); expect(source.props('candidateBaseIds')).toEqual([baseId, second]); wrapper.unmount();
  });
  it.each([{ ids: [] }, { ids: [baseId, baseId] }, { ids: ['invalid'] }, { ids: Array.from({ length: 9 }, (_, i) => `01900000-0000-7000-8000-${String(i + 1).padStart(12, '0')}`) }])('空、重复、非法或超限知识库集合不能发送', async ({ ids }) => {
    const wrapper = await ready(); wrapper.getComponent(AiKnowledgeBaseMultiSelect).vm.$emit('update:modelValue', ids); await flushPromises(); await wrapper.get('textarea').trigger('keydown', { key: 'Enter', ctrlKey: true });
    expect(askAiKnowledge).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it.each([403, 409, 422])('Ask HTTP %s 失败恢复问题输入', async status => {
    vi.mocked(askAiKnowledge).mockRejectedValue({ status, code: 'ai.knowledge.answer_unavailable', title: '来源或索引不可用', type: 'about:blank' });
    const wrapper = await ready(); await wrapper.get('.ai-chat-composer button').trigger('click'); await flushPromises();
    expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('检索问题'); expect(wrapper.text()).not.toContain('依据不足'); expect(showProblem).toHaveBeenCalledWith(expect.objectContaining({ status }), expect.any(String)); wrapper.unmount();
  });
  it('请求期间阻止重复发送，先取消服务器任务再 Abort', async () => {
    let finish!: (value: { message: typeof answer; refused: boolean }) => void;
    vi.mocked(askAiKnowledge).mockImplementation(() => new Promise(resolve => { finish = resolve; })); vi.mocked(cancelAiChatGeneration).mockResolvedValue(true);
    const wrapper = await ready(); await wrapper.get('.ai-chat-composer button').trigger('click'); await wrapper.get('textarea').trigger('keydown', { key: 'Enter', ctrlKey: true });
    expect(askAiKnowledge).toHaveBeenCalledTimes(1); const signal = vi.mocked(askAiKnowledge).mock.calls[0]![2]!;
    expect(signal.aborted).toBe(false); await wrapper.get('[data-testid="ai-chat-stop"]').trigger('click'); await flushPromises(); expect(cancelAiChatGeneration).toHaveBeenCalledWith(sessionId); expect(signal.aborted).toBe(true);
    finish({ message: answer, refused: true }); await flushPromises(); expect(wrapper.find('[data-testid="knowledge-refused"]').exists()).toBe(false); wrapper.unmount();
  });
  it('服务器取消收尾延迟时刷新到槽位终态', async () => {
    let finish!: (value: { message: typeof answer; refused: boolean }) => void;
    vi.mocked(askAiKnowledge).mockImplementation(() => new Promise(resolve => { finish = resolve; })); vi.mocked(cancelAiChatGeneration).mockResolvedValue(true);
    const wrapper = await ready(); vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      vi.mocked(getAiChatSession).mockResolvedValueOnce({ ...session, isGenerating: true }).mockResolvedValue(session);
      await wrapper.get('.ai-chat-composer button').trigger('click'); await wrapper.get('[data-testid="ai-chat-stop"]').trigger('click'); await flushPromises();
      finish({ message: answer, refused: true }); await flushPromises(); await vi.advanceTimersByTimeAsync(1500); await flushPromises();
      expect(getAiChatSession).toHaveBeenCalledTimes(3); await wrapper.get('textarea').setValue('继续'); expect(wrapper.get('.ai-chat-composer button').attributes('disabled')).toBeUndefined();
    } finally { wrapper.unmount(); vi.useRealTimers(); }
  });
  it('卸载页面中止等待并丢弃迟到回答', async () => {
    let finish!: (value: { message: typeof answer; refused: boolean }) => void;
    vi.mocked(askAiKnowledge).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    const wrapper = await ready(); await wrapper.get('.ai-chat-composer button').trigger('click'); wrapper.unmount(); finish({ message: answer, refused: true }); await flushPromises();
    expect(vi.mocked(askAiKnowledge).mock.calls[0]![2]?.aborted).toBe(true); expect(getAiChatSession).toHaveBeenCalledTimes(1);
  });
});
