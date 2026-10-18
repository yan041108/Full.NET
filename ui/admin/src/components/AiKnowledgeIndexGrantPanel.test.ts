import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { ElMessageBox, type MessageBoxData } from 'element-plus';
import Panel from './AiKnowledgeIndexGrantPanel.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-index-grants';
import { getKnowledgeBase } from '../api/ai-knowledge';
vi.mock('../api/ai-index-grants', () => ({ listIndexGrants: vi.fn(), createIndexGrant: vi.fn(), revokeIndexGrant: vi.fn() }));
vi.mock('../api/ai-knowledge', () => ({ getKnowledgeBase: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn() }));
const knowledge = { id: 'knowledge', name: '本知识库', version: 2, isEnabled: true, dataClassification: 'internal', description: null, embeddingModelConfigId: 'model', embeddingModelVersion: 1, generationModelConfigId: null, generationModelVersion: null, createdAtUtc: '2026-10-17T00:00:00Z', updatedAtUtc: null };
const grant = { id: 'grant', knowledgeBaseId: 'knowledge', knowledgeBaseVersion: 2, expiresAtUtc: '2026-10-17T01:00:00Z', documentLimit: 4, admittedDocuments: 1, inputTokenLimitPerDocument: 100000, isRevoked: false, version: 2 };
const wrappers: VueWrapper[] = [];
function render(allow = (_: string) => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allow);
  const wrapper = mount(Panel, { props: { knowledgeBaseId: 'knowledge' }, global: { plugins: [pinia], stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); wrappers.push(wrapper); return wrapper;
}
async function open(w: VueWrapper) { await w.get('[data-testid="index-grants-open"]').trigger('click'); await flushPromises(); }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(getKnowledgeBase).mockResolvedValue(knowledge); vi.mocked(api.listIndexGrants).mockResolvedValue([grant]);
  vi.mocked(api.createIndexGrant).mockResolvedValue(grant); vi.mocked(api.revokeIndexGrant).mockResolvedValue({ ...grant, isRevoked: true, version: 3 });
  vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm' as MessageBoxData); });
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks(); });
it('无读取权限不创建入口或请求', () => {
  const w = render(code => code !== 'ai.knowledge_documents.index_grant_read');
  expect(w.find('[data-testid="index-grants-open"]').exists()).toBe(false); expect(api.listIndexGrants).not.toHaveBeenCalled();
});
it('授权收费必须明确确认，使用当前知识库修订和有限额度', async () => {
  const w = render(); await open(w); await w.get('[data-testid="index-grants-create"]').trigger('click'); await flushPromises();
  expect(ElMessageBox.confirm).toHaveBeenCalled();
  expect(api.createIndexGrant).toHaveBeenCalledWith({ knowledgeBaseId: 'knowledge', body: expect.objectContaining({ knowledgeBaseVersion: 2,
    documentLimit: 4, inputTokenLimitPerDocument: 100000, confirmPaidDispatch: true, clientRequestId: expect.any(String), expiresAtUtc: expect.any(String) }) }, expect.any(AbortSignal));
});
it('创建与撤销权限独立，取消确认不派发', async () => {
  const w = render(code => code !== 'ai.knowledge_documents.index_grant_create'); await open(w);
  expect(w.find('[data-testid="index-grants-create"]').exists()).toBe(false);
  vi.mocked(ElMessageBox.confirm).mockRejectedValueOnce('cancel');
  await w.get('[data-testid="index-grants-revoke"]').trigger('click'); await flushPromises(); expect(api.revokeIndexGrant).not.toHaveBeenCalled();
});
it('原上下文切换关闭确认，不能向新上下文提交旧授权', async () => {
  let confirm!: (value: MessageBoxData) => void; vi.mocked(ElMessageBox.confirm).mockReturnValue(new Promise(r => { confirm = r; }));
  const w = render(); await open(w); await w.get('[data-testid="index-grants-create"]').trigger('click');
  useSessionStore().switching = true; await flushPromises(); confirm('confirm' as MessageBoxData); await flushPromises();
  expect(api.createIndexGrant).not.toHaveBeenCalled();
});
