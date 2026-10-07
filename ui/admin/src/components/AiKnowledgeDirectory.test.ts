import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import AiKnowledgeDirectory from './AiKnowledgeDirectory.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-knowledge';
import { showProblem } from '../feedback/fullNetMessage';
vi.mock('../api/ai-knowledge', () => ({ listKnowledgeBases: vi.fn(), createKnowledgeBase: vi.fn(), updateKnowledgeBase: vi.fn(), listKnowledgeDocuments: vi.fn(), getKnowledgeDocument: vi.fn(), createKnowledgeDocument: vi.fn(), updateKnowledgeDocument: vi.fn(), deleteKnowledgeDocument: vi.fn(), listKnowledgeVersions: vi.fn(), uploadKnowledgeVersion: vi.fn(), getKnowledgeImport: vi.fn(), retryKnowledgeImport: vi.fn() }));
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
  const wrapper = mount(AiKnowledgeDirectory, { props: { documents }, global: { plugins: [pinia, router], stubs: { ArtFormDialog: dialog, AiKnowledgeSourceDialog: true } } });
  wrappers.push(wrapper); await flushPromises(); return wrapper;
}
async function selectUpload(wrapper: VueWrapper) {
  await wrapper.get('[data-testid="knowledge-versions"]').trigger('click'); await flushPromises();
  const input = wrapper.get('[data-testid="knowledge-file"]');
  Object.defineProperty(input.element, 'files', { value: [new File(['hello'], 'hello.txt', { type: 'text/plain' })], configurable: true });
  await input.trigger('change'); await wrapper.get('[data-testid="knowledge-upload"]').trigger('click'); await flushPromises();
}
beforeEach(() => {
  vi.clearAllMocks(); vi.mocked(api.listKnowledgeBases).mockResolvedValue(page([base])); vi.mocked(api.listKnowledgeDocuments).mockResolvedValue(page([doc]));
  vi.mocked(api.listKnowledgeVersions).mockResolvedValue(page([])); vi.mocked(api.getKnowledgeDocument).mockResolvedValue(doc);
});
afterEach(() => { wrappers.forEach(wrapper => wrapper.unmount()); wrappers = []; vi.restoreAllMocks(); });
describe('知识目录与文件交付', () => {
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
