import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElInput } from 'element-plus';
import AiKnowledgeSearchView from './AiKnowledgeSearchView.vue';
import { useSessionStore } from '../auth/session';
import * as knowledge from '../api/ai-knowledge';
import * as api from '../api/ai-knowledge-search';
vi.mock('../api/ai-knowledge', () => ({ getKnowledgeBase: vi.fn(), listKnowledgeDocuments: vi.fn() }));
vi.mock('../api/ai-knowledge-search', () => ({ searchKnowledge: vi.fn(), readKnowledgeSearchContext: vi.fn(), compareKnowledgeSearch: vi.fn(), readKnowledgeSearchRelations: vi.fn(), refineKnowledgeSearch: vi.fn() }));
const id = (n: number) => '01900000-0000-7000-8000-' + String(n).padStart(12, '0');
const base = { id: id(1), name: '制度', description: null, isEnabled: true, dataClassification: 'internal', embeddingModelConfigId: id(6), embeddingModelVersion: 1,
  generationModelConfigId: null, generationModelVersion: null, createdAtUtc: '2026-10-29T00:00:00Z', updatedAtUtc: null, version: 1 };
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const result = () => ({ strategy: 'keyword_all_v1', sources: [source], totalMatches: 1, diagnostics: null, hits: [{ source, documentTitle: '制度来源', documentMetadataVersion: 1, sequence: 0, text: '可核对的事实', score: 1,
  pageNumber: null, offset: 0, length: 6, sourceLocation: null, docxLocation: null, correction: null }] });
const wrappers: VueWrapper[] = [];
async function render(allow: (code: string) => boolean = () => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allow);
  const wrapper = mount(AiKnowledgeSearchView, { global: { plugins: [pinia], stubs: {
    AiKnowledgeBaseSelect: { emits: ['update:modelValue'], template: `<button data-testid="choose-base" @click="$emit('update:modelValue', '${id(1)}')">选择</button>` }, AiKnowledgeSourceDialog: true, AiKnowledgeSearchAnswerDialog: true, AiKnowledgeSearchComparisonDialog: true, AiKnowledgeSearchRefinementDialog: true, AiKnowledgeFaqDraftDialog: true
  } } }); wrappers.push(wrapper); await flushPromises(); return wrapper;
}
async function choose(wrapper: VueWrapper) { await wrapper.get('[data-testid="choose-base"]').trigger('click'); await flushPromises(); await wrapper.findComponent(ElInput).find('input').setValue('事实'); }
async function submit(wrapper: VueWrapper) { await wrapper.get('form').trigger('submit'); await flushPromises(); }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(knowledge.getKnowledgeBase).mockResolvedValue(base);
  vi.mocked(knowledge.listKnowledgeDocuments).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 }); vi.mocked(api.searchKnowledge).mockResolvedValue(result()); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });
