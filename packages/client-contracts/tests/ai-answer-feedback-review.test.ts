import { afterEach, expect, it, vi } from 'vitest';
import { createHttpClient } from '../src/http';
import * as feedback from '../src/index';

const id = '00000000-0000-0000-0000-000000000001';
const item = { feedbackId: id, ratingKey: 'helpful', reasonKey: 'accurate', citationCount: 0, feedbackVersion: 1, reviewStateKey: 'pending_review', reviewVersion: 0 };
const json = (value: unknown) => new Response(JSON.stringify(value), { status: 200, headers: { 'content-type': 'application/json' } });
afterEach(() => vi.unstubAllGlobals());

it('审核队列对密文游标编码并保留认证、语言和取消', async () => {
  const fetch = vi.fn().mockResolvedValue(json({ items: [item], nextCursor: 'opaque-next' })); vi.stubGlobal('fetch', fetch);
  const http = createHttpClient(); http.configureRequestLocale(() => 'en-US'); http.configureAuthentication({ getAccessToken: () => 'token', refresh: async () => false });
  const signal = new AbortController().signal;
  expect(await feedback.aiGetKnowledgeAnswerFeedbackReviews(http, { after: 'opaque+/=' }, signal)).toEqual({ items: [item], nextCursor: 'opaque-next' });
  expect(fetch.mock.calls[0]![0]).toBe('/api/v1/ai/knowledge-answer-feedback/reviews?after=opaque%2B%2F%3D');
  const request = fetch.mock.calls[0]![1]; expect(request.signal).toBe(signal);
  expect(new Headers(request.headers).get('authorization')).toBe('Bearer token'); expect(new Headers(request.headers).get('accept-language')).toBe('en-US');
});
it('审核命令只发送决定和双版本，使用反馈专用路径', async () => {
  const fetch = vi.fn().mockResolvedValue(json({ ...item, reviewStateKey: 'approved', reviewVersion: 1 })); vi.stubGlobal('fetch', fetch);
  await feedback.aiReviewKnowledgeAnswerFeedback(createHttpClient(), { feedbackId: id, body: { decisionKey: 'approved', feedbackVersion: 1, reviewVersion: 0 } });
  expect(fetch.mock.calls[0]![0]).toBe(`/api/v1/ai/knowledge-answer-feedback/reviews/${id}`);
  expect(fetch.mock.calls[0]![1].method).toBe('PUT');
  expect(JSON.parse(fetch.mock.calls[0]![1].body)).toEqual({ decisionKey: 'approved', feedbackVersion: 1, reviewVersion: 0 });
});
it('统计只返回规则版本与固定标签，不携带附加标识或正文', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ policyVersion: 'feedback-labels-v1', isPartial: true, cells: [{ ratingKey: 'helpful', reasonKey: 'accurate', citationCount: 0, feedbackCount: 5, actorId: id, content: 'private' }], content: 'private' })));
  expect(await feedback.aiExportKnowledgeAnswerFeedbackLabels(createHttpClient())).toEqual({ policyVersion: 'feedback-labels-v1', isPartial: true, cells: [{ ratingKey: 'helpful', reasonKey: 'accurate', citationCount: 0, feedbackCount: 5 }] });
});
it('统计读取传递取消信号且不发送客户端筛选', async () => {
  const fetch = vi.fn().mockResolvedValue(json({ policyVersion: 'feedback-labels-v1', isPartial: true, cells: [] })); vi.stubGlobal('fetch', fetch);
  const signal = new AbortController().signal;
  await feedback.aiExportKnowledgeAnswerFeedbackLabels(createHttpClient(), signal);
  expect(fetch.mock.calls[0]![0]).toBe('/api/v1/ai/knowledge-answer-feedback/export');
  expect(fetch.mock.calls[0]![1].signal).toBe(signal); expect(fetch.mock.calls[0]![1].body).toBeUndefined();
});
it.each([
  { ...item, reasonKey: 'model instruction' }, { ...item, citationCount: 9 }, { ...item, feedbackVersion: 0 }, { ...item, reviewStateKey: 'unknown' },
  { ...item, reviewVersion: 0.5 }, { ...item, feedbackId: 'not-an-id' }
])('审核队列拒绝损坏的闭集标签和版本 %j', async value => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ items: [value], nextCursor: null })));
  await expect(feedback.aiGetKnowledgeAnswerFeedbackReviews(createHttpClient(), {})).rejects.toThrow();
});
it.each([
  { policyVersion: 'new-rule', isPartial: true, cells: [] }, { policyVersion: 'feedback-labels-v1', isPartial: false, cells: [] },
  { policyVersion: 'feedback-labels-v1', isPartial: true, cells: [{ ratingKey: 'helpful', reasonKey: 'accurate', citationCount: 0, feedbackCount: 4 }] },
  { policyVersion: 'feedback-labels-v1', isPartial: true, cells: [{ ratingKey: 'helpful', reasonKey: 'accurate', citationCount: 0, feedbackCount: 65 }] }
])('统计拒绝不符合版本、窗口和小样本约束的响应 %j', async value => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(value)));
  await expect(feedback.aiExportKnowledgeAnswerFeedbackLabels(createHttpClient())).rejects.toThrow();
});
it.each([
  { feedbackId: id, body: { decisionKey: 'unknown', feedbackVersion: 1, reviewVersion: 0 } },
  { feedbackId: id, body: { decisionKey: 'approved', feedbackVersion: 1, reviewVersion: 2147483647 } },
  { feedbackId: '00000000-0000-0000-0000-000000000000', body: { decisionKey: 'approved', feedbackVersion: 1, reviewVersion: 0 } }
])('审核输入非法时不派发请求 %j', async request => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  await expect(feedback.aiReviewKnowledgeAnswerFeedback(createHttpClient(), request)).rejects.toThrow('invalid_ai_feedback_review_request'); expect(fetch).not.toHaveBeenCalled();
});
it.each([
  { ...item, reviewStateKey: 'approved', reviewVersion: 2 },
  { ...item, reviewStateKey: 'rejected', reviewVersion: 1 },
  { ...item, reviewStateKey: 'approved', reviewVersion: 1, feedbackVersion: 2 },
  { ...item, reviewStateKey: 'approved', reviewVersion: 1, feedbackId: '00000000-0000-0000-0000-000000000002' }
])('审核响应不能冒充另一行或另一次版本更新 %j', async value => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(value)));
  await expect(feedback.aiReviewKnowledgeAnswerFeedback(createHttpClient(), { feedbackId: id, body: { decisionKey: 'approved', feedbackVersion: 1, reviewVersion: 0 } })).rejects.toThrow('invalid_ai_feedback_review_response');
});
