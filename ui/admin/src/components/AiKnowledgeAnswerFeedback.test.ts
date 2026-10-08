import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import Feedback from './AiKnowledgeAnswerFeedback.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import * as api from '../api/ai-answer-feedback';
vi.mock('../api/ai-answer-feedback');
const saved = { id: 'feedback', messageId: 'message', ratingKey: 'helpful', reasonKey: 'accurate', citationIds: [], statusKey: 'pending_review', version: 1, updatedAtUtc: 'now' };
const wrappers: VueWrapper[] = [];
async function render(allow: (code: string) => boolean = () => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allow);
  const wrapper = mount(Feedback, { props: { sessionId: 'session', messageId: 'message', citations: [] }, global: { plugins: [pinia] } });
  wrappers.push(wrapper); return wrapper;
}
async function open(wrapper: VueWrapper) { await wrapper.get('[data-testid="feedback-open"]').trigger('click'); await flushPromises(); }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getAnswerFeedback).mockRejectedValue({ status: 404 }); vi.mocked(api.getAnswerFeedbackState).mockRejectedValue({ status: 404 }); vi.mocked(api.setAnswerFeedback).mockResolvedValue(saved); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); });
it('首次提交使用版本零；拒答没有引用也可提交', async () => {
  const wrapper = await render(); await open(wrapper); await wrapper.get('[data-testid="feedback-save"]').trigger('click'); await flushPromises();
  expect(api.setAnswerFeedback).toHaveBeenCalledWith({ sessionId: 'session', messageId: 'message', body: { ratingKey: 'helpful', reasonKey: 'accurate', citationIds: [], version: 0 } }, expect.any(AbortSignal));
  expect(wrapper.text()).toContain('待审核');
});
it.each([409, 500])('状态 %s 保留选择并阻止重复写入，刷新后使用新版本', async status => {
  const wrapper = await render(); await open(wrapper); await wrapper.get('[data-testid="feedback-rating"]').setValue('unhelpful');
  await wrapper.get('[data-testid="feedback-reason"]').setValue('other'); vi.mocked(api.setAnswerFeedback).mockRejectedValueOnce({ status });
  await wrapper.get('[data-testid="feedback-save"]').trigger('click'); await flushPromises();
  expect(wrapper.get('[data-testid="feedback-save"]').attributes('disabled')).toBeDefined();
  expect((wrapper.get('[data-testid="feedback-reason"]').element as HTMLSelectElement).value).toBe('other');
  vi.mocked(api.getAnswerFeedback).mockResolvedValue({ ...saved, version: 4 }); await wrapper.get('[data-testid="feedback-refresh"]').trigger('click'); await flushPromises();
  await wrapper.get('[data-testid="feedback-save"]').trigger('click'); await flushPromises();
  expect(vi.mocked(api.setAnswerFeedback).mock.calls.at(-1)?.[0].body).toMatchObject({ ratingKey: 'unhelpful', reasonKey: 'other', version: 4 });
});
it('只读权限不显示提交；无读取精确权限不请求', async () => {
  const denied = await render(code => !['ai.knowledge_answer_feedback.read', 'ai.knowledge_answer_feedback.submit', 'ai.knowledge_answer_feedback.withdraw'].includes(code)); expect(denied.find('[data-testid="feedback-open"]').exists()).toBe(false); expect(api.getAnswerFeedback).not.toHaveBeenCalled();
  const readonly = await render(code => !['ai.knowledge_answer_feedback.submit', 'ai.knowledge_answer_feedback.withdraw'].includes(code)); await open(readonly); expect(readonly.find('[data-testid="feedback-save"]').exists()).toBe(false);
});
it('切换消息中止请求并丢弃晚到结果', async () => {
  let resolve!: (value: typeof saved) => void; vi.mocked(api.getAnswerFeedback).mockReturnValue(new Promise(r => { resolve = r; }));
  const wrapper = await render(); await wrapper.get('[data-testid="feedback-open"]').trigger('click');
  const signal = vi.mocked(api.getAnswerFeedback).mock.calls[0]![1]!; await wrapper.setProps({ messageId: 'other' }); expect(signal.aborted).toBe(true);
  resolve(saved); await flushPromises(); expect(wrapper.find('[data-testid="feedback-save"]').exists()).toBe(false); expect(wrapper.text()).not.toContain('待审核');
});
it('身份或租户切换中止请求', async () => {
  vi.mocked(api.getAnswerFeedback).mockReturnValue(new Promise(() => {})); const wrapper = await render(); await wrapper.get('[data-testid="feedback-open"]').trigger('click');
  const signal = vi.mocked(api.getAnswerFeedback).mock.calls[0]![1]!; useSessionStore().switching = true; await flushPromises(); expect(signal.aborted).toBe(true); expect(wrapper.find('[data-testid="feedback-open"]').exists()).toBe(false);
});
it('撤回使用已读版本且显示清空墓碑', async () => {
  vi.mocked(api.getAnswerFeedback).mockResolvedValue(saved); vi.mocked(api.withdrawAnswerFeedback).mockResolvedValue({ ...saved, ratingKey: null, reasonKey: null, statusKey: 'withdrawn', version: 2 });
  const wrapper = await render(); await open(wrapper); await wrapper.get('[data-testid="feedback-withdraw"]').trigger('click'); await flushPromises();
  expect(api.withdrawAnswerFeedback).toHaveBeenCalledWith({ sessionId: 'session', messageId: 'message', version: 1 }, expect.any(AbortSignal)); expect(wrapper.text()).toContain('已撤回');
});
it('英文模式显示固定文案', async () => {
  const i18n = useAdminI18n(); const previous = i18n.locale.value; i18n.setLocale('en-US');
  try { const wrapper = await render(); await open(wrapper); expect(wrapper.text()).toContain('Helpful'); expect(wrapper.text()).toContain('requires review'); }
  finally { i18n.setLocale(previous); }
});
it('来源资格丢失后只读取清除版本并允许撤回，清空评价与引用', async () => {
  vi.mocked(api.getAnswerFeedback).mockResolvedValue(saved); const wrapper = await render(); await open(wrapper);
  vi.mocked(api.getAnswerFeedbackState).mockResolvedValue({ messageId: 'message', statusKey: 'pending_review', version: 4 });
  vi.mocked(api.withdrawAnswerFeedback).mockResolvedValue({ ...saved, ratingKey: null, reasonKey: null, citationIds: [], statusKey: 'withdrawn', version: 5 });
  await wrapper.setProps({ unavailable: true }); await open(wrapper);
  expect(wrapper.find('[data-testid="feedback-rating"]').exists()).toBe(false); expect(wrapper.find('[data-testid="feedback-save"]').exists()).toBe(false);
  await wrapper.get('[data-testid="feedback-withdraw"]').trigger('click'); await flushPromises();
  expect(api.withdrawAnswerFeedback).toHaveBeenCalledWith({ sessionId: 'session', messageId: 'message', version: 4 }, expect.any(AbortSignal)); expect(wrapper.text()).toContain('已撤回');
});
it('来源 GET 返回404时使用无载荷状态恢复撤回，不允许重新提交', async () => {
  vi.mocked(api.getAnswerFeedbackState).mockResolvedValue({ messageId: 'message', statusKey: 'pending_review', version: 7 });
  const wrapper = await render(); await open(wrapper); expect(wrapper.get('[data-testid="feedback-withdraw"]').attributes('disabled')).toBeUndefined();
  expect(wrapper.get('[data-testid="feedback-save"]').attributes('disabled')).toBeDefined();
  expect((wrapper.get('[data-testid="feedback-reason"]').element as HTMLSelectElement).value).toBe('accurate');
  vi.mocked(api.withdrawAnswerFeedback).mockResolvedValue({ ...saved, ratingKey: null, reasonKey: null, citationIds: [], statusKey: 'withdrawn', version: 8 });
  await wrapper.get('[data-testid="feedback-withdraw"]').trigger('click'); await flushPromises();
  expect(wrapper.get('[data-testid="feedback-save"]').attributes('disabled')).toBeDefined();
});
it('首次提交结果未知且未落库时，双404刷新保留待重试选择', async () => {
  const wrapper = await render(); await open(wrapper); await wrapper.get('[data-testid="feedback-rating"]').setValue('unhelpful');
  await wrapper.get('[data-testid="feedback-reason"]').setValue('other'); vi.mocked(api.setAnswerFeedback).mockRejectedValueOnce(new Error('network'));
  await wrapper.get('[data-testid="feedback-save"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="feedback-refresh"]').trigger('click'); await flushPromises();
  expect((wrapper.get('[data-testid="feedback-reason"]').element as HTMLSelectElement).value).toBe('other');
  await wrapper.get('[data-testid="feedback-save"]').trigger('click'); await flushPromises();
  expect(vi.mocked(api.setAnswerFeedback).mock.calls.at(-1)?.[0].body).toMatchObject({ ratingKey: 'unhelpful', reasonKey: 'other', version: 0 });
});
it('来源仍可读时墓碑允许使用最新版本重新评价', async () => {
  vi.mocked(api.getAnswerFeedback).mockResolvedValue({ ...saved, ratingKey: null, reasonKey: null, citationIds: [], statusKey: 'withdrawn', version: 3 });
  const wrapper = await render(); await open(wrapper); expect(wrapper.get('[data-testid="feedback-save"]').attributes('disabled')).toBeUndefined();
  await wrapper.get('[data-testid="feedback-save"]').trigger('click'); await flushPromises();
  expect(vi.mocked(api.setAnswerFeedback).mock.calls.at(-1)?.[0].body.version).toBe(3);
});
it('撤回权限独立于提交权限，清除者不能提交固定评价', async () => {
  vi.mocked(api.getAnswerFeedback).mockResolvedValue(saved);
  const wrapper = await render(code => code !== 'ai.knowledge_answer_feedback.submit'); await open(wrapper);
  expect(wrapper.find('[data-testid="feedback-save"]').exists()).toBe(false);
  expect(wrapper.find('[data-testid="feedback-withdraw"]').exists()).toBe(true);
});
it('提交权限不会同时授予撤回权限', async () => {
  vi.mocked(api.getAnswerFeedback).mockResolvedValue(saved);
  const wrapper = await render(code => code !== 'ai.knowledge_answer_feedback.withdraw'); await open(wrapper);
  expect(wrapper.find('[data-testid="feedback-save"]').exists()).toBe(true);
  expect(wrapper.find('[data-testid="feedback-withdraw"]').exists()).toBe(false);
});
