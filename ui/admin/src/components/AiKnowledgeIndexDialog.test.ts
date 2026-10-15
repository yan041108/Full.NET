import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import Dialog from './AiKnowledgeIndexDialog.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-knowledge';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
vi.mock('../api/ai-knowledge', () => ({ getKnowledgeIndexStatus: vi.fn(), getKnowledgeIndexReconciliation: vi.fn(), enqueueKnowledgeIndex: vi.fn(), retryKnowledgeIndex: vi.fn(), rebuildKnowledgeIndex: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn() }));
const id = '01900000-0000-7000-8000-000000000001'; const doc = '01900000-0000-7000-8000-000000000002'; const version = '01900000-0000-7000-8000-000000000003'; const task = '01900000-0000-7000-8000-000000000004';
const parameters = { knowledgeBaseId: id, documentId: doc, versionId: version };
const status = { versionId: version, taskId: null, state: 'pending_processing', failureCode: null };
const reconciliation = { versionId: version, taskId: task, state: 'processing', reason: 'lease_expired', recoveryAction: 'wait', documentVersion: 3, knowledgeBaseVersion: 4 };
let wrappers: VueWrapper[] = [];
async function render(allow: (code: string) => boolean = () => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allow);
  const wrapper = mount(Dialog, { props: { open: true, ...parameters }, global: { plugins: [pinia], stubs: { ArtFormDialog: { props: ['open'], template: '<section v-if="open"><slot/></section>' } } } });
  wrappers.push(wrapper); await flushPromises(); return wrapper;
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getKnowledgeIndexStatus).mockResolvedValue(status); vi.mocked(api.enqueueKnowledgeIndex).mockResolvedValue({ taskId: task, versionId: version }); vi.mocked(api.retryKnowledgeIndex).mockResolvedValue({ taskId: task, versionId: version }); vi.mocked(api.rebuildKnowledgeIndex).mockResolvedValue({ taskId: task, versionId: version }); });
afterEach(() => { wrappers.forEach(w => w.unmount()); wrappers = []; vi.restoreAllMocks(); });
it('所有者显式对账绑定精确版本并展示恢复建议，不自动请求或执行', async () => {
  vi.mocked(api.getKnowledgeIndexReconciliation).mockResolvedValue(reconciliation);
  const wrapper = await render(); expect(api.getKnowledgeIndexReconciliation).not.toHaveBeenCalled();
  await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises();
  expect(api.getKnowledgeIndexReconciliation).toHaveBeenCalledWith(parameters, expect.any(AbortSignal));
  expect(wrapper.get('[data-testid="knowledge-index-reconciliation"]').text()).toContain('租约已过期');
  expect(wrapper.get('[data-testid="knowledge-index-reconciliation"]').text()).toContain('等待');
  expect(wrapper.find('[data-testid="knowledge-index-retry"]').exists()).toBe(false);
  expect(api.retryKnowledgeIndex).not.toHaveBeenCalled();
});
it.each(['generation_unavailable', 'task_cancelled'])('重建建议 %s 展示独立重建确认，不复用入队或重试', async reason => {
  vi.mocked(api.getKnowledgeIndexReconciliation).mockResolvedValue({ ...reconciliation, reason, recoveryAction: 'rebuild' });
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises();
  expect(wrapper.get('[data-testid="knowledge-index-reconciliation"]').text()).toContain('需要创建独立索引代次');
  expect(wrapper.find('[data-testid="knowledge-index-enqueue"]').exists()).toBe(false); expect(wrapper.find('[data-testid="knowledge-index-retry"]').exists()).toBe(false);
});
it('对账重试仅绑定最新 taskId，显式提交后清除恢复建议', async () => {
  vi.mocked(api.getKnowledgeIndexReconciliation).mockResolvedValue({ ...reconciliation, reason: 'lease_exhausted', recoveryAction: 'retry' });
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises();
  await wrapper.get('[data-testid="knowledge-index-retry"]').trigger('click'); await flushPromises();
  expect(api.retryKnowledgeIndex).toHaveBeenCalledWith({ ...parameters, taskId: task }); expect(wrapper.find('[data-testid="knowledge-index-reconciliation"]').exists()).toBe(false);
});
it.each(['future_reason', 'ready'])('未知或不一致的恢复建议 %s 失败关闭', async reason => {
  vi.mocked(api.getKnowledgeIndexReconciliation).mockResolvedValue({ ...reconciliation, reason, recoveryAction: 'enqueue' });
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises();
  expect(wrapper.find('[data-testid="knowledge-index-enqueue"]').exists()).toBe(false); expect(wrapper.find('[data-testid="knowledge-index-reconciliation"]').exists()).toBe(false); expect(showProblem).toHaveBeenCalled();
});
it.each([403, 404, 409])('对账拒绝 %s 清除之前可写状态，不回退或自动重发', async code => {
  vi.mocked(api.getKnowledgeIndexReconciliation).mockRejectedValue({ status: code });
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises();
  expect(wrapper.find('[data-testid="knowledge-index-state"]').exists()).toBe(false); expect(wrapper.find('[data-testid="knowledge-index-enqueue"]').exists()).toBe(false); expect(api.enqueueKnowledgeIndex).not.toHaveBeenCalled();
});
it('对账期间版本切换丢弃迟到结果并取消原请求', async () => {
  let finish!: (value: typeof reconciliation) => void; vi.mocked(api.getKnowledgeIndexReconciliation).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click');
  await wrapper.setProps({ versionId: doc }); finish(reconciliation); await flushPromises();
  expect(wrapper.find('[data-testid="knowledge-index-reconciliation"]').exists()).toBe(false); expect(vi.mocked(api.getKnowledgeIndexReconciliation).mock.calls[0]![1]?.aborted).toBe(true);
});
it('索引读取的独立权限缺失不发起查询', async () => { const wrapper = await render(code => code !== 'ai.knowledge_documents.index_read'); expect(api.getKnowledgeIndexStatus).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-index-refresh"]').exists()).toBe(false); });
it.each([
  ['ready', 'none'], ['deleted', 'none'], ['not_indexed', 'enqueue'], ['binding_stale', 'enqueue'],
  ['queued', 'wait'], ['processing', 'wait'], ['lease_expired', 'wait'], ['cleanup_pending', 'wait'],
  ['task_failed', 'retry'], ['lease_exhausted', 'retry'], ['task_cancelled', 'rebuild'], ['generation_unavailable', 'rebuild'],
  ['source_unavailable', 'none'], ['format_unsupported', 'none'],
])('当前分类 %s/%s 只显示对应恢复操作', async (reason, recoveryAction) => {
  vi.mocked(api.getKnowledgeIndexReconciliation).mockResolvedValue({ ...reconciliation, reason, recoveryAction });
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises();
  expect(wrapper.find('[data-testid="knowledge-index-reconciliation"]').exists()).toBe(true);
  expect(wrapper.find('[data-testid="knowledge-index-enqueue"]').exists()).toBe(recoveryAction === 'enqueue');
  expect(wrapper.find('[data-testid="knowledge-index-retry"]').exists()).toBe(recoveryAction === 'retry');
});
it('对账建议入队仍要求权限，并绑定当前版本显式提交', async () => {
  vi.mocked(api.getKnowledgeIndexReconciliation).mockResolvedValue({ ...reconciliation, taskId: null, reason: 'not_indexed', recoveryAction: 'enqueue' });
  const readOnly = await render(code => code !== 'ai.knowledge_documents.upload');
  await readOnly.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises(); expect(readOnly.find('[data-testid="knowledge-index-enqueue"]').exists()).toBe(false);
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises();
  await wrapper.get('[data-testid="knowledge-index-enqueue"]').trigger('click'); await flushPromises();
  expect(api.enqueueKnowledgeIndex).toHaveBeenCalledWith(parameters); expect(api.enqueueKnowledgeIndex).toHaveBeenCalledTimes(1);
});
it('撤销重试权限时不提供对账重试操作', async () => {
  vi.mocked(api.getKnowledgeIndexReconciliation).mockResolvedValue({ ...reconciliation, reason: 'task_failed', recoveryAction: 'retry' });
  const wrapper = await render(code => code !== 'ai.knowledge_documents.index_retry'); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises();
  expect(wrapper.find('[data-testid="knowledge-index-retry"]').exists()).toBe(false);
});
it('对账冲突后的刷新仍读取对账，不降级为旧状态', async () => {
  vi.mocked(api.getKnowledgeIndexReconciliation).mockRejectedValueOnce({ status: 409 }).mockResolvedValueOnce(reconciliation);
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises();
  await wrapper.get('[data-testid="knowledge-index-refresh"]').trigger('click'); await flushPromises();
  expect(api.getKnowledgeIndexStatus).toHaveBeenCalledTimes(1); expect(api.getKnowledgeIndexReconciliation).toHaveBeenCalledTimes(2);
  expect(wrapper.get('[data-testid="knowledge-index-reconciliation"]').text()).toContain('等待');
});
it.each(['wrong-version', 'missing-task', 'invalid-snapshot', 'unknown-action'])('损坏对账绑定 %s 清除操作资格', async scenario => {
  vi.mocked(api.getKnowledgeIndexReconciliation).mockResolvedValue({ ...reconciliation, reason: 'task_failed', recoveryAction: 'retry',
    ...(scenario === 'wrong-version' ? { versionId: doc } : scenario === 'missing-task' ? { taskId: null } : scenario === 'invalid-snapshot' ? { documentVersion: 0 } : { recoveryAction: 'future_action' }) });
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises();
  expect(wrapper.find('[data-testid="knowledge-index-reconciliation"]').exists()).toBe(false); expect(wrapper.find('[data-testid="knowledge-index-retry"]').exists()).toBe(false); expect(showProblem).toHaveBeenCalled();
});
it('对账期间撤权取消请求，迟到敏感任务不进入 DOM', async () => {
  let finish!: (value: typeof reconciliation) => void; vi.mocked(api.getKnowledgeIndexReconciliation).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click');
  useSessionStore().switching = true; await flushPromises(); finish(reconciliation); await flushPromises();
  expect(wrapper.find('[data-testid="knowledge-index-reconciliation"]').exists()).toBe(false); expect(wrapper.find('[data-testid="knowledge-index-reconcile"]').exists()).toBe(false); expect(vi.mocked(api.getKnowledgeIndexReconciliation).mock.calls[0]![1]?.aborted).toBe(true);
});
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

async function prepareRebuild(allow: (code: string) => boolean = () => true) {
  vi.mocked(api.getKnowledgeIndexReconciliation).mockResolvedValue({ ...reconciliation, reason: 'generation_unavailable', recoveryAction: 'rebuild' });
  const wrapper = await render(allow); await wrapper.get('[data-testid="knowledge-index-reconcile"]').trigger('click'); await flushPromises(); return wrapper;
}
async function confirmRebuild(wrapper: VueWrapper) {
  await wrapper.get('[data-testid="knowledge-index-rebuild-confirm"] input').setValue(true); await flushPromises();
}
it('重建需独立权限及显式费用确认，提交当前对账快照', async () => {
  const denied = await prepareRebuild(code => code !== 'ai.knowledge_documents.index_rebuild');
  expect(denied.find('[data-testid="knowledge-index-rebuild"]').exists()).toBe(false);
  const wrapper = await prepareRebuild(); const button = wrapper.get('[data-testid="knowledge-index-rebuild"]');
  expect(button.attributes('disabled')).toBeDefined(); expect(api.rebuildKnowledgeIndex).not.toHaveBeenCalled();
  await confirmRebuild(wrapper); await button.trigger('click'); await flushPromises();
  expect(api.rebuildKnowledgeIndex).toHaveBeenCalledExactlyOnceWith({ ...parameters, body: {
    clientRequestId: expect.any(String), expectedTaskId: task, expectedDocumentVersion: 3, expectedKnowledgeBaseVersion: 4, confirmAdditionalCost: true,
  } }); expect(wrapper.find('[data-testid="knowledge-index-rebuild"]').exists()).toBe(false);
});
it('未知重建结果保留同一请求键，显式再次确认不生成新请求', async () => {
  vi.mocked(api.rebuildKnowledgeIndex).mockRejectedValueOnce(new Error('connection lost'));
  const wrapper = await prepareRebuild(); await confirmRebuild(wrapper);
  await wrapper.get('[data-testid="knowledge-index-rebuild"]').trigger('click'); await flushPromises();
  const first = vi.mocked(api.rebuildKnowledgeIndex).mock.calls[0]![0];
  expect(wrapper.get('[data-testid="knowledge-index-rebuild-pending"]').text()).toContain('相同请求键');
  await wrapper.get('[data-testid="knowledge-index-rebuild"]').trigger('click'); await flushPromises();
  expect(vi.mocked(api.rebuildKnowledgeIndex).mock.calls[1]![0]).toEqual(first); expect(api.rebuildKnowledgeIndex).toHaveBeenCalledTimes(2);
});
it('重建等待期间重复点击不产生第二次请求', async () => {
  let finish!: (value: { taskId: string; versionId: string }) => void;
  vi.mocked(api.rebuildKnowledgeIndex).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const wrapper = await prepareRebuild(); await confirmRebuild(wrapper);
  await wrapper.get('[data-testid="knowledge-index-rebuild"]').trigger('click');
  expect(wrapper.get('[data-testid="knowledge-index-rebuild"]').attributes('disabled')).toBeDefined();
  await wrapper.get('[data-testid="knowledge-index-rebuild"]').trigger('click'); expect(api.rebuildKnowledgeIndex).toHaveBeenCalledTimes(1);
  finish({ taskId: task, versionId: version }); await flushPromises();
});
it.each([false, true])('切换来源后丢弃迟到重建%s并清除旧确认', async failure => {
  let finish!: (value: { taskId: string; versionId: string }) => void; let fail!: (error: Error) => void;
  vi.mocked(api.rebuildKnowledgeIndex).mockImplementationOnce(() => new Promise((resolve, reject) => { finish = resolve; fail = reject; }));
  const wrapper = await prepareRebuild(); await confirmRebuild(wrapper); await wrapper.get('[data-testid="knowledge-index-rebuild"]').trigger('click');
  await wrapper.setProps({ versionId: doc }); await flushPromises(); vi.mocked(showProblem).mockClear(); vi.mocked(showSuccess).mockClear();
  if (failure) fail(new Error('late failure')); else finish({ taskId: task, versionId: version }); await flushPromises();
  expect(showProblem).not.toHaveBeenCalled(); expect(showSuccess).not.toHaveBeenCalled();
  expect(wrapper.find('[data-testid="knowledge-index-rebuild-pending"]').exists()).toBe(false);
  expect(wrapper.find('[data-testid="knowledge-index-accepted"]').exists()).toBe(false);
});
it('刷新发现新的权威任务后清除旧请求及费用确认', async () => {
  vi.mocked(api.rebuildKnowledgeIndex).mockRejectedValueOnce(new Error('unknown result'));
  const wrapper = await prepareRebuild(); await confirmRebuild(wrapper); await wrapper.get('[data-testid="knowledge-index-rebuild"]').trigger('click'); await flushPromises();
  vi.mocked(api.getKnowledgeIndexReconciliation).mockResolvedValue({ ...reconciliation, taskId: doc, reason: 'task_cancelled', recoveryAction: 'rebuild' });
  await wrapper.get('[data-testid="knowledge-index-refresh"]').trigger('click'); await flushPromises();
  expect(wrapper.find('[data-testid="knowledge-index-rebuild-pending"]').exists()).toBe(false);
  expect(wrapper.get('[data-testid="knowledge-index-rebuild"]').attributes('disabled')).toBeDefined();
});
