import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { useSessionStore } from '../auth/session';
import AiKnowledgeSearchComparisonDialog from './AiKnowledgeSearchComparisonDialog.vue';
import * as api from '../api/ai-knowledge-search';
vi.mock('../api/ai-knowledge-search', () => ({ compareKnowledgeSearch: vi.fn() }));
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const target = { knowledgeBaseIds: [id(1)], embeddingModelConfigId: id(6), query: '事实', limit: 5, includeDiagnostics: false,
  searchScope: { documentIds: [id(2)], embeddingModelVersion: 1, sources: [source] } };
const hit = { source, documentTitle: '制度', documentMetadataVersion: 1, sequence: 0, text: '事实', score: 1, pageNumber: null, offset: 0, length: 2, sourceLocation: null, docxLocation: null, correction: null };
const response = () => ({ strategy: 'same_candidates_v1', query: '事实', sources: [source], keyword: { strategy: 'keyword_all_v1', scoreKind: 'keyword_occurrences', hasMore: false, hits: [hit] },
  vector: { strategy: 'vector_cosine_v1', scoreKind: 'cosine_similarity', hasMore: false, hits: [{ ...hit, score: 0.8 }] }, usage: { operationId: id(7), status: 'unknown' }, diagnostics: null });
const wrappers: VueWrapper[] = [];
async function render(allow = true) { const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockReturnValue(allow);
  const wrapper = mount(AiKnowledgeSearchComparisonDialog, { props: { open: true, target }, global: { plugins: [pinia], stubs: { ArtFormDialog: { template: '<div><slot /></div>' }, AiKnowledgeSourceDialog: true } } });
  wrappers.push(wrapper); await flushPromises(); return wrapper; }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.compareKnowledgeSearch).mockResolvedValue(response()); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });
describe('同输入收费策略比较', () => {
  it('只有明确确认才调用一次，未知用量不显示零费用', async () => { const wrapper = await render(); expect(api.compareKnowledgeSearch).not.toHaveBeenCalled();
    await wrapper.get('[data-testid="compare-confirm"]').trigger('click'); await flushPromises(); expect(api.compareKnowledgeSearch).toHaveBeenCalledTimes(1);
    expect(vi.mocked(api.compareKnowledgeSearch).mock.calls[0]![0].body).toEqual(target); expect(wrapper.get('[data-testid="comparison-usage"]').text()).toContain('用量未知');
    expect(wrapper.get('[data-testid="compare-confirm"]').attributes('disabled')).toBeDefined(); expect(wrapper.findAll('[data-testid="comparison-hit"]')).toHaveLength(2);
  });
  it('无派发权限时不调用模型', async () => { const wrapper = await render(false); await wrapper.get('[data-testid="compare-confirm"]').trigger('click'); await flushPromises(); expect(api.compareKnowledgeSearch).not.toHaveBeenCalled(); });
  it('预览始终使用服务器命中的精确知识库及版本', async () => { const wrapper = await render(); await wrapper.get('[data-testid="compare-confirm"]').trigger('click'); await flushPromises();
    await wrapper.findAll('[data-testid="comparison-preview"]')[0]!.trigger('click'); const preview = wrapper.findComponent({ name: 'AiKnowledgeSourceDialog' });
    expect(preview.props('knowledgeBaseId')).toBe(source.knowledgeBaseId); expect(preview.props('versionId')).toBe(source.versionId); });
  it.each(['ai.knowledge.search_source_changed', 'ai.knowledge.search_unavailable'])('失败关闭两组结果 %s', async code => { const wrapper = await render();
    vi.mocked(api.compareKnowledgeSearch).mockRejectedValueOnce({ type: 'about:blank', title: 'unavailable', code, status: 409 });
    await wrapper.get('[data-testid="compare-confirm"]').trigger('click'); await flushPromises(); expect(wrapper.emitted(code.includes('changed') ? 'changed' : 'unavailable')).toHaveLength(1);
    expect(wrapper.findAll('[data-testid="comparison-hit"]')).toHaveLength(0); });
  it('关闭取消 IO 且忽略迟到结果', async () => { const wrapper = await render(); let resolve!: (value: ReturnType<typeof response>) => void;
    vi.mocked(api.compareKnowledgeSearch).mockReturnValueOnce(new Promise(done => { resolve = done; })); await wrapper.get('[data-testid="compare-confirm"]').trigger('click');
    const signal = vi.mocked(api.compareKnowledgeSearch).mock.calls[0]![1]!; await wrapper.setProps({ open: false }); expect(signal.aborted).toBe(true);
    resolve(response()); await flushPromises(); expect(wrapper.findAll('[data-testid="comparison-hit"]')).toHaveLength(0); });
});
