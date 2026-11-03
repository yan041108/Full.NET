import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import AiKnowledgeCorrectionDialog from './AiKnowledgeCorrectionDialog.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-knowledge';
vi.mock('../api/ai-knowledge', () => ({ getLatestKnowledgeCorrectionPreparation: vi.fn(), getKnowledgeCorrectionPreparation: vi.fn(), getKnowledgeCorrectionWorkspace: vi.fn(), prepareKnowledgeCorrection: vi.fn(),
  editKnowledgeCorrection: vi.fn(), restoreKnowledgeCorrection: vi.fn(), listKnowledgeCorrectionRevisions: vi.fn() }));
const id = (n: number) => '01900000-0000-7000-8000-' + String(n).padStart(12, '0');
const props = { open: true, knowledgeBaseId: id(1), documentId: id(2), versionId: id(3), documentVersion: 3,
  version: { id: id(3), documentId: id(2), versionNumber: 1, originalFileName: 'source.txt', contentType: 'text/plain', contentHash: 'a'.repeat(64), sizeBytes: 20, processingStatus: 'pending_processing', createdAtUtc: '2026-10-28T00:00:00Z' } };
const location = { blockSequence: 0, kind: 'document', pageNumber: null, paragraphNumber: null, tableNumber: null, rowNumber: null, cellNumber: null, grid: null, slide: null, htmlPath: null, image: null };
const workspace = () => ({ versionId: id(3), documentVersion: 3, sourceId: id(4), revisionId: id(5), headVersion: 1, sourceHash: 'a'.repeat(64), payloadHash: 'b'.repeat(64),
  parserRuntimeId: 'managed:utf8-text-v1', contentType: 'text/plain', originalFileName: 'source.txt', blockIndex: 0, totalBlocks: 2, isSourceView: false,
  block: { id: id(6), excluded: false, separator: '', parts: [{ id: id(7), text: '人工校对', prefix: '', contentKind: 'corrected', anchorId: id(8), originalText: '完整原识别', sourceOffset: 0, sourceLength: 5, sourceLocation: location }] } });
