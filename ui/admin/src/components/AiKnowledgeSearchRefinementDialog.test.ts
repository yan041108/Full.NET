import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElSelect } from 'element-plus';
import { useSessionStore } from '../auth/session';
import AiKnowledgeSearchRefinementDialog from './AiKnowledgeSearchRefinementDialog.vue';
import * as api from '../api/ai-knowledge-search';
vi.mock('../api/ai-knowledge-search', () => ({ refineKnowledgeSearch: vi.fn() }));
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const target = { knowledgeBaseIds: [id(1)], embeddingModelConfigId: id(6), processingModelConfigId: id(7), processingModelVersion: 1, query: '事实', limit: 5, includeDiagnostics: false,
  searchScope: { documentIds: [id(2)], embeddingModelVersion: 1, sources: [source] } };
const hit = { source, documentTitle: '制度', documentMetadataVersion: 1, sequence: 0, text: '事实', score: 1, pageNumber: null, offset: 0, length: 2, sourceLocation: null, docxLocation: null, correction: null };
const response = () => ({ strategy: 'controlled_refinement_v1', mode: 'rewrite_rerank', query: '事实', effectiveQuery: '事实 范围', processingModelConfigId: id(7), processingModelVersion: 1, refusedStage: null as string | null, sources: [source],
  originalKeyword: { strategy: 'keyword_all_v1', scoreKind: 'keyword_occurrences', hasMore: false, hits: [hit] }, originalVector: { strategy: 'vector_cosine_v1', scoreKind: 'cosine_similarity', hasMore: false, hits: [hit] },
  rewrittenKeyword: null, rewrittenVector: null, reranked: { strategy: 'model_order_prefix_v1', scoreKind: 'ordinal_priority', hasMore: false, hits: [hit] }, rerankCandidateCount: 1,
  operations: ['original_embedding', 'rewrite', 'rewritten_embedding', 'rerank'].map((stage, index) => ({ stage, operationId: id(10 + index), status: 'unknown' })), diagnostics: null });
const wrappers: VueWrapper[] = [];
async function render(denied = '') { const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(code => code !== denied);
  const wrapper = mount(AiKnowledgeSearchRefinementDialog, { props: { open: true, target }, global: { plugins: [pinia], stubs: { ArtFormDialog: { template: '<div><slot /></div>' }, AiKnowledgeSourceDialog: true } } });
  wrappers.push(wrapper); await flushPromises(); return wrapper; }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.refineKnowledgeSearch).mockResolvedValue(response()); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });
describe('受控改写重排窗口', () => {
  it('打开不收费，明确确认仅一次并保留原始查询及未知阶段', async () => { const wrapper = await render('ai.chat.messages.send'); expect(api.refineKnowledgeSearch).not.toHaveBeenCalled();
    await wrapper.get('[data-testid="refine-confirm"]').trigger('click'); await flushPromises(); expect(api.refineKnowledgeSearch).toHaveBeenCalledTimes(1);
    expect(vi.mocked(api.refineKnowledgeSearch).mock.calls[0]![0].body).toEqual({ ...target, mode: 'rewrite_rerank' });
    expect(wrapper.get('[data-testid="refine-confirm"]').attributes('disabled')).toBeDefined(); expect(wrapper.findAll('[data-testid="refine-operation"]')).toHaveLength(4);
    expect(wrapper.text()).toContain('用量未知'); expect(wrapper.get('[data-testid="refine-effective-query"]').text()).toContain('事实 范围'); expect(target.query).toBe('事实'); });
  it.each(['ai.knowledge_search.refine', 'ai.knowledge_search.compare', 'ai.knowledge_search.read'])('缺精确权限不派发 %s', async code => { const wrapper = await render(code);
    await wrapper.get('[data-testid="refine-confirm"]').trigger('click'); expect(api.refineKnowledgeSearch).not.toHaveBeenCalled(); });
  it.each(['rewrite', 'rerank'])('独立选择只有明确新确认才派发 %s', async mode => { const wrapper = await render(); wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', mode); await flushPromises();
    expect(api.refineKnowledgeSearch).not.toHaveBeenCalled(); await wrapper.get('[data-testid="refine-confirm"]').trigger('click'); await flushPromises(); expect(vi.mocked(api.refineKnowledgeSearch).mock.calls[0]![0].body.mode).toBe(mode); });
  it('预览绑定精确知识库和来源版本', async () => { const wrapper = await render(); await wrapper.get('[data-testid="refine-confirm"]').trigger('click'); await flushPromises();
    await wrapper.findAll('[data-testid="refine-preview"]')[0]!.trigger('click'); const preview = wrapper.findComponent({ name: 'AiKnowledgeSourceDialog' });
    expect(preview.props('knowledgeBaseId')).toBe(id(1)); expect(preview.props('versionId')).toBe(id(3)); });
  it('明确显示模型拒绝且不自动重试', async () => { const value = response(); value.refusedStage = 'rerank'; value.reranked = null!; vi.mocked(api.refineKnowledgeSearch).mockResolvedValue(value);
    const wrapper = await render(); await wrapper.get('[data-testid="refine-confirm"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="refine-refused"]').exists()).toBe(true); expect(api.refineKnowledgeSearch).toHaveBeenCalledTimes(1); });
  it.each(['ai.knowledge.search_source_changed', 'ai.knowledge.search_unavailable'])('来源失败清空结果 %s', async code => { const wrapper = await render(); vi.mocked(api.refineKnowledgeSearch).mockRejectedValueOnce({ type: 'about:blank', title: 'unavailable', code, status: 409 });
    await wrapper.get('[data-testid="refine-confirm"]').trigger('click'); await flushPromises(); expect(wrapper.emitted(code.includes('changed') ? 'changed' : 'unavailable')).toHaveLength(1); expect(wrapper.findAll('[data-testid="refine-hit"]')).toHaveLength(0); });
  it('关闭后取消并拒绝迟到正文', async () => { const wrapper = await render(); let resolve!: (value: ReturnType<typeof response>) => void;
    vi.mocked(api.refineKnowledgeSearch).mockReturnValueOnce(new Promise(done => { resolve = done; })); await wrapper.get('[data-testid="refine-confirm"]').trigger('click');
    const signal = vi.mocked(api.refineKnowledgeSearch).mock.calls[0]![1]!; await wrapper.setProps({ open: false }); expect(signal.aborted).toBe(true); resolve(response()); await flushPromises(); expect(wrapper.findAll('[data-testid="refine-hit"]')).toHaveLength(0); });
  it('切换会话清空敏感结果并关闭', async () => { const wrapper = await render(); await wrapper.get('[data-testid="refine-confirm"]').trigger('click'); await flushPromises(); useSessionStore().$patch({ switching: true }); await flushPromises();
    expect(wrapper.findAll('[data-testid="refine-hit"]')).toHaveLength(0); expect(wrapper.emitted('update:open')).toEqual([[false]]); });
});
