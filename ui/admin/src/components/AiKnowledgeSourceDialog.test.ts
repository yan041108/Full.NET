import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, describe, expect, it, vi } from 'vitest';
import AiKnowledgeSourceDialog from './AiKnowledgeSourceDialog.vue';
import type { AiKnowledgeVersionResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { downloadKnowledgeSource } from '../api/ai-knowledge';
import AiKnowledgeBaseSelect from './AiKnowledgeBaseSelect.vue';
vi.mock('./AiKnowledgePdfPreview.vue', () => ({ __esModule: true, default: { name: 'AiKnowledgePdfPreview', props: ['data', 'initialPage'], template: '<div />' } }));
vi.mock('../api/ai-knowledge', () => ({ downloadKnowledgeSource: vi.fn(), listKnowledgeVersions: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 100 }) }));
let wrappers: VueWrapper[] = [];
function render(allow = true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockReturnValue(allow);
  const wrapper = mount(AiKnowledgeSourceDialog, { props: { open: true, knowledgeBaseId: 'base', documentId: 'doc', versionId: 'version' }, global: { plugins: [pinia], stubs: {Teleport:true, ArtFormDialog: { template: '<div><slot /></div>' }, AiKnowledgeBaseSelect: true } } }); wrappers.push(wrapper); return wrapper;
}
function textBlob(text: string) { const blob = new Blob([text], { type: 'text/plain' }); Object.defineProperty(blob, 'arrayBuffer', { value: async () => new TextEncoder().encode(text).buffer }); return blob; }
afterEach(() => { wrappers.forEach(w => w.unmount()); wrappers = []; vi.clearAllMocks(); vi.restoreAllMocks(); });
describe('授权来源预览', () => {
  it('多库引用只提示原请求范围，不默认绑定第一库或读取来源', async () => {
    const wrapper = render(); await wrapper.setProps({ knowledgeBaseId: undefined, candidateBaseIds: ['first', 'second'] });
    expect(wrapper.getComponent(AiKnowledgeBaseSelect).props('candidateIds')).toEqual(['first', 'second']); expect(wrapper.get('[data-testid="knowledge-source-read"]').attributes('disabled')).toBeDefined(); expect(downloadKnowledgeSource).not.toHaveBeenCalled();
  });
  it('原请求范围之外的来源选择不发起网络请求', async () => {
    const wrapper = render(); await wrapper.setProps({ knowledgeBaseId: undefined, candidateBaseIds: ['first', 'second'] });
    wrapper.getComponent(AiKnowledgeBaseSelect).vm.$emit('update:modelValue', 'outside'); await flushPromises(); await wrapper.get('[data-testid="knowledge-source-read"]').trigger('click'); await flushPromises(); expect(downloadKnowledgeSource).not.toHaveBeenCalled();
  });
  it('没有来源读取权时不创建读取操作', async () => { const wrapper = render(false); expect(wrapper.find('[data-testid="knowledge-source-read"]').exists()).toBe(false); expect(downloadKnowledgeSource).not.toHaveBeenCalled(); });
  it('文本作为纯文本渲染，读取被撤权后清除旧预览', async () => {
    vi.mocked(downloadKnowledgeSource).mockResolvedValueOnce(textBlob('<img src=x onerror=alert(1)>')).mockRejectedValueOnce({ status: 403 });
    const wrapper = render(); await wrapper.get('[data-testid="knowledge-source-read"]').trigger('click'); await flushPromises();
    expect(wrapper.get('pre').text()).toBe('<img src=x onerror=alert(1)>'); expect(wrapper.find('img').exists()).toBe(false);
    await wrapper.get('[data-testid="knowledge-source-read"]').trigger('click'); await flushPromises(); expect(wrapper.find('pre').exists()).toBe(false); expect(wrapper.find('[data-testid="knowledge-source-error"]').exists()).toBe(true);
  });
  it('关闭来源期间到达的响应不能回填', async () => {
    let finish!: (blob: Blob) => void; vi.mocked(downloadKnowledgeSource).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const wrapper = render(); await wrapper.get('[data-testid="knowledge-source-read"]').trigger('click'); await wrapper.setProps({ open: false }); finish(textBlob('private')); await flushPromises(); expect(wrapper.find('pre').exists()).toBe(false);
    expect(vi.mocked(downloadKnowledgeSource).mock.calls[0]![1]?.aborted).toBe(true);
  });
  it('作用域切换立即清空来源并关闭', async () => {
    vi.mocked(downloadKnowledgeSource).mockResolvedValue(textBlob('private')); const wrapper = render(); await wrapper.get('[data-testid="knowledge-source-read"]').trigger('click'); await flushPromises();
    useSessionStore().switching = true; await flushPromises(); expect(wrapper.find('pre').exists()).toBe(false); expect(wrapper.emitted('update:open')?.at(-1)).toEqual([false]);
  });
});

