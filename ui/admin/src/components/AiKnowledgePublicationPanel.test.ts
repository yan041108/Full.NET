import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElMessageBox, type MessageBoxData } from 'element-plus';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import type { AiKnowledgeDirectoryResponse, AiKnowledgePublicationResponse } from '@fullnet/client-contracts';
import Panel from './AiKnowledgePublicationPanel.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-knowledge-governance';

vi.mock('../api/ai-knowledge-governance');
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const document: AiKnowledgeDirectoryResponse = { id: id(2), title: '原来源', folderId: null, tags: [], responsibleUserId: null,
  version: 0, documentVersion: 1, publicationState: 'draft', publicationId: null, headVersion: 0, candidateVersionId: id(3), pendingPublicationId: id(4) };
const proposal: AiKnowledgePublicationResponse = { id: id(4), documentId: id(2), versionId: id(3), generationId: id(5), stateKey: 'approved',
  snapshotHash: 'A'.repeat(64), policyVersion: 1, expectedHeadVersion: 0, version: 2, effectiveFromUtc: '2026-10-28T00:00:00Z', effectiveToUtc: '2099-01-01T00:00:00Z',
  actorUserId: id(6), reviewerUserId: id(7), createdAtUtc: '2026-10-28T00:00:00Z', reviewedAtUtc: '2026-10-28T00:00:00Z' };
const wrappers: VueWrapper[] = [];
function render() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  session.currentUser = { id: id(6), sessionId: id(8) } as NonNullable<typeof session.currentUser>;
  vi.spyOn(session, 'can').mockReturnValue(true);
  const wrapper = mount(Panel, { props: { open: true, knowledgeBaseId: id(1), document }, global: { plugins: [pinia], stubs: {
    ArtFormDialog: { props: ['open'], template: '<section v-if="open"><slot/></section>' }, AiKnowledgeSourceDialog: true
  } } }); wrappers.push(wrapper); return { wrapper, session };
}
beforeEach(() => {
  vi.clearAllMocks(); vi.mocked(api.readPublicationHead).mockResolvedValue({ documentId: id(2), publicationId: null, version: 0 });
  vi.mocked(api.readPublication).mockResolvedValue(proposal);
  vi.spyOn(ElMessageBox, 'close').mockImplementation(() => {});
});
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });
it.each(['context', 'session'])('%s 改变取消已打开的确认，不派发旧主体决定', async kind => {
  let finish!: (value: MessageBoxData) => void;
  vi.spyOn(ElMessageBox, 'confirm').mockReturnValue(new Promise(resolve => { finish = resolve; }));
  const { wrapper, session } = render(); await flushPromises();
  await wrapper.get('input[type="checkbox"]').setValue(true); await wrapper.get('[data-testid="knowledge-publication-publish"]').trigger('click');
  expect(ElMessageBox.confirm).toHaveBeenCalled();
  if (kind === 'context') session.currentContextName = '新租户';
  else session.currentUser = { ...session.currentUser!, sessionId: id(9) };
  await flushPromises(); expect(ElMessageBox.close).toHaveBeenCalled();
  finish('confirm' as MessageBoxData); await flushPromises(); expect(api.publishPublication).not.toHaveBeenCalled();
});
it.each(['context', 'session'])('%s 改变后迟到提案不能覆盖新作用域结果', async kind => {
  let finish!: (value: AiKnowledgePublicationResponse) => void;
  vi.mocked(api.readPublication).mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
  const { wrapper, session } = render(); await flushPromises();
  const firstSignal = vi.mocked(api.readPublication).mock.calls[0]![2]!;
  vi.mocked(api.readPublication).mockResolvedValue({ ...proposal, snapshotHash: 'B'.repeat(64), stateKey: 'rejected' });
  if (kind === 'context') session.currentContextName = '新租户';
  else session.currentUser = { ...session.currentUser!, sessionId: id(9) };
  await flushPromises(); expect(firstSignal.aborted).toBe(true); expect(wrapper.text()).toContain('B'.repeat(64));
  finish(proposal); await flushPromises();
  expect(wrapper.text()).not.toContain('A'.repeat(64)); expect(wrapper.text()).toContain('B'.repeat(64));
  expect(wrapper.find('[data-testid="knowledge-publication-publish"]').exists()).toBe(false);
});