const task = () => ({ id: id(9), versionId: id(3), documentVersion: 3, state: 'queued', attemptCount: 0, sourceId: null, failureCode: null, createdAtUtc: '2026-10-28T00:00:00Z', updatedAtUtc: '2026-10-28T00:00:00Z' });
const missing = { status: 404, code: 'ai.knowledge.correction_unavailable' };
const wrappers: VueWrapper[] = [];
async function render(allow: (code: string) => boolean = () => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allow);
  const wrapper = mount(AiKnowledgeCorrectionDialog, { props, global: { plugins: [pinia], stubs: {
    ArtFormDialog: { props: ['open'], template: '<section v-if="open"><slot /></section>' }, AiKnowledgeSourceDialog: true
  } } }); wrappers.push(wrapper); await flushPromises(); return wrapper;
}
beforeEach(() => {
  vi.clearAllMocks(); vi.mocked(api.getLatestKnowledgeCorrectionPreparation).mockRejectedValue(missing);
  vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue(workspace());
  vi.mocked(api.listKnowledgeCorrectionRevisions).mockResolvedValue({ items: [], nextHeadVersion: null });
});
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks(); vi.useRealTimers(); });
describe('持久准备与逐块对照工作台', () => {
  const receipt = () => ({ id: id(10), versionId: id(3), sourceId: id(4), parentId: id(5), headVersion: 2, documentVersion: 4,
    payloadHash: 'c'.repeat(64), reason: '人工核对', createdAtUtc: '2026-10-28T01:00:00Z' });
  async function draft(wrapper: VueWrapper, text = '本地更正') {
    await wrapper.get('[data-testid="correction-edit-part"]').trigger('click');
    await wrapper.get('[data-testid="correction-draft-text"]').setValue(text);
    await wrapper.get('[data-testid="correction-reason"]').setValue('人工核对');
  }
  it('保存使用实际双版本及来源摘要，成功后才清除本地草稿并刷新权威头', async () => {
    const wrapper = await render(); await draft(wrapper);
    vi.mocked(api.editKnowledgeCorrection).mockResolvedValue(receipt());
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue({ ...workspace(), documentVersion: 4, headVersion: 2, revisionId: id(10), payloadHash: 'c'.repeat(64) });
    await wrapper.get('[data-testid="correction-save-draft"]').trigger('click'); await flushPromises();
    expect(api.editKnowledgeCorrection).toHaveBeenCalledWith(expect.objectContaining({ body: expect.objectContaining({
      expected: { documentVersion: 3, sourceId: id(4), revisionId: id(5), headVersion: 1, sourceHash: 'a'.repeat(64), payloadHash: 'b'.repeat(64) },
      operation: { kind: 'replace', blockId: id(6), partId: id(7), text: '本地更正', offset: null, excluded: null }
    }) }), expect.any(AbortSignal));
    expect(wrapper.find('[data-testid="correction-local-draft"]').exists()).toBe(false); expect(wrapper.emitted('saved')).toEqual([[4]]);
    expect(wrapper.get('[data-testid="correction-saved"]').text()).toContain('尚未发布');
  });
  it('409 保留文本与说明，显式刷新后再次保存才使用新 CAS 和新请求键', async () => {
    const wrapper = await render(); await draft(wrapper, '冲突仍保留');
    vi.mocked(api.editKnowledgeCorrection).mockRejectedValueOnce({ status: 409, code: 'ai.knowledge.correction_conflict' }).mockResolvedValueOnce({ ...receipt(), id: id(11), parentId: id(10), documentVersion: 5, headVersion: 3 });
    await wrapper.get('[data-testid="correction-save-draft"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="correction-conflict"]').exists()).toBe(true);
    expect((wrapper.get('[data-testid="correction-draft-text"]').element as HTMLTextAreaElement).value).toBe('冲突仍保留');
    const first = vi.mocked(api.editKnowledgeCorrection).mock.calls[0]![0].body;
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue({ ...workspace(), documentVersion: 4, headVersion: 2, revisionId: id(10) });
    await wrapper.get('[data-testid="correction-refresh"]').trigger('click'); await flushPromises();
    expect((wrapper.get('[data-testid="correction-draft-text"]').element as HTMLTextAreaElement).value).toBe('冲突仍保留');
    await wrapper.get('[data-testid="correction-save-draft"]').trigger('click'); await flushPromises();
    const second = vi.mocked(api.editKnowledgeCorrection).mock.calls[1]![0].body;
    expect(second.requestKey).not.toBe(first.requestKey); expect(second.expected.documentVersion).toBe(4); expect(second.operation.text).toBe('冲突仍保留');
  });
  it('非首页保存后刷新采用新头，普通翻页仍拒绝混读不同修订', async () => {
    const value = workspace();
    const wrapper = await render();
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue({ ...value, blockIndex: 1 });
    await wrapper.get('[data-testid="correction-next"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="correction-reason"]').setValue('人工核对');
    vi.mocked(api.editKnowledgeCorrection).mockResolvedValue(receipt());
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue({ ...value, blockIndex: 1, documentVersion: 4, headVersion: 2, revisionId: id(10), block: { ...value.block, excluded: true } });
    await wrapper.get('[data-testid="correction-exclude"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="correction-error"]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="correction-page"]').text()).toBe('2 / 2');
    expect(wrapper.get('[data-testid="correction-saved"]').text()).toContain('尚未发布');
    expect(wrapper.get('[data-testid="correction-text"]').text()).toBe('人工校对');
  });
  it('未知提交只允许重试同一请求，不能把已保存但响应丢失变成第二次修订', async () => {
    const wrapper = await render(); await draft(wrapper);
    vi.mocked(api.editKnowledgeCorrection).mockRejectedValueOnce(new Error('network')).mockResolvedValueOnce(receipt());
    await wrapper.get('[data-testid="correction-save-draft"]').trigger('click'); await flushPromises();
    const first = vi.mocked(api.editKnowledgeCorrection).mock.calls[0]![0];
    expect(wrapper.get('[data-testid="correction-discard-draft"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="correction-retry-write"]').trigger('click'); await flushPromises();
    expect(vi.mocked(api.editKnowledgeCorrection).mock.calls[1]![0]).toEqual(first);
  });
  it('缺编辑权限不渲染编辑按钮；编辑撤权清空原文和本地草稿', async () => {
    const readOnly = await render(code => code !== 'ai.knowledge_corrections.edit');
    expect(readOnly.find('[data-testid="correction-edit-part"]').exists()).toBe(false);
    const wrapper = await render(); await draft(wrapper);
    vi.mocked(api.editKnowledgeCorrection).mockRejectedValue({ status: 403, code: 'authorization.permission_denied' });
    await wrapper.get('[data-testid="correction-save-draft"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="correction-local-draft"]').exists()).toBe(false); expect(wrapper.find('[data-testid="correction-text"]').exists()).toBe(false);
  });
  it('历史预览不会覆盖保存前读取的当前头，恢复明确选中历史来源并写新身份', async () => {
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue({ ...workspace(), documentVersion: 4, headVersion: 2, revisionId: id(10) });
    vi.mocked(api.listKnowledgeCorrectionRevisions).mockResolvedValue({ items: [{ ...receipt(), id: id(20), parentId: null, headVersion: 1, documentVersion: 3 }], nextHeadVersion: null });
    const wrapper = await render(); await wrapper.get('[data-testid="correction-history"]').trigger('click'); await flushPromises();
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue({ ...workspace(), revisionId: id(20), documentVersion: 4 });
    await wrapper.get('[data-testid="correction-history-preview"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="correction-edit-part"]').exists()).toBe(false);
    await wrapper.get('[data-testid="correction-reason"]').setValue('明确恢复');
    vi.mocked(api.restoreKnowledgeCorrection).mockResolvedValue({ ...receipt(), parentId: id(10), headVersion: 3, documentVersion: 5, reason: '明确恢复' });
    await wrapper.get('[data-testid="correction-restore"]').trigger('click'); await flushPromises();
    const body = vi.mocked(api.restoreKnowledgeCorrection).mock.calls[0]![0].body;
    expect(body.expected.headVersion).toBe(2); expect(body.expected.revisionId).toBe(id(10)); expect(body.targetRevisionId).toBe(id(20));
  });
  it('字符拆分转换为真实 UTF-16 位置，排版或坐标不由编辑器比例推算', async () => {
    const value = workspace(); vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue({ ...value, block: { ...value.block, parts: [{ ...value.block.parts[0]!, text: 'A😀B' }] } });
    const wrapper = await render(); await wrapper.get('[data-testid="correction-split-part"]').trigger('click');
    await wrapper.get('[data-testid="correction-reason"]').setValue('拆分');
    const input = wrapper.get('[data-testid="correction-split-offset"] input'); await input.setValue('2'); await input.trigger('change');
    vi.mocked(api.editKnowledgeCorrection).mockResolvedValue(receipt()); await wrapper.get('[data-testid="correction-save-draft"]').trigger('click'); await flushPromises();
    expect(vi.mocked(api.editKnowledgeCorrection).mock.calls[0]![0].body.operation.offset).toBe(3);
  });
  it('显示更正的完整原文，分页不在页面缓存整份正文', async () => {
    const wrapper = await render(); expect(wrapper.get('[data-testid="correction-original"]').text()).toBe('完整原识别');
    expect(wrapper.get('[data-testid="correction-text"]').text()).toBe('人工校对');
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue({ ...workspace(), blockIndex: 1 });
    await wrapper.get('[data-testid="correction-next"]').trigger('click'); await flushPromises();
    expect(api.getKnowledgeCorrectionWorkspace).toHaveBeenLastCalledWith(expect.objectContaining({ block: 1 }), expect.any(AbortSignal));
    expect(wrapper.get('[data-testid="correction-page"]').text()).toBe('2 / 2');
  });
  it('缺少校对读取权限不读取或渲染正文操作', async () => {
    const wrapper = await render(code => code !== 'ai.knowledge_corrections.read');
    expect(api.getKnowledgeCorrectionWorkspace).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="correction-prepare"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="correction-original"]').exists()).toBe(false);
  });
  it('读取权限不授予准备，补写不提供伪造定位', async () => {
    const value = workspace(); vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue({ ...value, block: { ...value.block,
      parts: [{ ...value.block.parts[0]!, contentKind: 'supplement', anchorId: null, originalText: null, sourceOffset: null, sourceLength: null, sourceLocation: null }] } });
    const wrapper = await render(code => code !== 'ai.knowledge_corrections.prepare');
    expect(wrapper.find('[data-testid="correction-prepare"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="correction-source"]').exists()).toBe(false); expect(wrapper.text()).toContain('补写没有原始位置');
  });
  it('响应丢失重试保留同一请求键及文档版本', async () => {
    vi.mocked(api.prepareKnowledgeCorrection).mockRejectedValueOnce(new Error('network')).mockResolvedValueOnce(task());
    const wrapper = await render(); await wrapper.get('[data-testid="correction-prepare"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="correction-unknown"]').exists()).toBe(true);
    const first = vi.mocked(api.prepareKnowledgeCorrection).mock.calls[0]![0];
    await wrapper.get('[data-testid="correction-prepare"]').trigger('click'); await flushPromises();
    expect(vi.mocked(api.prepareKnowledgeCorrection).mock.calls[1]![0]).toEqual(first);
    expect(first.body.documentVersion).toBe(3);
  });
  it('重新打开恢复已有队列，完成后读取独立来源，人工头仍可切回', async () => {
    vi.useFakeTimers(); vi.mocked(api.getLatestKnowledgeCorrectionPreparation).mockResolvedValue(task());
    vi.mocked(api.getKnowledgeCorrectionPreparation).mockResolvedValue({ ...task(), state: 'completed', attemptCount: 1, sourceId: id(10) });
    const wrapper = await render(); expect(wrapper.get('[data-testid="correction-prepare"]').attributes('disabled')).toBeDefined();
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue({ ...workspace(), sourceId: id(10), revisionId: null, headVersion: 0, isSourceView: true });
    await vi.advanceTimersByTimeAsync(1500); await flushPromises();
    expect(wrapper.find('[data-testid="correction-source-view"]').exists()).toBe(true);
    expect(api.getKnowledgeCorrectionWorkspace).toHaveBeenLastCalledWith(expect.objectContaining({ sourceId: id(10), block: 0 }), expect.any(AbortSignal));
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue(workspace());
    await wrapper.get('[data-testid="correction-current"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="correction-text"]').text()).toBe('人工校对'); expect(wrapper.find('[data-testid="correction-source-view"]').exists()).toBe(false);
  });
  it('翻页期间头版本变化时清除旧正文并要求刷新', async () => {
    const wrapper = await render(); vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue({ ...workspace(), blockIndex: 1, headVersion: 2 });
    await wrapper.get('[data-testid="correction-next"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="correction-error"]').exists()).toBe(true); expect(wrapper.find('[data-testid="correction-text"]').exists()).toBe(false);
  });
  it('准备时撤权立即清空已展示原文及来源预览，不把明确拒绝当成成功或未知回执', async () => {
    vi.mocked(api.prepareKnowledgeCorrection).mockRejectedValue({ status: 403, code: 'authorization.permission_denied' });
    const wrapper = await render(); await wrapper.get('[data-testid="correction-source"]').trigger('click');
    await wrapper.get('[data-testid="correction-prepare"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="correction-text"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="correction-original"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="correction-error"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="correction-unknown"]').exists()).toBe(false);
  });
  it('关闭或切换文档丢弃迟到正文并中止轮询', async () => {
    let resolve!: (value: ReturnType<typeof workspace>) => void;
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockReturnValue(new Promise(done => { resolve = done; }));
    const wrapper = await render(); const signal = vi.mocked(api.getKnowledgeCorrectionWorkspace).mock.calls[0]![1]!;
    await wrapper.setProps({ open: false }); resolve(workspace()); await flushPromises();
    expect(signal.aborted).toBe(true); expect(wrapper.find('[data-testid="correction-text"]').exists()).toBe(false);
  });
  it('空来源可准备；任一权限故障不得展示另一并发请求的正文', async () => {
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockRejectedValueOnce(missing); const wrapper = await render();
    expect(wrapper.text()).toContain('尚无可读工作台来源'); expect(wrapper.find('[data-testid="correction-prepare"]').exists()).toBe(true);
    vi.mocked(api.getLatestKnowledgeCorrectionPreparation).mockRejectedValue({ status: 403, code: 'authorization.permission_denied' });
    vi.mocked(api.getKnowledgeCorrectionWorkspace).mockResolvedValue(workspace());
    await wrapper.get('[data-testid="correction-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="correction-error"]').exists()).toBe(true); expect(wrapper.find('[data-testid="correction-text"]').exists()).toBe(false);
  });
});