it('真实 octet-stream 来源使用精确版本元数据进行文本预览', async () => {
  const blob = textBlob('真实文本'); Object.defineProperty(blob, 'type', { value: 'application/octet-stream' });
  vi.mocked(downloadKnowledgeSource).mockResolvedValue(blob); const wrapper = render();
  const version: AiKnowledgeVersionResponse = { id: 'version', documentId: 'doc', contentHash: 'hash', contentType: 'text/plain', originalFileName: 'file.txt', sizeBytes: 12, versionNumber: 1, processingStatus: 'ready', createdAtUtc: 'now' };
  await wrapper.setProps({ version }); await wrapper.get('[data-testid="knowledge-source-read"]').trigger('click'); await flushPromises(); expect(wrapper.get('pre').text()).toBe('真实文本');
});

it('PDF 下载按精确版本元数据命名并释放临时 URL', async () => {
  const blob = new Blob(['pdf'], { type: 'application/octet-stream' }); vi.mocked(downloadKnowledgeSource).mockResolvedValue(blob);
  const create = vi.fn().mockReturnValue('blob:temporary'); const revoke = vi.fn();
  vi.stubGlobal('URL', class extends URL { static createObjectURL = create; static revokeObjectURL = revoke; });
  const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function(this: HTMLAnchorElement) { expect(this.download).toBe('file.pdf'); });
  const wrapper = render();
  try {
    await wrapper.setProps({ version: { id: 'version', documentId: 'doc', contentHash: 'hash', contentType: 'application/pdf', originalFileName: 'file.pdf', sizeBytes: 3, versionNumber: 1, processingStatus: 'ready', createdAtUtc: 'now' } });
    await wrapper.get('[data-testid="knowledge-source-download"]').trigger('click'); await flushPromises();
    expect(downloadKnowledgeSource).toHaveBeenCalledTimes(1); expect(click).toHaveBeenCalledTimes(1); expect(revoke).toHaveBeenCalledWith('blob:temporary');
  } finally { vi.unstubAllGlobals(); }
});

it('PDF 精确版本来源交给画布预览，并在换源时卸载', async () => {
  const blob = textBlob('pdf'); vi.mocked(downloadKnowledgeSource).mockResolvedValue(blob);
  const wrapper = render();
  await wrapper.setProps({ version: { id: 'version', documentId: 'doc', contentHash: 'hash', contentType: 'application/pdf', originalFileName: 'file.pdf', sizeBytes: 3, versionNumber: 1, processingStatus: 'ready', createdAtUtc: 'now' }, citation: { id: 'chunk', documentId: 'doc', versionId: 'version', sequence: 0, pageNumber: 2, offset: 0, length: 10 } });
  await wrapper.get('[data-testid="knowledge-source-read"]').trigger('click'); await flushPromises();
  expect(wrapper.findComponent({ name: 'AiKnowledgePdfPreview' }).exists()).toBe(true);
  expect(wrapper.findComponent({ name: 'AiKnowledgePdfPreview' }).props('initialPage')).toBe(2);
  await wrapper.setProps({ versionId: 'other' }); expect(wrapper.findComponent({ name: 'AiKnowledgePdfPreview' }).exists()).toBe(false);
});

it('跨页人工引用可逐一选择真实上下文，补写不产生定位按钮且换引用清空正文', async () => {
  vi.mocked(downloadKnowledgeSource).mockResolvedValue(textBlob('pdf'));
  const wrapper = render();
  const location = (pageNumber: number) => ({ blockSequence: pageNumber - 1, kind: 'pdf_page', pageNumber, paragraphNumber: null,
    tableNumber: null, rowNumber: null, cellNumber: null, grid: null, slide: null, htmlPath: null, image: null });
  await wrapper.setProps({ version: { id: 'version', documentId: 'doc', contentHash: 'hash', contentType: 'application/pdf', originalFileName: 'file.pdf', sizeBytes: 3, versionNumber: 1, processingStatus: 'ready', createdAtUtc: 'now' },
    citation: { id: 'chunk', documentId: 'doc', versionId: 'version', sequence: 0, pageNumber: null, offset: null, length: null,
      correction: { revisionId: 'revision', textLength: 8, spans: [
        { contentKind: 'original', textOffset: 0, textLength: 2, sourceLocation: location(1), sourceOffset: 0, sourceLength: 2 },
        { contentKind: 'corrected', textOffset: 3, textLength: 2, sourceLocation: location(3), sourceOffset: 0, sourceLength: 20 },
        { contentKind: 'supplement', textOffset: 6, textLength: 2, sourceLocation: null, sourceOffset: null, sourceLength: null }
      ] } } });
  await wrapper.get('[data-testid="knowledge-source-read"]').trigger('click'); await flushPromises();
  expect(wrapper.findComponent({ name: 'AiKnowledgePdfPreview' }).props('initialPage')).toBe(1);
  const contexts = wrapper.findAll('[data-testid="knowledge-source-context"]'); expect(contexts).toHaveLength(2);
  await contexts[1]!.trigger('click'); await flushPromises();
  expect(contexts[1]!.attributes('aria-pressed')).toBe('true');
  expect(wrapper.findComponent({ name: 'AiKnowledgePdfPreview' }).props('initialPage')).toBe(3);
  expect(downloadKnowledgeSource).toHaveBeenCalledTimes(1);
  await wrapper.setProps({ citation: undefined });
  expect(wrapper.findComponent({ name: 'AiKnowledgePdfPreview' }).exists()).toBe(false);
  expect(wrapper.find('[data-testid="knowledge-source-context"]').exists()).toBe(false);
});