describe('独立知识搜索工作台', () => {
  it('创建 FAQ 冻结全部发布来源，权限独立且不调用模型',async()=>{const w=await render();await choose(w);await submit(w);await w.get('[data-testid="search-faq"]').trigger('click');expect(w.findComponent({name:'AiKnowledgeFaqDraftDialog'}).props('selection')).toEqual({embeddingModelConfigId:id(6),embeddingModelVersion:1,sources:[source]});});
  it('没有 FAQ 写权限不显示派生入口',async()=>{const w=await render(code=>code!=='ai.knowledge_faqs.write');await choose(w);await submit(w);expect(w.find('[data-testid="search-faq"]').exists()).toBe(false);});
  it('只有已批准处理模型才开放实验，回答仍保留原查询和全部来源', async () => {
    vi.mocked(knowledge.getKnowledgeBase).mockResolvedValue({ ...base, generationModelConfigId: id(7), generationModelVersion: 3 });
    const wrapper = await render(); await choose(wrapper); await submit(wrapper); await wrapper.get('[data-testid="search-refine"]').trigger('click');
    const dialog = wrapper.findComponent({ name: 'AiKnowledgeSearchRefinementDialog' });
    expect(dialog.props('target')).toMatchObject({ query: '事实', processingModelConfigId: id(7), processingModelVersion: 3, searchScope: { sources: [source], documentIds: [] } });
    dialog.vm.$emit('update:open', false); await flushPromises(); await wrapper.get('[data-testid="search-answer"]').trigger('click');
    expect(wrapper.findComponent({ name: 'AiKnowledgeSearchAnswerDialog' }).props('target')).toMatchObject({ question: '事实', searchScope: { sources: [source] } });
  });
  it('没有批准处理模型不提供实验入口', async () => { const wrapper = await render(); await choose(wrapper); await submit(wrapper); expect(wrapper.find('[data-testid="search-refine"]').exists()).toBe(false); });
  it('实验来源漂移清空所有旧命中', async () => {
    vi.mocked(knowledge.getKnowledgeBase).mockResolvedValue({ ...base, generationModelConfigId: id(7), generationModelVersion: 3 });
    const wrapper = await render(); await choose(wrapper); await submit(wrapper); await wrapper.get('[data-testid="search-refine"]').trigger('click');
    wrapper.findComponent({ name: 'AiKnowledgeSearchRefinementDialog' }).vm.$emit('changed'); await flushPromises(); expect(wrapper.findAll('[data-testid="search-hit"]')).toHaveLength(0); expect(wrapper.find('[data-testid="search-changed"]').exists()).toBe(true);
  });
  it('关系读取只冻结命中来源，不从块序号构造父子', async () => { const wrapper = await render(); await choose(wrapper); await submit(wrapper);
    await wrapper.get('[data-testid="search-relations"]').trigger('click'); const dialog = wrapper.findComponent({ name: 'AiKnowledgeSearchRelationsDialog' });
    expect(dialog.props('target')).toEqual({ source, embeddingModelConfigId: id(6), embeddingModelVersion: 1, anchorSequence: 0 });
    dialog.vm.$emit('changed'); await flushPromises(); expect(wrapper.findAll('[data-testid="search-hit"]')).toHaveLength(0); expect(wrapper.find('[data-testid="search-changed"]').exists()).toBe(true);
  });
  it('关系不可用清空旧结果并关闭弹窗', async () => { const wrapper = await render(); await choose(wrapper); await submit(wrapper);
    await wrapper.get('[data-testid="search-relations"]').trigger('click'); wrapper.findComponent({ name: 'AiKnowledgeSearchRelationsDialog' }).vm.$emit('unavailable'); await flushPromises();
    expect(wrapper.findAll('[data-testid="search-hit"]')).toHaveLength(0); expect(wrapper.find('[data-testid="knowledge-relations-dialog"]').exists()).toBe(false); expect(wrapper.find('[data-testid="search-error"]').exists()).toBe(true);
  });
  it('比较保留完整搜索范围，来源变化清空旧命中', async () => { const wrapper = await render(); await choose(wrapper); await submit(wrapper);
    await wrapper.get('[data-testid="search-compare"]').trigger('click'); const comparison = wrapper.findComponent({ name: 'AiKnowledgeSearchComparisonDialog' });
    expect(comparison.props('target')).toMatchObject({ query: '事实', limit: 5, includeDiagnostics: false, searchScope: { documentIds: [], sources: [source], embeddingModelVersion: 1 } });
    comparison.vm.$emit('changed'); await flushPromises(); expect(wrapper.findAll('[data-testid="search-hit"]')).toHaveLength(0); expect(wrapper.find('[data-testid="search-changed"]').exists()).toBe(true);
  });
  it('比较精确权限独立于聊天发送与会话读取', async () => { const wrapper = await render(code => !['ai.chat.messages.send', 'ai.chat.sessions.read'].includes(code));
    await choose(wrapper); await submit(wrapper); expect(wrapper.find('[data-testid="search-compare"]').exists()).toBe(true); expect(wrapper.find('[data-testid="search-answer"]').exists()).toBe(false);
  });
  it('缺少精确比较权限时只有普通搜索入口', async () => { const wrapper = await render(code => code !== 'ai.knowledge_search.compare');
    await choose(wrapper); await submit(wrapper); expect(wrapper.find('[data-testid="search-compare"]').exists()).toBe(false);
  });
  it('搜索到问答保留完整来源集合，历史失效清空全部旧命中', async () => {
    const wrapper = await render(); await choose(wrapper); await submit(wrapper); await wrapper.get('[data-testid="search-answer"]').trigger('click');
    const answer = wrapper.findComponent({ name: 'AiKnowledgeSearchAnswerDialog' });
    expect(answer.props('target')).toMatchObject({ question: '事实', knowledgeBaseIds: [id(1)], searchScope: { documentIds: [], embeddingModelVersion: 1, sources: [source] } });
    answer.vm.$emit('unavailable'); await flushPromises(); expect(wrapper.findAll('[data-testid="search-hit"]')).toHaveLength(0);
    expect(wrapper.find('[data-testid="search-error"]').exists()).toBe(true);
  });
  it('只有搜索权限时不展示收费入口', async () => { const wrapper = await render(code => code !== 'ai.chat.messages.send'); await choose(wrapper); await submit(wrapper);
    expect(wrapper.find('[data-testid="search-answer"]').exists()).toBe(false);
  });
  it('缺精确搜索权限时不读取目录或正文', async () => {
    const wrapper = await render(code => code !== 'ai.knowledge_search.read'); expect(wrapper.find('[data-testid="choose-base"]').exists()).toBe(false);
    expect(knowledge.getKnowledgeBase).not.toHaveBeenCalled(); expect(api.searchKnowledge).not.toHaveBeenCalled();
  });
  it('绑定已批准索引空间，重复查询带完整发布集合', async () => {
    const wrapper = await render(); await choose(wrapper); await submit(wrapper);
    expect(wrapper.get('[data-testid="search-hit"]').text()).toContain('可核对的事实');
    expect(vi.mocked(api.searchKnowledge).mock.calls[0]![0].body).toMatchObject({ knowledgeBaseIds: [id(1)], documentIds: [], embeddingModelConfigId: id(6), embeddingModelVersion: 1, expectedSources: null });
    await submit(wrapper); expect(vi.mocked(api.searchKnowledge).mock.calls[1]![0].body.expectedSources).toEqual([source]);
  });
  it('来源冲突清空旧正文，只允许明确采用最新发布', async () => {
    const wrapper = await render(); await choose(wrapper); await submit(wrapper);
    vi.mocked(api.searchKnowledge).mockRejectedValueOnce({ type: 'https://full.net/errors/ai.knowledge.search_source_changed', title: 'changed', status: 409, code: 'ai.knowledge.search_source_changed' });
    await submit(wrapper); expect(wrapper.find('[data-testid="search-hit"]').exists()).toBe(false); expect(wrapper.find('[data-testid="search-changed"]').exists()).toBe(true);
    await wrapper.get('[data-testid="search-adopt"]').trigger('click'); await flushPromises();
    expect(vi.mocked(api.searchKnowledge).mock.calls[2]![0].body.expectedSources).toBeNull(); expect(wrapper.find('[data-testid="search-hit"]').exists()).toBe(true);
  });
  it('切换可信会话时取消读取并拒绝迟到正文', async () => {
    let resolve!: (value: ReturnType<typeof result>) => void;
    vi.mocked(api.searchKnowledge).mockReturnValueOnce(new Promise(done => { resolve = done; }));
    const wrapper = await render(); await choose(wrapper); await submit(wrapper);
    const signal = vi.mocked(api.searchKnowledge).mock.calls[0]![1]!;
    useSessionStore().$patch({ switching: true }); await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(result()); await flushPromises(); expect(wrapper.find('[data-testid="search-hit"]').exists()).toBe(false);
  });
  it('无命中有独立状态，权限失败不保留上一批正文', async () => {
    const wrapper = await render(); await choose(wrapper); await submit(wrapper);
    vi.mocked(api.searchKnowledge).mockResolvedValueOnce({ ...result(), totalMatches: 0, hits: [] }); await submit(wrapper);
    expect(wrapper.find('[data-testid="search-empty"]').exists()).toBe(true);
    vi.mocked(api.searchKnowledge).mockRejectedValueOnce({ status: 403, code: 'authorization.permission_denied' }); await submit(wrapper);
    expect(wrapper.find('[data-testid="search-empty"]').exists()).toBe(false); expect(wrapper.find('[data-testid="search-error"]').exists()).toBe(true);
  });
  it('上下文版本冲突清除全部旧命中', async () => {
    const wrapper = await render(); await choose(wrapper); await submit(wrapper);
    vi.mocked(api.readKnowledgeSearchContext).mockRejectedValueOnce({ type: 'https://full.net/errors/ai.knowledge.search_source_changed', title: 'changed', status: 409, code: 'ai.knowledge.search_source_changed' });
    await wrapper.get('[data-testid="search-context"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="search-hit"]').exists()).toBe(false); expect(wrapper.find('[data-testid="search-changed"]').exists()).toBe(true);
  });
});
