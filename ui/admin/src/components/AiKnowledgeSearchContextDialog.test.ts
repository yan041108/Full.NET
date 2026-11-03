import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import AiKnowledgeSearchContextDialog from './AiKnowledgeSearchContextDialog.vue';
import * as api from '../api/ai-knowledge-search';
vi.mock('../api/ai-knowledge-search', () => ({ readKnowledgeSearchContext: vi.fn() }));
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const target = { source, embeddingModelConfigId: id(6), embeddingModelVersion: 1, anchorSequence: 0 };
const response = () => ({ strategy: 'adjacent_sequence_v1', source, documentTitle: '制度', documentMetadataVersion: 1, anchorSequence: 0, radius: 1,
  hasPreviousBlocks: false, hasNextBlocks: true, blocks: [0, 1].map(sequence => ({ sequence, text: '实际片段', pageNumber: null, offset: sequence * 4, length: 4, sourceLocation: null, docxLocation: null, correction: null })) });
const wrappers: VueWrapper[] = [];
async function render() { const pinia = createPinia(); setActivePinia(pinia); const wrapper = mount(AiKnowledgeSearchContextDialog, {
  props: { open: true, target }, global: { plugins: [pinia], stubs: { ArtFormDialog: { template: '<div><slot /></div>' }, AiKnowledgeSourceDialog: true } }
}); wrappers.push(wrapper); await flushPromises(); return wrapper; }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.readKnowledgeSearchContext).mockResolvedValue(response()); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); });
describe('已发布上下文窗口', () => {
  it('仅传冻结来源和窗口，不持久化或拼接正文', async () => { const wrapper = await render(); expect(wrapper.findAll('[data-testid="context-block"]')).toHaveLength(2);
    expect(vi.mocked(api.readKnowledgeSearchContext).mock.calls[0]![0].body).toEqual({ ...target, radius: 1 }); });
  it('重新核验仍绑定原发布', async () => { const wrapper = await render(); await wrapper.get('[data-testid="context-refresh"]').trigger('click'); await flushPromises();
    expect(vi.mocked(api.readKnowledgeSearchContext).mock.calls[1]![0].body.source).toEqual(source); });
  it.each(['ai.knowledge.search_source_changed', 'authorization.permission_denied'])('失败清空窗口并上报 %s', async code => {
    const wrapper = await render(); vi.mocked(api.readKnowledgeSearchContext).mockRejectedValueOnce({ type: 'https://full.net/errors/' + code, title: 'unavailable', status: code.includes('changed') ? 409 : 403, code });
    await wrapper.get('[data-testid="context-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.findAll('[data-testid="context-block"]')).toHaveLength(0); expect(wrapper.emitted(code.includes('changed') ? 'changed' : 'unavailable')).toHaveLength(1);
  });
  it('关闭后取消 IO 并拒绝迟到正文', async () => { let resolve!: (value: ReturnType<typeof response>) => void;
    vi.mocked(api.readKnowledgeSearchContext).mockReturnValueOnce(new Promise(done => { resolve = done; })); const wrapper = await render();
    const signal = vi.mocked(api.readKnowledgeSearchContext).mock.calls[0]![1]!; await wrapper.setProps({ open: false }); expect(signal.aborted).toBe(true);
    resolve(response()); await flushPromises(); expect(wrapper.findAll('[data-testid="context-block"]')).toHaveLength(0);
  });
});
