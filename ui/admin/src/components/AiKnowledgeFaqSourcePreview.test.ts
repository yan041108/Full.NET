import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { AiKnowledgeCitation, AiKnowledgeFaqPublishedContent } from '@fullnet/client-contracts';
import AiKnowledgeSourceDialog from './AiKnowledgeSourceDialog.vue';
import { useSessionStore } from '../auth/session';
import { getPublishedFaq } from '../api/ai-knowledge-faq-lifecycle';
import { downloadKnowledgeSource, listKnowledgeVersions } from '../api/ai-knowledge';
vi.mock('../api/ai-knowledge-faq-lifecycle', () => ({ getPublishedFaq: vi.fn() }));
vi.mock('../api/ai-knowledge', () => ({ downloadKnowledgeSource: vi.fn(), listKnowledgeVersions: vi.fn() }));
const id = (n: number) => `01990000-0000-7000-8000-${n.toString().padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3),
  publicationId: id(4), generationId: id(5), manifestHash: 'A'.repeat(64), sourceKind: 'faq-revision-v1', faqId: id(6), faqRevisionId: id(7) };
const citation: AiKnowledgeCitation = { id: id(8), documentId: id(2), versionId: id(3), sequence: 0, pageNumber: null, offset: 0, length: 10,
  faqSource: { source, embeddingModelConfigId: id(9), embeddingModelVersion: 1 } };
const published: AiKnowledgeFaqPublishedContent = { faqId: id(6), revisionId: id(7), publicationId: id(4), origin: 'independent', basis: '维护者独立编写依据', sourceHash: 'B'.repeat(64),
  content: { question: '标准问题', aliases: [], answer: '<img src=x onerror=alert(1)>正式答案', category: '制度', validFromUtc: null, expiresAtUtc: null },
  sources: [], effectiveFromUtc: '2026-10-01T00:00:00Z', effectiveToUtc: '2027-01-01T00:00:00Z' };
const wrappers: VueWrapper[] = [];
function render(allowed = true) {
  const pinia = createPinia(); setActivePinia(pinia); const store = useSessionStore();
  vi.spyOn(store, 'can').mockImplementation(code => code !== 'ai.knowledge_faqs.read' || allowed);
  const wrapper = mount(AiKnowledgeSourceDialog, { props: { open: true, documentId: id(2), versionId: id(3), citation },
    global: { plugins: [pinia], stubs: { Teleport:true, ArtFormDialog: { template: '<div><slot /></div>' }, AiKnowledgeBaseSelect: true } } });
  wrappers.push(wrapper); return { wrapper, store };
}
afterEach(() => { wrappers.splice(0).forEach(value => value.unmount()); vi.resetAllMocks(); vi.restoreAllMocks(); });
describe('FAQ 采用专属发布正文而非文件入口', () => {
  it('明确读取同一发布，纯文本渲染；从不下载文件或查普通版本', async () => {
    vi.mocked(getPublishedFaq).mockResolvedValue(published); const { wrapper } = render();
    expect(getPublishedFaq).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-source-download"]').exists()).toBe(false);
    await wrapper.get('[data-testid="knowledge-faq-source-read"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="knowledge-faq-source-body"]').text()).toContain(published.content.answer); expect(wrapper.find('img').exists()).toBe(false);
    expect(getPublishedFaq).toHaveBeenCalledWith({ knowledgeBaseId: id(1), faqId: id(6) }, expect.any(AbortSignal));
    expect(downloadKnowledgeSource).not.toHaveBeenCalled(); expect(listKnowledgeVersions).not.toHaveBeenCalled();
  });
  it('FAQ 撤权拒读时清空正文，并通知外层清除旧命中和回答', async () => {
    vi.mocked(getPublishedFaq).mockResolvedValueOnce(published).mockRejectedValueOnce({ status: 409 }); const { wrapper } = render();
    await wrapper.get('[data-testid="knowledge-faq-source-read"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="knowledge-faq-source-read"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="knowledge-faq-source-body"]').exists()).toBe(false); expect(wrapper.emitted('unavailable')).toHaveLength(1);
    expect(downloadKnowledgeSource).not.toHaveBeenCalled();
  });
  it('新发布不能自动替代冻结来源', async () => {
    vi.mocked(getPublishedFaq).mockResolvedValue({ ...published, publicationId: id(10) }); const { wrapper } = render();
    await wrapper.get('[data-testid="knowledge-faq-source-read"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="knowledge-faq-source-body"]').exists()).toBe(false); expect(wrapper.emitted('unavailable')).toHaveLength(1);
  });
  it('缺少 FAQ 权限不回退普通文件，身份不匹配也不发请求', async () => {
    const { wrapper } = render(false); expect(wrapper.get('[data-testid="knowledge-faq-source-read"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="knowledge-faq-source-read"]').trigger('click'); expect(getPublishedFaq).not.toHaveBeenCalled(); expect(downloadKnowledgeSource).not.toHaveBeenCalled();
    const other = render().wrapper; await other.setProps({ documentId: id(11) }); await other.get('[data-testid="knowledge-faq-source-read"]').trigger('click'); await flushPromises();
    expect(getPublishedFaq).not.toHaveBeenCalled(); expect(downloadKnowledgeSource).not.toHaveBeenCalled();
  });
  it('会话切换取消等待，迟到响应不能恢复旧正文', async () => {
    let resolve!: (value: AiKnowledgeFaqPublishedContent) => void; vi.mocked(getPublishedFaq).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper, store } = render(); await wrapper.get('[data-testid="knowledge-faq-source-read"]').trigger('click'); store.switching = true; await flushPromises();
    resolve(published); await flushPromises(); expect(wrapper.find('[data-testid="knowledge-faq-source-body"]').exists()).toBe(false);
  });
});
