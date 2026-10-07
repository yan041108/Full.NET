import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElSelect } from 'element-plus';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import Dialog from './AiKnowledgeGrantsDialog.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-knowledge';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
vi.mock('../api/ai-knowledge');
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn() }));
const id = '01900000-0000-7000-8000-000000000001'; const doc = '01900000-0000-7000-8000-000000000002'; const member = '01900000-0000-7000-8000-000000000003';
const dialog = { props: ['open', 'showConfirm', 'confirmTestId'], emits: ['confirm', 'update:open'], template: '<section v-if="open"><slot/><button v-if="showConfirm" :data-testid="confirmTestId" @click="$emit(\'confirm\')">save</button></section>' };
let wrappers: VueWrapper[] = [];
async function render(documentId?: string, allow: (code: string) => boolean = () => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allow);
  const wrapper = mount(Dialog, { props: { open: true, knowledgeBaseId: id, documentId }, global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } });
  wrappers.push(wrapper); await flushPromises(); return wrapper;
}
async function kind(wrapper: VueWrapper, value: string) { wrapper.findAllComponents(ElSelect)[0]!.vm.$emit('update:modelValue', value); await flushPromises(); }
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getKnowledgeMembers).mockResolvedValue({ knowledgeBaseId: id, userIds: [member], version: 7 });
  vi.mocked(api.getKnowledgeDocumentMembers).mockResolvedValue({ documentId: doc, userIds: [member], version: 8 });
  vi.mocked(api.getKnowledgeRoles).mockResolvedValue({ resourceId: id, roleIds: [member], version: 9 });
  vi.mocked(api.getKnowledgeDocumentRoles).mockResolvedValue({ resourceId: doc, roleIds: [member], version: 10 });
  vi.mocked(api.getKnowledgeOrganizations).mockResolvedValue({ resourceId: id, organizationUnitIds: [member], version: 11 });
  vi.mocked(api.getKnowledgeDocumentOrganizations).mockResolvedValue({ resourceId: doc, organizationUnitIds: [member], version: 12 });
  for (const fn of [api.listKnowledgeRoleCandidates, api.listKnowledgeDocumentRoleCandidates, api.listKnowledgeOrganizationCandidates, api.listKnowledgeDocumentOrganizationCandidates]) vi.mocked(fn).mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0 });
});
afterEach(() => { wrappers.forEach(w => w.unmount()); wrappers = []; vi.restoreAllMocks(); });
it('只有写入权不能读取授权集合', async () => {
  const wrapper = await render(undefined, code => !code.endsWith('_read')); expect(api.getKnowledgeMembers).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-grants-save"]').exists()).toBe(false);
});
it('只读角色权限选择角色并隔离候选权限', async () => {
  const wrapper = await render(doc, code => ['ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.roles_read'].includes(code));
  expect(api.getKnowledgeDocumentRoles).toHaveBeenCalledWith({ knowledgeBaseId: id, documentId: doc }, expect.any(AbortSignal));
  expect(api.getKnowledgeDocumentMembers).not.toHaveBeenCalled(); expect(api.listKnowledgeDocumentRoleCandidates).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-grants-save"]').exists()).toBe(false);
});
it('文档成员全量替换携带文档版本', async () => {
  const wrapper = await render(doc); await wrapper.get('[data-testid="knowledge-grants-save"]').trigger('click'); await flushPromises();
  expect(api.setKnowledgeDocumentMembers).toHaveBeenCalledWith({ knowledgeBaseId: id, documentId: doc, body: { userIds: [member], version: 8 } }); expect(api.setKnowledgeMembers).not.toHaveBeenCalled();
});
it.each(['roles', 'organizations'])('知识库 %s 独立读取版本并全量替换', async value => {
  const wrapper = await render(); await kind(wrapper, value); await wrapper.get('[data-testid="knowledge-grants-save"]').trigger('click'); await flushPromises();
  if (value === 'roles') expect(api.setKnowledgeRoles).toHaveBeenCalledWith({ knowledgeBaseId: id, body: { roleIds: [member], version: 9 } });
  else expect(api.setKnowledgeOrganizations).toHaveBeenCalledWith({ knowledgeBaseId: id, body: { organizationUnitIds: [member], version: 11 } });
});
it.each(['roles', 'organizations'])('文档 %s 独立读取版本并全量替换', async value => {
  const wrapper = await render(doc); await kind(wrapper, value); await wrapper.get('[data-testid="knowledge-grants-save"]').trigger('click'); await flushPromises();
  if (value === 'roles') expect(api.setKnowledgeDocumentRoles).toHaveBeenCalledWith({ knowledgeBaseId: id, documentId: doc, body: { roleIds: [member], version: 10 } });
  else expect(api.setKnowledgeDocumentOrganizations).toHaveBeenCalledWith({ knowledgeBaseId: id, documentId: doc, body: { organizationUnitIds: [member], version: 12 } });
});
it('空集合允许显式撤销当前类型全部授权', async () => {
  const wrapper = await render(); await wrapper.get('textarea[data-testid="knowledge-user-ids"]').setValue(''); await wrapper.get('[data-testid="knowledge-grants-save"]').trigger('click'); await flushPromises();
  expect(api.setKnowledgeMembers).toHaveBeenCalledWith({ knowledgeBaseId: id, body: { userIds: [], version: 7 } });
});
it('不匹配的授权资源响应不开放保存或候选读取', async () => {
  vi.mocked(api.getKnowledgeRoles).mockResolvedValue({ resourceId: doc, roleIds: [member], version: 9 }); const wrapper = await render(); await kind(wrapper, 'roles');
  expect(wrapper.find('[data-testid="knowledge-grants-save"]').exists()).toBe(false); expect(api.listKnowledgeRoleCandidates).not.toHaveBeenCalled();
});
it.each(['invalid', `${member}\n${member.toUpperCase()}`, Array.from({ length: 101 }, (_, i) => `01900000-0000-7000-8000-${String(i + 1).padStart(12, '0')}`).join('\n')])('无效、重复或超限用户集合不提交', async value => {
  const wrapper = await render(); await wrapper.get('textarea[data-testid="knowledge-user-ids"]').setValue(value); await wrapper.get('[data-testid="knowledge-grants-save"]').trigger('click'); await flushPromises();
  expect(api.setKnowledgeMembers).not.toHaveBeenCalled(); expect(showWarning).toHaveBeenCalled();
});
it('授权读取失败不请求候选也不开放保存', async () => {
  vi.mocked(api.getKnowledgeRoles).mockRejectedValue(new Error('denied')); const wrapper = await render(); await kind(wrapper, 'roles');
  expect(api.listKnowledgeRoleCandidates).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-grants-save"]').exists()).toBe(false); expect(showProblem).toHaveBeenCalled();
});
it('候选分页始终绑定当前文档', async () => {
  vi.mocked(api.listKnowledgeDocumentRoleCandidates).mockResolvedValueOnce({ items: [{ id: member, code: 'role', name: '角色' }], page: 1, pageSize: 100, total: 101 });
  const wrapper = await render(doc); await kind(wrapper, 'roles'); await wrapper.get('[data-testid="knowledge-candidates-more"]').trigger('click'); await flushPromises();
  expect(api.listKnowledgeDocumentRoleCandidates).toHaveBeenLastCalledWith({ knowledgeBaseId: id, documentId: doc, page: 2, pageSize: 100 }, expect.any(AbortSignal));
});
it('类别切换丢弃旧成员响应，不复用旧版本', async () => {
  let finish!: (value: { knowledgeBaseId: string; userIds: string[]; version: number }) => void;
  vi.mocked(api.getKnowledgeMembers).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const wrapper = await render(); await kind(wrapper, 'roles'); finish({ knowledgeBaseId: id, userIds: [], version: 777 }); await flushPromises();
  await wrapper.get('[data-testid="knowledge-grants-save"]').trigger('click'); await flushPromises(); expect(api.setKnowledgeRoles).toHaveBeenCalledWith(expect.objectContaining({ body: expect.objectContaining({ version: 9 }) }));
});
it('保存防重复，冲突不自动刷新并覆盖', async () => {
  let reject!: (reason: unknown) => void; vi.mocked(api.setKnowledgeMembers).mockImplementation(() => new Promise((_, fail) => { reject = fail; }));
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-grants-save"]').trigger('click'); await wrapper.get('[data-testid="knowledge-grants-save"]').trigger('click');
  expect(api.setKnowledgeMembers).toHaveBeenCalledTimes(1); reject({ status: 409 }); await flushPromises(); expect(showProblem).toHaveBeenCalled(); expect(api.getKnowledgeMembers).toHaveBeenCalledTimes(1);
});
it('作用域切换丢弃迟到写入成功', async () => {
  let finish!: () => void; vi.mocked(api.setKnowledgeMembers).mockImplementation(() => new Promise(resolve => { finish = () => resolve({ knowledgeBaseId: id, userIds: [], version: 8 }); }));
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-grants-save"]').trigger('click'); useSessionStore().switching = true; await flushPromises(); finish(); await flushPromises(); expect(showSuccess).not.toHaveBeenCalled(); expect(wrapper.emitted('saved')).toBeUndefined();
});
