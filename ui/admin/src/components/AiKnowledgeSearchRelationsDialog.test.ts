import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { useSessionStore } from '../auth/session';
import AiKnowledgeSearchRelationsDialog from './AiKnowledgeSearchRelationsDialog.vue';
import * as api from '../api/ai-knowledge-search';
vi.mock('../api/ai-knowledge-search', () => ({ readKnowledgeSearchRelations: vi.fn() }));
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const target = { source, embeddingModelConfigId: id(6), embeddingModelVersion: 1, anchorSequence: 0 };
const response = () => ({ strategy: 'embedded_image_parent_v1', source, documentTitle: '制度', documentMetadataVersion: 1, anchorSequence: 0,
  relationState: 'none', relations: [], blocks: [{ sequence: 0, text: '事实', pageNumber: null, offset: 0, length: 2, sourceLocation: null, docxLocation: null, correction: null }] });
const wrappers: VueWrapper[] = [];
async function render(allow = true) { const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockReturnValue(allow);
  const wrapper = mount(AiKnowledgeSearchRelationsDialog, { props: { open: true, target }, global: { plugins: [pinia], stubs: { ArtFormDialog: { template: '<div><slot /></div>' }, AiKnowledgeSourceDialog: true } } });
  wrappers.push(wrapper); await flushPromises(); return wrapper; }
async function read(wrapper: VueWrapper) { await wrapper.get('[data-testid="relations-load"]').trigger('click'); await flushPromises(); }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.readKnowledgeSearchRelations).mockResolvedValue(response()); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });
describe('真实父子上下文展示', () => {
  it('显式只读，不把普通块展示为虚构关系', async () => { const wrapper = await render(); expect(api.readKnowledgeSearchRelations).not.toHaveBeenCalled(); await read(wrapper);
    expect(vi.mocked(api.readKnowledgeSearchRelations).mock.calls[0]![0].body).toEqual(target); expect(wrapper.get('[data-testid="relations-state"]').text()).toContain('没有可验证');
    expect(wrapper.findAll('[data-testid="relation-block"]')).toHaveLength(1); expect(wrapper.findAll('[data-testid="relation-edge"]')).toHaveLength(0); });
  it('无读取权限不发起请求', async () => { const wrapper = await render(false); await read(wrapper); expect(api.readKnowledgeSearchRelations).not.toHaveBeenCalled(); });
  it('父正文未索引明确提示，不把图片正文当作父正文', async () => {
    const parent = { blockSequence: 0, kind: 'docx_paragraph', pageNumber: null, paragraphNumber: 1, tableNumber: null, rowNumber: null, cellNumber: null, grid: null, slide: null, htmlPath: null, image: null };
    const child = { ...parent, blockSequence: 1, kind: 'image_region', paragraphNumber: null, image: { frameNumber: 1, imageWidth: 100, imageHeight: 100, x: 0, y: 0, width: 100, height: 100, confidence: 1, tableNumber: null, rowNumber: null, columnNumber: null, parent } };
    vi.mocked(api.readKnowledgeSearchRelations).mockResolvedValueOnce({ ...response(), relationState: 'parent_not_indexed',
      relations: [{ parentSequence: null, childSequence: 0, parentLocation: parent, childLocation: child }], blocks: [{ ...response().blocks[0]!, sourceLocation: child }] });
    const wrapper = await render(); await read(wrapper); expect(wrapper.get('[data-testid="relations-state"]').text()).toContain('未进入当前发布索引');
    expect(wrapper.findAll('[data-testid="relation-block"]')).toHaveLength(1); expect(wrapper.get('[data-testid="relation-edge"]').text()).toContain('未索引正文');
  });
  it('预览精确绑定来源知识库与文件版本', async () => { const wrapper = await render(); await read(wrapper); await wrapper.get('[data-testid="relations-preview"]').trigger('click');
    const preview = wrapper.findComponent({ name: 'AiKnowledgeSourceDialog' }); expect(preview.props('knowledgeBaseId')).toBe(source.knowledgeBaseId); expect(preview.props('versionId')).toBe(source.versionId); });
  it.each(['ai.knowledge.search_source_changed', 'ai.knowledge.search_unavailable'])('失败清空双方正文 %s', async code => { const wrapper = await render(); await read(wrapper);
    vi.mocked(api.readKnowledgeSearchRelations).mockRejectedValueOnce({ type: 'about:blank', title: 'unavailable', status: 409, code }); await read(wrapper);
    expect(wrapper.emitted(code.includes('changed') ? 'changed' : 'unavailable')).toHaveLength(1); expect(wrapper.findAll('[data-testid="relation-block"]')).toHaveLength(0); });
  it('关闭取消读取且迟到正文不能恢复', async () => { let complete!: (value: ReturnType<typeof response>) => void;
    vi.mocked(api.readKnowledgeSearchRelations).mockImplementationOnce(() => new Promise(resolve => { complete = resolve; })); const wrapper = await render();
    await wrapper.get('[data-testid="relations-load"]').trigger('click'); const signal = vi.mocked(api.readKnowledgeSearchRelations).mock.calls[0]![1];
    await wrapper.setProps({ open: false }); complete(response()); await flushPromises(); expect(signal?.aborted).toBe(true); expect(wrapper.findAll('[data-testid="relation-block"]')).toHaveLength(0); });
});
