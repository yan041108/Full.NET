import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElSelect } from 'element-plus';
import AiKnowledgeSearchAnswerDialog from './AiKnowledgeSearchAnswerDialog.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-chat';
vi.mock('../api/ai-chat', () => ({ askAiKnowledge: vi.fn(), getAiChatSession: vi.fn(), listAiChatSessions: vi.fn() }));
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const target = { knowledgeBaseIds: [id(1)], embeddingModelConfigId: id(6), question: '问题', searchScope: { documentIds: [id(2)], embeddingModelVersion: 1, sources: [source] } };
const chat = { id: id(7), tenantId: null, ownerUserId: id(8), modelConfigId: id(9), modelName: '批准模型', title: '问答', isGenerating: false,
  createdAtUtc: '2026-10-29T00:00:00Z', updatedAtUtc: null, version: 1, messages: [] };
const message = { id: id(10), sessionId: chat.id, roleKey: 'assistant', statusKey: 'completed', content: '当前可读答案', promptTokens: null, completionTokens: null,
  createdAtUtc: chat.createdAtUtc, isKnowledgeAnswer: true, citations: [], knowledgeUnavailableCode: null };
const wrappers: VueWrapper[] = [];
async function render(allowed = true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockReturnValue(allowed);
  const wrapper = mount(AiKnowledgeSearchAnswerDialog, { props: { open: true, target }, global: { plugins: [pinia], stubs: {
    ElDialog: { template: '<div><slot /><slot name="footer" /></div>' }, AiKnowledgeSourceDialog: true, AiKnowledgeAnswerFeedback: true
  } } }); wrappers.push(wrapper); await flushPromises();
  if (allowed) { wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', chat.id); await flushPromises(); }
  return wrapper;
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.listAiChatSessions).mockResolvedValue({ items: [{ ...chat, messageCount: 0, lastMessageAtUtc: null }], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.askAiKnowledge).mockResolvedValue({ message, refused: false }); vi.mocked(api.getAiChatSession).mockResolvedValue({ ...chat, messages: [message] }); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });
describe('冻结搜索到收费问答及历史', () => {
  it('缺权限时不读取会话或派发模型', async () => { await render(false); expect(api.listAiChatSessions).not.toHaveBeenCalled(); expect(api.askAiKnowledge).not.toHaveBeenCalled(); });
  it('只在确认后派发一次并保留全部范围，再读正式历史', async () => {
    const wrapper = await render(); expect(api.askAiKnowledge).not.toHaveBeenCalled(); await wrapper.get('[data-testid="answer-confirm"]').trigger('click'); await flushPromises();
    expect(api.askAiKnowledge).toHaveBeenCalledTimes(1); expect(vi.mocked(api.askAiKnowledge).mock.calls[0]!.slice(0, 2)).toEqual([chat.id, target]);
    expect(wrapper.get('[data-testid="search-answer-text"]').text()).toBe(message.content);
    await wrapper.get('[data-testid="answer-history"]').trigger('click'); await flushPromises(); expect(api.askAiKnowledge).toHaveBeenCalledTimes(1); expect(api.getAiChatSession).toHaveBeenCalledTimes(2);
  });
  it('历史撤权隐藏正文和引用，不沿用最初答案', async () => { const wrapper = await render(); await wrapper.get('[data-testid="answer-confirm"]').trigger('click'); await flushPromises();
    vi.mocked(api.getAiChatSession).mockResolvedValueOnce({ ...chat, messages: [{ ...message, content: '', knowledgeUnavailableCode: 'ai.knowledge.answer_unavailable' }] });
    await wrapper.get('[data-testid="answer-history"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="search-answer-text"]').exists()).toBe(false);
    expect(wrapper.emitted('unavailable')).toHaveLength(1); });
  it('预览按完整来源集合精确绑定知识库，避免要求重复选库', async () => {
    const citation = { id: id(11), documentId: source.documentId, versionId: source.versionId, sequence: 0, pageNumber: null, offset: 0, length: 4 };
    vi.mocked(api.askAiKnowledge).mockResolvedValueOnce({ message: { ...message, citations: [citation] }, refused: false });
    vi.mocked(api.getAiChatSession).mockResolvedValueOnce({ ...chat, messages: [{ ...message, citations: [citation] }] });
    const wrapper = await render(); await wrapper.get('[data-testid="answer-confirm"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="answer-preview"]').trigger('click');
    expect(wrapper.findComponent({ name: 'AiKnowledgeSourceDialog' }).props('knowledgeBaseId')).toBe(source.knowledgeBaseId);
  });
  it('版本冲突清空展示并要求上层采用新发布', async () => { const wrapper = await render();
    vi.mocked(api.askAiKnowledge).mockRejectedValueOnce({ type: 'about:blank', status: 409, title: 'changed', code: 'ai.knowledge.search_source_changed' });
    await wrapper.get('[data-testid="answer-confirm"]').trigger('click'); await flushPromises(); expect(wrapper.emitted('changed')).toHaveLength(1);
    expect(wrapper.find('[data-testid="search-answer-text"]').exists()).toBe(false); expect(api.getAiChatSession).not.toHaveBeenCalled(); });
  it('未知失败不自动重试收费', async () => { const wrapper = await render(); vi.mocked(api.askAiKnowledge).mockRejectedValueOnce(new Error('unknown'));
    await wrapper.get('[data-testid="answer-confirm"]').trigger('click'); await flushPromises(); expect(wrapper.get('[data-testid="answer-confirm"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="answer-history"]').trigger('click'); await flushPromises(); expect(api.askAiKnowledge).toHaveBeenCalledTimes(1); });
  it('日程问答未知失败通知上层清空私人来源，不隐式重试', async () => {
    const wrapper = await render(); await wrapper.setProps({ target: { ...target, personalSchedules: [{ id: id(22), version: 1, projectionHash: 'A'.repeat(64) }] } });
    await flushPromises(); wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', chat.id); await flushPromises();
    vi.mocked(api.askAiKnowledge).mockRejectedValueOnce(new Error('unknown'));
    await wrapper.get('[data-testid="answer-confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.emitted('unavailable')).toHaveLength(1); expect(api.askAiKnowledge).toHaveBeenCalledTimes(1);
    expect(wrapper.find('[data-testid="search-answer-text"]').exists()).toBe(false);
  });
  it.each(['ai.knowledge.answer_unavailable', 'ai.knowledge.model_unavailable'])('来源不可用清空搜索展示 %s', async code => {
    const wrapper = await render(); vi.mocked(api.askAiKnowledge).mockRejectedValueOnce({ type: 'about:blank', title: 'unavailable', code, status: 409 });
    await wrapper.get('[data-testid="answer-confirm"]').trigger('click'); await flushPromises(); expect(wrapper.emitted('unavailable')).toHaveLength(1);
    expect(wrapper.find('[data-testid="search-answer-text"]').exists()).toBe(false);
  });
  it('关闭后取消 IO 并拒绝迟到答案', async () => { const wrapper = await render(); let resolve!: (value: { message: typeof message; refused: boolean }) => void;
    vi.mocked(api.askAiKnowledge).mockReturnValueOnce(new Promise(done => { resolve = done; })); await wrapper.get('[data-testid="answer-confirm"]').trigger('click');
    const signal = vi.mocked(api.askAiKnowledge).mock.calls[0]![2]!; await wrapper.setProps({ open: false }); expect(signal.aborted).toBe(true);
    resolve({ message, refused: false }); await flushPromises(); expect(wrapper.find('[data-testid="search-answer-text"]').exists()).toBe(false); expect(api.getAiChatSession).not.toHaveBeenCalled(); });
});
