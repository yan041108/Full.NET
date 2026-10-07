import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import Dialog from './AiKnowledgeIndexDialog.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-knowledge';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
vi.mock('../api/ai-knowledge', () => ({ getKnowledgeIndexStatus: vi.fn(), enqueueKnowledgeIndex: vi.fn(), retryKnowledgeIndex: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn() }));
const id = '01900000-0000-7000-8000-000000000001'; const doc = '01900000-0000-7000-8000-000000000002'; const version = '01900000-0000-7000-8000-000000000003'; const task = '01900000-0000-7000-8000-000000000004';
const parameters = { knowledgeBaseId: id, documentId: doc, versionId: version };
const status = { versionId: version, taskId: null, state: 'pending_processing', failureCode: null };
let wrappers: VueWrapper[] = [];
async function render(allow: (code: string) => boolean = () => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allow);
  const wrapper = mount(Dialog, { props: { open: true, ...parameters }, global: { plugins: [pinia], stubs: { ArtFormDialog: { props: ['open'], template: '<section v-if="open"><slot/></section>' } } } });
  wrappers.push(wrapper); await flushPromises(); return wrapper;
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getKnowledgeIndexStatus).mockResolvedValue(status); vi.mocked(api.enqueueKnowledgeIndex).mockResolvedValue({ taskId: task, versionId: version }); vi.mocked(api.retryKnowledgeIndex).mockResolvedValue({ taskId: task, versionId: version }); });
afterEach(() => { wrappers.forEach(w => w.unmount()); wrappers = []; vi.restoreAllMocks(); });
it('索引读取的独立权限缺失不发起查询', async () => { const wrapper = await render(code => code !== 'ai.knowledge_documents.index_read'); expect(api.getKnowledgeIndexStatus).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-index-refresh"]').exists()).toBe(false); });
it('只读状态不隐式允许入队或重试', async () => {
  const wrapper = await render(code => ['ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.index_read'].includes(code));
  expect(api.getKnowledgeIndexStatus).toHaveBeenCalledWith(parameters, expect.any(AbortSignal)); expect(wrapper.find('[data-testid="knowledge-index-enqueue"]').exists()).toBe(false); expect(wrapper.find('[data-testid="knowledge-index-retry"]').exists()).toBe(false);
});
it('入队需要上传和来源读取权限', async () => { const wrapper = await render(code => code !== 'ai.knowledge_documents.upload'); expect(wrapper.find('[data-testid="knowledge-index-enqueue"]').exists()).toBe(false); });
it('入队捕获精确版本，202 只表示接受而不宣称 ready', async () => {
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-enqueue"]').trigger('click'); await flushPromises(); expect(api.enqueueKnowledgeIndex).toHaveBeenCalledWith(parameters);
  expect(wrapper.find('[data-testid="knowledge-index-accepted"]').text()).toContain(task); expect(wrapper.find('[data-testid="knowledge-index-state"]').exists()).toBe(false);
});
it('失败任务显式重试绑定最新读取 taskId，冲突不自动重发', async () => {
  vi.mocked(api.getKnowledgeIndexStatus).mockResolvedValue({ ...status, taskId: task, state: 'failed', failureCode: 'ai.knowledge.index_unavailable' }); vi.mocked(api.retryKnowledgeIndex).mockRejectedValue({ status: 409 });
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-retry"]').trigger('click'); await flushPromises();
  expect(api.retryKnowledgeIndex).toHaveBeenCalledWith({ ...parameters, taskId: task }); expect(api.retryKnowledgeIndex).toHaveBeenCalledTimes(1); expect(showProblem).toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-index-retry"]').exists()).toBe(false);
});
it('读取被撤权或版本不匹配时不能写入', async () => {
  vi.mocked(api.getKnowledgeIndexStatus).mockResolvedValue({ ...status, versionId: doc }); const wrapper = await render(); expect(wrapper.find('[data-testid="knowledge-index-enqueue"]').exists()).toBe(false);
  vi.mocked(api.getKnowledgeIndexStatus).mockRejectedValue({ status: 403 }); await wrapper.get('[data-testid="knowledge-index-refresh"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="knowledge-index-state"]').exists()).toBe(false);
});
it('结果不明时禁止重复提交，显式刷新后才允许下一次操作', async () => {
  let reject!: (reason: unknown) => void; vi.mocked(api.enqueueKnowledgeIndex).mockImplementationOnce(() => new Promise((_, fail) => { reject = fail; }));
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-enqueue"]').trigger('click'); expect(api.enqueueKnowledgeIndex).toHaveBeenCalledTimes(1);
  reject(new Error('network')); await flushPromises(); expect(wrapper.find('[data-testid="knowledge-index-enqueue"]').exists()).toBe(false);
  await wrapper.get('[data-testid="knowledge-index-refresh"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="knowledge-index-enqueue"]').exists()).toBe(true);
});
it('关闭或资源切换丢弃迟到状态', async () => {
  let finish!: (value: typeof status) => void; vi.mocked(api.getKnowledgeIndexStatus).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const wrapper = await render(); await wrapper.setProps({ open: false }); finish(status); await flushPromises(); expect(wrapper.find('[data-testid="knowledge-index-state"]').exists()).toBe(false); expect(vi.mocked(api.getKnowledgeIndexStatus).mock.calls[0]![1]?.aborted).toBe(true);
});
it('会话切换后的迟到入队成功不反馈到新作用域', async () => {
  let finish!: (value: { taskId: string; versionId: string }) => void; vi.mocked(api.enqueueKnowledgeIndex).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-enqueue"]').trigger('click'); useSessionStore().switching = true; await flushPromises(); finish({ taskId: task, versionId: version }); await flushPromises(); expect(showSuccess).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-index-accepted"]').exists()).toBe(false);
});
