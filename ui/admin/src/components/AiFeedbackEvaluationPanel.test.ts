import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { ElMessageBox, type MessageBoxData } from 'element-plus';
import Panel from './AiFeedbackEvaluationPanel.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-feedback-evaluation';
vi.mock('../api/ai-feedback-evaluation', () => ({ prepareFeedbackEvaluation: vi.fn(), admitFeedbackEvaluation: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn() }));
const feedback = { feedbackId: 'feedback', ratingKey: 'unhelpful', reasonKey: 'unsupported', citationCount: 1, feedbackVersion: 2, reviewVersion: 3, reviewStateKey: 'approved' };
const candidate = { candidateToken: 'encrypted', sampleHash: 'a'.repeat(64), expiresAtUtc: '2026-10-17T03:00:00Z' };
const wrappers: VueWrapper[] = [];
function render(allowed = (_: string) => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allowed);
  const wrapper = mount(Panel, { props: { feedback }, global: { plugins: [pinia], stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); wrappers.push(wrapper); return wrapper;
}
async function fill(w: VueWrapper) {
  await w.get('[data-testid="feedback-evaluation-open"]').trigger('click');
  await w.get('[data-testid="evaluation-question"]').setValue('脱敏问题');
  await w.get('[data-testid="evaluation-answer"]').setValue('脱敏答案');
  await w.get('[data-testid="evaluation-evidence"]').setValue('脱敏依据');
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.prepareFeedbackEvaluation).mockResolvedValue(candidate);
  vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm' as MessageBoxData); });
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks(); });
it('没有准备权限不创建入口或请求', () => {
  const w = render(code => code !== 'ai.knowledge_answer_feedback.evaluation_prepare');
  expect(w.find('[data-testid="feedback-evaluation-open"]').exists()).toBe(false); expect(api.prepareFeedbackEvaluation).not.toHaveBeenCalled();
});
it('只提交人工改写内容及精确审核版本，准入权限独立', async () => {
  const w = render(code => code !== 'ai.knowledge_answer_feedback.evaluation_admit'); await fill(w);
  await w.get('[data-testid="evaluation-prepare"]').trigger('click'); await flushPromises();
  expect(api.prepareFeedbackEvaluation).toHaveBeenCalledWith({ feedbackId: 'feedback', body: { feedbackVersion: 2, reviewVersion: 3,
    sample: { languageTag: 'zh-CN', question: '脱敏问题', expectedAnswer: '脱敏答案', evidence: '脱敏依据', shouldRefuse: false } } }, expect.any(AbortSignal));
  expect(w.find('[data-testid="evaluation-admit"]').exists()).toBe(false);
});
it('编辑候选立即清除旧准入凭据', async () => {
  const w = render(); await fill(w); await w.get('[data-testid="evaluation-prepare"]').trigger('click'); await flushPromises();
  expect(w.get('[data-testid="evaluation-admit"]').attributes('disabled')).toBeUndefined();
  await w.get('[data-testid="evaluation-question"]').setValue('新的脱敏问题');
  expect(w.get('[data-testid="evaluation-admit"]').attributes('disabled')).toBeDefined();
});
it('撤权中止候选等待，迟到响应不能准入', async () => {
  let resolve!: (value: typeof candidate) => void; vi.mocked(api.prepareFeedbackEvaluation).mockReturnValue(new Promise(r => { resolve = r; }));
  const w = render(); await fill(w); await w.get('[data-testid="evaluation-prepare"]').trigger('click');
  const signal = vi.mocked(api.prepareFeedbackEvaluation).mock.calls[0]![1]!;
  useSessionStore().switching = true; await flushPromises(); expect(signal.aborted).toBe(true);
  resolve(candidate); await flushPromises(); expect(api.admitFeedbackEvaluation).not.toHaveBeenCalled(); expect(w.text()).not.toContain(candidate.sampleHash);
});
it('取消人工准入确认不会发送准入请求', async () => {
  const w = render(); await fill(w); await w.get('[data-testid="evaluation-prepare"]').trigger('click'); await flushPromises();
  vi.mocked(ElMessageBox.confirm).mockRejectedValueOnce('cancel');
  await w.get('[data-testid="evaluation-admit"]').trigger('click'); await flushPromises();
  expect(api.admitFeedbackEvaluation).not.toHaveBeenCalled();
});
