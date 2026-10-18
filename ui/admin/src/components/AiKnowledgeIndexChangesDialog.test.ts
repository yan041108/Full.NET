import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import Dialog from './AiKnowledgeIndexChangesDialog.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-knowledge';
import { showProblem } from '../feedback/fullNetMessage';
vi.mock('../api/ai-knowledge', () => ({ listKnowledgeIndexChanges: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn() }));
const base = '01900000-0000-7000-8000-000000000001'; const doc = '01900000-0000-7000-8000-000000000002'; const version = '01900000-0000-7000-8000-000000000003';
const item = { documentId: doc, title: '来源标题', versionId: version, versionNumber: 2, createdAtUtc: '2026-10-16T00:00:00Z', updatedAtUtc: '2026-10-16T01:00:00Z' };
const response = { items: [item], nextDocumentId: null as string | null };
const required = ['ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.versions_read', 'ai.knowledge_documents.index_read', 'ai.knowledge_documents.index_changes_read'];
let wrappers: VueWrapper[] = [];
async function render(allow: (code: string) => boolean = () => true, open = true) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  vi.spyOn(session, 'can').mockImplementation(code => allow(code) && session.currentUser?.permissions.includes(code) !== false);
  const wrapper = mount(Dialog, { props: { open, knowledgeBaseId: base }, global: { plugins: [pinia], stubs: {
    ArtFormDialog: { props: ['open'], template: '<section v-if="open"><slot/></section>' }, AiKnowledgeIndexDialog: true
  } } }); wrappers.push(wrapper); await flushPromises(); return wrapper;
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.listKnowledgeIndexChanges).mockResolvedValue(response); });
afterEach(() => { wrappers.forEach(w => w.unmount()); wrappers = []; vi.restoreAllMocks(); });
it.each(required)('缺失 %s 不读取目录、不渲染数据或索引操作', async permission => {
  const wrapper = await render(code => code !== permission);
  expect(api.listKnowledgeIndexChanges).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-changes-refresh"]').exists()).toBe(false);
  expect(wrapper.text()).not.toContain(item.title);
});
it('关闭目录不读取，打开后只读取一页，用户显式选择才打开原索引状态入口', async () => {
  const wrapper = await render(() => true, false); expect(api.listKnowledgeIndexChanges).not.toHaveBeenCalled();
  await wrapper.setProps({ open: true }); await flushPromises();
  expect(api.listKnowledgeIndexChanges).toHaveBeenCalledExactlyOnceWith({ knowledgeBaseId: base, afterDocumentId: undefined, pageSize: 20 }, expect.any(AbortSignal));
  expect(wrapper.text()).toContain('不表示索引已就绪');
  expect(wrapper.findComponent({ name: 'AiKnowledgeIndexDialog' }).exists()).toBe(false);
  await wrapper.get('[data-testid="knowledge-changes-index"]').trigger('click');
  const index = wrapper.findComponent({ name: 'AiKnowledgeIndexDialog' });
  expect(index.props()).toMatchObject({ documentId: doc, versionId: version, knowledgeBaseId: base, open: true });
});
it('前后分页绑定服务端游标，从头刷新清空历史', async () => {
  vi.mocked(api.listKnowledgeIndexChanges).mockResolvedValueOnce({ ...response, nextDocumentId: doc }).mockResolvedValue(response);
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-changes-next"]').trigger('click'); await flushPromises();
  expect(api.listKnowledgeIndexChanges).toHaveBeenLastCalledWith({ knowledgeBaseId: base, afterDocumentId: doc, pageSize: 20 }, expect.any(AbortSignal));
  expect(wrapper.text()).toContain('第 2 页');
  await wrapper.get('[data-testid="knowledge-changes-previous"]').trigger('click'); await flushPromises();
  expect(api.listKnowledgeIndexChanges).toHaveBeenLastCalledWith({ knowledgeBaseId: base, afterDocumentId: undefined, pageSize: 20 }, expect.any(AbortSignal));
});
it.each(['close', 'base', 'session', 'permission'])('范围变化 %s 中止旧请求并忽略迟到响应', async scenario => {
  let finish!: (value: typeof response) => void;
  vi.mocked(api.listKnowledgeIndexChanges).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const wrapper = await render(); const signal = vi.mocked(api.listKnowledgeIndexChanges).mock.calls[0]![1]!;
  if (scenario === 'close') await wrapper.setProps({ open: false });
  if (scenario === 'base') await wrapper.setProps({ knowledgeBaseId: version });
  if (scenario === 'session') useSessionStore().switching = true;
  if (scenario === 'permission') useSessionStore().currentUser = {
    id: base, username: 'owner', displayName: 'Owner', tenantId: null, actorScope: 'host', scope: 'host', sessionId: version,
    isSuperAdministrator: false, passwordChangeRequired: false, preferredLocale: 'zh-CN', profileVersion: 1, permissions: []
  };
  await flushPromises(); finish({ ...response, items: [{ ...item, title: '迟到旧标题' }] }); await flushPromises();
  expect(signal.aborted).toBe(true); expect(wrapper.text()).not.toContain('迟到旧标题');
});
it.each([403, 404, 409])('目录拒绝 %s 清空旧来源，后续手动刷新从头开始', async status => {
  vi.mocked(api.listKnowledgeIndexChanges).mockResolvedValueOnce({ ...response, nextDocumentId: doc }).mockRejectedValueOnce({ status }).mockResolvedValue(response);
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-changes-next"]').trigger('click'); await flushPromises();
  expect(wrapper.text()).not.toContain(item.title); expect(showProblem).toHaveBeenCalledOnce();
  await wrapper.get('[data-testid="knowledge-changes-refresh"]').trigger('click'); await flushPromises();
  expect(api.listKnowledgeIndexChanges).toHaveBeenLastCalledWith({ knowledgeBaseId: base, afterDocumentId: undefined, pageSize: 20 }, expect.any(AbortSignal));
});
it('重复游标失败关闭且不自动重试', async () => {
  vi.mocked(api.listKnowledgeIndexChanges).mockResolvedValue({ ...response, nextDocumentId: doc });
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-changes-next"]').trigger('click'); await flushPromises();
  expect(showProblem).toHaveBeenCalledOnce(); expect(api.listKnowledgeIndexChanges).toHaveBeenCalledTimes(2); expect(wrapper.text()).not.toContain(item.title);
});
