import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import AiKnowledgeDirectory from './AiKnowledgeDirectory.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-knowledge';
import { showProblem, showWarning } from '../feedback/fullNetMessage';
vi.mock('../api/ai-knowledge', () => ({ recoverKnowledgeIndexChanges: vi.fn(), listKnowledgeBases: vi.fn(), createKnowledgeBase: vi.fn(), updateKnowledgeBase: vi.fn(), listKnowledgeDocuments: vi.fn(), getKnowledgeDocument: vi.fn(), createKnowledgeDocument: vi.fn(), updateKnowledgeDocument: vi.fn(), deleteKnowledgeDocument: vi.fn(), listKnowledgeVersions: vi.fn(), uploadKnowledgeVersion: vi.fn(), getKnowledgeImport: vi.fn(), retryKnowledgeImport: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn() }));
const baseId = '01900000-0000-7000-8000-000000000001'; const docId = '01900000-0000-7000-8000-000000000002';
const base = { id: baseId, name: '获授权目录', description: null, isEnabled: true, dataClassification: 'internal', embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: null, generationModelVersion: null, createdAtUtc: 'now', updatedAtUtc: null, version: 7 };
const doc = { id: docId, knowledgeBaseId: baseId, title: '文档', description: null, status: 'draft', createdAtUtc: 'now', updatedAtUtc: null, version: 4 };
const page = <T>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length });
const dialog = { props: ['open', 'confirmTestId'], emits: ['confirm'], template: '<section v-if="open"><slot/><button :data-testid="confirmTestId" @click="$emit(\'confirm\')">save</button></section>' };
let wrappers: VueWrapper[] = [];
async function render(documents = false, allow: (code: string) => boolean = () => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allow);
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/ai/knowledge-documents', name: 'ai-knowledge-documents', component: { template: '<div />' } }, { path: '/', component: { template: '<div />' } }] });
  await router.push({ path: '/', query: documents ? { knowledgeBaseId: baseId } : {} }); await router.isReady();
  const wrapper = mount(AiKnowledgeDirectory, { props: { documents }, global: { plugins: [pinia, router], stubs: { ArtFormDialog: dialog, AiKnowledgeSourceDialog: true, AiKnowledgePolicyDialog: true, AiKnowledgeGrantsDialog: true, AiKnowledgeIndexDialog: true } } });
  wrappers.push(wrapper); await flushPromises(); return wrapper;
}
async function selectUpload(wrapper: VueWrapper, file = new File(['hello'], 'hello.txt', { type: 'text/plain' })) {
  await wrapper.get('[data-testid="knowledge-versions"]').trigger('click'); await flushPromises();
  const input = wrapper.get('[data-testid="knowledge-file"]');
  Object.defineProperty(input.element, 'files', { value: [file], configurable: true });
  await input.trigger('change'); await wrapper.get('[data-testid="knowledge-upload"]').trigger('click'); await flushPromises();
}
beforeEach(() => {
  vi.clearAllMocks(); vi.mocked(api.listKnowledgeBases).mockResolvedValue(page([base])); vi.mocked(api.listKnowledgeDocuments).mockResolvedValue(page([doc]));
  vi.mocked(api.listKnowledgeVersions).mockResolvedValue(page([])); vi.mocked(api.getKnowledgeDocument).mockResolvedValue(doc);
});
afterEach(() => { wrappers.forEach(wrapper => wrapper.unmount()); wrappers = []; vi.restoreAllMocks(); });
describe('知识目录与文件交付', () => {
  it('恢复必须具备独立操作和索引读取权限', async () => {
    const wrapper = await render(true, code => code !== 'ai.knowledge_documents.index_recover');
    expect(wrapper.find('[data-testid="knowledge-recover"]').exists()).toBe(false);
    expect(api.recoverKnowledgeIndexChanges).not.toHaveBeenCalled();
  });
  it('恢复一次只请求一页，下一批使用服务端返回游标', async () => {
    vi.mocked(api.recoverKnowledgeIndexChanges).mockResolvedValueOnce({ scanned: 50, updated: 3, nextDocumentId: docId })
      .mockResolvedValueOnce({ scanned: 1, updated: 0, nextDocumentId: null });
    const wrapper = await render(true);
    await wrapper.get('[data-testid="knowledge-recover"]').trigger('click'); await flushPromises();
    expect(api.recoverKnowledgeIndexChanges).toHaveBeenCalledTimes(1);
    expect(wrapper.get('[data-testid="knowledge-recover"]').text()).toContain('继续');
    await wrapper.get('[data-testid="knowledge-recover"]').trigger('click'); await flushPromises();
    expect(api.recoverKnowledgeIndexChanges).toHaveBeenLastCalledWith({ knowledgeBaseId: baseId, body: { afterDocumentId: docId, pageSize: 50 } }, expect.any(AbortSignal));
    expect(wrapper.get('[data-testid="knowledge-recover"]').text()).toContain('恢复增量意图');
  });
  it('恢复失败后清空游标，从头幂等重扫', async () => {
    vi.mocked(api.recoverKnowledgeIndexChanges).mockResolvedValueOnce({ scanned: 50, updated: 3, nextDocumentId: docId })
      .mockRejectedValueOnce(new Error('failed')).mockResolvedValueOnce({ scanned: 1, updated: 0, nextDocumentId: null });
    const wrapper = await render(true);
    for (let i = 0; i < 3; i++) { await wrapper.get('[data-testid="knowledge-recover"]').trigger('click'); await flushPromises(); }
    expect(api.recoverKnowledgeIndexChanges).toHaveBeenLastCalledWith({ knowledgeBaseId: baseId, body: { afterDocumentId: null, pageSize: 50 } }, expect.any(AbortSignal));
    expect(showProblem).toHaveBeenCalledTimes(1);
  });
  it('切换知识库后忽略旧恢复响应并清空游标', async () => {
    let finish!: (value: { scanned: number; updated: number; nextDocumentId: string | null }) => void;
    vi.mocked(api.recoverKnowledgeIndexChanges).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const wrapper = await render(true);
    await wrapper.get('[data-testid="knowledge-recover"]').trigger('click');
    await wrapper.vm.$router.push({ path: '/', query: { knowledgeBaseId: docId } }); await flushPromises();
    finish({ scanned: 50, updated: 50, nextDocumentId: baseId }); await flushPromises();
    expect(wrapper.get('[data-testid="knowledge-recover"]').text()).toContain('恢复增量意图');
    expect(wrapper.text()).not.toContain('本批来源意图已复核');
  });
  it('DOCX 在 5 MiB 边界使用规范 MIME 上传', async () => {
    vi.mocked(api.uploadKnowledgeVersion).mockRejectedValue(new Error('network'));
    const wrapper = await render(true);
    await selectUpload(wrapper, new File([new Uint8Array(5 * 1024 * 1024)], 'source.docx', { type: '' }));
    expect(api.uploadKnowledgeVersion).toHaveBeenCalledWith(expect.objectContaining({ contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' }));
  });
  it('DOCX 超过 5 MiB 时拒绝上传并显示准确限制', async () => {
    const wrapper = await render(true);
    await selectUpload(wrapper, new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'source.docx', { type: '' }));
    expect(api.uploadKnowledgeVersion).not.toHaveBeenCalled();
    expect(showWarning).toHaveBeenCalledWith(expect.stringContaining('5 MiB'));
  });
  it('版本索引读取权提供索引入口，不隐式提供原文读取', async () => {
    vi.mocked(api.listKnowledgeVersions).mockResolvedValue(page([{ id: docId, documentId: docId, versionNumber: 1, originalFileName: 'test.txt', contentType: 'text/plain', contentHash: 'hash', sizeBytes: 4, processingStatus: 'completed', createdAtUtc: 'now' }]));
    const wrapper = await render(true, code => ['ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.versions_read', 'ai.knowledge_documents.index_read'].includes(code));
    await wrapper.get('[data-testid="knowledge-versions"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="knowledge-index"]').exists()).toBe(true); expect(wrapper.find('[data-testid="knowledge-source"]').exists()).toBe(false);
  });
  it('知识库的独立策略权限提供处理策略入口', async () => {
    const wrapper = await render(false, code => ['ai.knowledge_bases.read', 'ai.knowledge_bases.policy_update'].includes(code));
    expect(wrapper.find('[data-testid="knowledge-policy"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="knowledge-edit"]').exists()).toBe(false);
  });
  it('文档角色读取权提供授权入口，不隐式授予编辑或成员读取', async () => {
    const wrapper = await render(true, code => ['ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.roles_read'].includes(code));
    expect(wrapper.find('[data-testid="knowledge-grants"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="knowledge-edit"]').exists()).toBe(false);
  });
  it('只有授权写入权限不显示读取入口', async () => {
    const wrapper = await render(false, code => ['ai.knowledge_bases.read', 'ai.knowledge_bases.members_update'].includes(code));
    expect(wrapper.find('[data-testid="knowledge-grants"]').exists()).toBe(false);
  });
  it('没有目录读取权时不请求数据或创建入口', async () => {
    const wrapper = await render(false, () => false); expect(api.listKnowledgeBases).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-create"]').exists()).toBe(false);
  });
  it('目录读取不隐式授予文档读取', async () => {
    const wrapper = await render(true, code => code === 'ai.knowledge_bases.read'); expect(api.listKnowledgeDocuments).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-versions"]').exists()).toBe(false);
  });
  it('编辑提交原始版本，冲突不自动覆盖', async () => {
    vi.mocked(api.updateKnowledgeBase).mockRejectedValue({ status: 409, title: '版本冲突', code: 'conflict', type: 'about:blank' });
    const wrapper = await render(); await wrapper.get('[data-testid="knowledge-edit"]').trigger('click');
    await wrapper.get('[data-testid="knowledge-name"]').setValue('新名称'); await wrapper.get('[data-testid="knowledge-save"]').trigger('click'); await flushPromises();
    expect(api.updateKnowledgeBase).toHaveBeenCalledWith({ knowledgeBaseId: baseId, body: { name: '新名称', description: null, isEnabled: true, version: 7 } }); expect(showProblem).toHaveBeenCalledWith(expect.objectContaining({ status: 409 }), expect.any(String));
  });
  it('保存过程中阻止重复提交', async () => {
    let finish!: (value: typeof base) => void; vi.mocked(api.createKnowledgeBase).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    const wrapper = await render(); await wrapper.get('[data-testid="knowledge-create"]').trigger('click'); await wrapper.get('[data-testid="knowledge-name"]').setValue('new');
    await wrapper.get('[data-testid="knowledge-save"]').trigger('click'); await wrapper.get('[data-testid="knowledge-save"]').trigger('click'); expect(api.createKnowledgeBase).toHaveBeenCalledTimes(1); finish(base); await flushPromises();
  });
  it('未知上传结果的显式重试复用请求键与版本', async () => {
    vi.mocked(api.uploadKnowledgeVersion).mockRejectedValue(new Error('network')); const wrapper = await render(true); await selectUpload(wrapper);
    await wrapper.get('[data-testid="knowledge-upload"]').trigger('click'); await flushPromises();
    const calls = vi.mocked(api.uploadKnowledgeVersion).mock.calls; expect(calls).toHaveLength(2); expect(calls[0]![0].requestKey).toBe(calls[1]![0].requestKey); expect(calls[1]![0].version).toBe(4);
  });
  it('成功上传显示服务器导入状态和失败原因', async () => {
    vi.mocked(api.uploadKnowledgeVersion).mockResolvedValue({ id: baseId, documentId: docId, versionId: docId, state: 'FailedBeforeCommit', failureCode: 'ai.file_unavailable', createdAtUtc: 'now', updatedAtUtc: 'now' });
    const wrapper = await render(true); await selectUpload(wrapper); expect(wrapper.text()).toContain('FailedBeforeCommit'); expect(wrapper.text()).toContain('ai.file_unavailable');
  });
  it('切换作用域时丢弃迟到目录响应', async () => {
    let finish!: (value: ReturnType<typeof page<typeof base>>) => void; vi.mocked(api.listKnowledgeBases).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const wrapper = await render(); useSessionStore().switching = true; await flushPromises(); finish(page([base])); await flushPromises(); expect(wrapper.text()).not.toContain('获授权目录');
  });
});
