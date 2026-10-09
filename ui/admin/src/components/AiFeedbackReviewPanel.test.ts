import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { ElMessageBox, type MessageBoxData } from 'element-plus';
import Panel from './AiFeedbackReviewPanel.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import * as api from '../api/ai-answer-feedback-review';
vi.mock('../api/ai-answer-feedback-review', () => ({ getAnswerFeedbackReviews: vi.fn(), reviewAnswerFeedback: vi.fn(), exportAnswerFeedbackLabels: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn() }));
const id = '00000000-0000-0000-0000-000000000001';
const row = { feedbackId: id, ratingKey: 'helpful', reasonKey: 'accurate', citationCount: 1, feedbackVersion: 2, reviewStateKey: 'pending_review', reviewVersion: 3 };
const stats = { policyVersion: 'feedback-labels-v1', isPartial: true, cells: [{ ratingKey: 'helpful', reasonKey: 'accurate', citationCount: 1, feedbackCount: 5 }] };
const wrappers: VueWrapper[] = [];
function render(allow = (_code: string) => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allow);
  const w = mount(Panel, { global: { plugins: [pinia], stubs: { teleport: true, transition: false } } }); wrappers.push(w); return w;
}
async function open(w: VueWrapper) { await w.get('[data-testid="feedback-review-open"]').trigger('click'); await flushPromises(); }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getAnswerFeedbackReviews).mockResolvedValue({ items: [row], nextCursor: null }); vi.mocked(api.reviewAnswerFeedback).mockResolvedValue({ ...row, reviewStateKey: 'approved', reviewVersion: 4 }); vi.mocked(api.exportAnswerFeedbackLabels).mockResolvedValue(stats); vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm' as unknown as MessageBoxData); });
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

it('无独立读取及导出权限时没有入口且不请求', async () => {
  const w = render(code => code === 'ai.chat.sessions.read'); expect(w.find('[data-testid="feedback-review-open"]').exists()).toBe(false);
  expect(api.getAnswerFeedbackReviews).not.toHaveBeenCalled(); expect(api.exportAnswerFeedbackLabels).not.toHaveBeenCalled();
});
it('队列只展示标签，独立审核权限撤销后不渲染写按钮', async () => {
  const w = render(code => code !== 'ai.knowledge_answer_feedback.review'); await open(w);
  expect(w.text()).toContain('有帮助'); expect(w.text()).toContain('准确'); expect(w.text()).not.toContain(id);
  expect(w.find('[data-testid="feedback-review-approve"]').exists()).toBe(false); expect(w.find('[data-testid="feedback-review-reject"]').exists()).toBe(false);
});
it.each(['approved', 'rejected'] as const)('审核 %s 冻结该行双版本并刷新', async decision => {
  const w = render(); await open(w); await w.get(`[data-testid="feedback-review-${decision === 'approved' ? 'approve' : 'reject'}"]`).trigger('click'); await flushPromises();
  expect(api.reviewAnswerFeedback).toHaveBeenCalledWith({ feedbackId: id, body: { decisionKey: decision, feedbackVersion: 2, reviewVersion: 3 } }, expect.any(AbortSignal));
  expect(api.getAnswerFeedbackReviews).toHaveBeenCalledTimes(2);
});
it.each([409, 500])('写入结果 %s 要求刷新，阻止重复审核', async status => {
  vi.mocked(api.reviewAnswerFeedback).mockRejectedValue({ status }); const w = render(); await open(w);
  await w.get('[data-testid="feedback-review-approve"]').trigger('click'); await flushPromises(); expect(w.get('[data-testid="feedback-review-approve"]').attributes('disabled')).toBeDefined();
  vi.mocked(api.getAnswerFeedbackReviews).mockResolvedValue({ items: [{ ...row, feedbackVersion: 5, reviewVersion: 4 }], nextCursor: null });
  await w.get('[data-testid="feedback-review-refresh"]').trigger('click'); await flushPromises();
  await w.get('[data-testid="feedback-review-reject"]').trigger('click'); await flushPromises();
  expect(vi.mocked(api.reviewAnswerFeedback).mock.calls.at(-1)![0].body).toEqual({ decisionKey: 'rejected', feedbackVersion: 5, reviewVersion: 4 });
});
it('空页仍允许用密文游标翻页，并可返回上一页', async () => {
  vi.mocked(api.getAnswerFeedbackReviews).mockResolvedValueOnce({ items: [], nextCursor: 'opaque-next' }).mockResolvedValue({ items: [row], nextCursor: null });
  const w = render(); await open(w); await w.get('[data-testid="feedback-review-next"]').trigger('click'); await flushPromises();
  expect(vi.mocked(api.getAnswerFeedbackReviews).mock.calls.at(-1)![0]).toEqual({ after: 'opaque-next' });
  await w.get('[data-testid="feedback-review-previous"]').trigger('click'); await flushPromises(); expect(vi.mocked(api.getAnswerFeedbackReviews).mock.calls.at(-1)![0]).toEqual({});
});
it('导出权限独立于队列读取，统计面板不请求队列', async () => {
  const w = render(code => ['ai.chat.sessions.read', 'ai.knowledge_answer_feedback.export'].includes(code)); await open(w);
  expect(api.getAnswerFeedbackReviews).not.toHaveBeenCalled(); expect(w.find('[data-testid="feedback-review-refresh"]').exists()).toBe(false);
  await w.get('[data-testid="feedback-labels-preview"]').trigger('click'); await flushPromises(); expect(w.text()).toContain('5');
});
it('租户切换中止请求并丢弃晚到标签', async () => {
  let resolve!: (value: { items: typeof row[]; nextCursor: null }) => void; vi.mocked(api.getAnswerFeedbackReviews).mockReturnValue(new Promise(r => { resolve = r; }));
  const w = render(); await w.get('[data-testid="feedback-review-open"]').trigger('click'); const signal = vi.mocked(api.getAnswerFeedbackReviews).mock.calls[0]![1]!;
  useSessionStore().currentContextName = 'Tenant B'; await flushPromises(); expect(signal.aborted).toBe(true);
  resolve({ items: [row], nextCursor: null }); await flushPromises(); expect(w.text()).not.toContain('准确');
});
it('确认期间切换上下文不得派发旧审核', async () => {
  let confirm!: (value: MessageBoxData) => void; vi.mocked(ElMessageBox.confirm).mockReturnValue(new Promise(r => { confirm = r; }));
  const w = render(); await open(w); await w.get('[data-testid="feedback-review-approve"]').trigger('click');
  useSessionStore().switching = true; confirm('confirm' as unknown as MessageBoxData); await flushPromises(); expect(api.reviewAnswerFeedback).not.toHaveBeenCalled();
});
it('关闭面板和卸载中止在途读取', async () => {
  vi.mocked(api.getAnswerFeedbackReviews).mockReturnValue(new Promise(() => {})); const w = render(); await w.get('[data-testid="feedback-review-open"]').trigger('click');
  const signal = vi.mocked(api.getAnswerFeedbackReviews).mock.calls[0]![1]!; await w.get('[data-testid="feedback-review-close"]').trigger('click'); expect(signal.aborted).toBe(true);
  await w.get('[data-testid="feedback-review-open"]').trigger('click'); const second = vi.mocked(api.getAnswerFeedbackReviews).mock.calls[1]![1]!; w.unmount(); expect(second.aborted).toBe(true);
});
it('英文模式显示审核与统计范围说明', async () => {
  const i18n = useAdminI18n(); const old = i18n.locale.value; i18n.setLocale('en-US');
  try { const w = render(); await open(w); expect(w.text()).toContain('Feedback review'); expect(w.text()).toContain('anonymity'); }
  finally { i18n.setLocale(old); }
});
it('下载重新读取统计且仅保存固定投影，不泄露附加字段', async () => {
  let blob!: Blob; const create = vi.fn((value: Blob) => { blob = value; return 'blob:labels'; }); const revoke = vi.fn();
  vi.stubGlobal('URL', class extends URL { static createObjectURL = create; static revokeObjectURL = revoke; });
  const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
  const w = render(); await open(w); await w.get('[data-testid="feedback-labels-preview"]').trigger('click'); await flushPromises();
  const extended = { ...stats, content: 'private', cells: [{ ...stats.cells[0]!, actorId: id, content: 'private' }] };
  vi.mocked(api.exportAnswerFeedbackLabels).mockResolvedValue(extended);
  await w.get('[data-testid="feedback-labels-download"]').trigger('click'); await flushPromises();
  expect(api.exportAnswerFeedbackLabels).toHaveBeenCalledTimes(2); expect(create).toHaveBeenCalledTimes(1); expect(click).toHaveBeenCalledTimes(1); expect(revoke).toHaveBeenCalledWith('blob:labels');
  const content = await new Promise<string>(resolve => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result)); reader.readAsText(blob); });
  expect(JSON.parse(content)).toEqual(stats); expect(content).not.toContain(id); expect(content).not.toContain('private');
});
it('独立导出权限撤销会中止读取并阻止晚到下载', async () => {
  let resolve!: (value: typeof stats) => void; vi.mocked(api.exportAnswerFeedbackLabels).mockReturnValue(new Promise(r => { resolve = r; }));
  const create = vi.fn(); vi.stubGlobal('URL', class extends URL { static createObjectURL = create; static revokeObjectURL = vi.fn(); });
  const pinia = createPinia(); setActivePinia(pinia);
  useSessionStore().currentUser = { permissions: ['ai.chat.sessions.read', 'ai.knowledge_answer_feedback.review_read', 'ai.knowledge_answer_feedback.review', 'ai.knowledge_answer_feedback.export'] } as NonNullable<ReturnType<typeof useSessionStore>['currentUser']>;
  const w = mount(Panel, { global: { plugins: [pinia], stubs: { teleport: true, transition: false } } }); wrappers.push(w);
  await open(w); await w.get('[data-testid="feedback-labels-download"]').trigger('click');
  const signal = vi.mocked(api.exportAnswerFeedbackLabels).mock.calls[0]![0]!;
  useSessionStore().currentUser = { permissions: ['ai.chat.sessions.read', 'ai.knowledge_answer_feedback.review_read'] } as NonNullable<ReturnType<typeof useSessionStore>['currentUser']>;
  await flushPromises(); expect(signal.aborted).toBe(true); resolve(stats); await flushPromises(); expect(create).not.toHaveBeenCalled();
  await open(w); expect(w.find('[data-testid="feedback-labels-download"]').exists()).toBe(false); expect(w.find('[data-testid="feedback-review-approve"]').exists()).toBe(false);
});
it('过期游标错误回到第一页，显式刷新后才恢复审核', async () => {
  vi.mocked(api.getAnswerFeedbackReviews).mockResolvedValueOnce({ items: [row], nextCursor: 'expired' }).mockRejectedValueOnce({ status: 400 }).mockResolvedValue({ items: [row], nextCursor: null });
  const w = render(); await open(w); await w.get('[data-testid="feedback-review-next"]').trigger('click'); await flushPromises();
  expect(w.get('[data-testid="feedback-review-approve"]').attributes('disabled')).toBeDefined();
  await w.get('[data-testid="feedback-review-refresh"]').trigger('click'); await flushPromises();
  expect(vi.mocked(api.getAnswerFeedbackReviews).mock.calls.at(-1)![0]).toEqual({}); expect(w.get('[data-testid="feedback-review-approve"]').attributes('disabled')).toBeUndefined();
});
