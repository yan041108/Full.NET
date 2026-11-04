import { beforeEach, expect, it, vi } from 'vitest';
import { request } from './http';
import { createWebhookSubscription, listWebhookSubscriptions, setWebhookSubscriptionStatus } from './webhook-subscriptions';
vi.mock('./http', () => ({ request: vi.fn() }));
const call = vi.mocked(request), id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const row = { id, tenantId: id, eventType: 'fullnet.workflow.instance.completed', targetUrl: 'https://example.com/hooks', isActive: true, version: 1 };
beforeEach(() => call.mockReset());
it('读取规范化版本并移除意外签名字段', async () => {
  call.mockResolvedValue([{ ...row, version: '1', signingSecret: 'must-not-escape' }]); expect(await listWebhookSubscriptions()).toEqual([row]);
});
it('创建携带作用域信号并关闭 401 自动重放', async () => {
  call.mockResolvedValue(row); const signal = new AbortController().signal, body = { eventType: row.eventType, targetUrl: row.targetUrl, signingSecret: 'independent-random-secret' };
  await createWebhookSubscription(body, signal); expect(call).toHaveBeenCalledExactlyOnceWith('/api/v1/webhooks/subscriptions', { method: 'POST', body: JSON.stringify(body) }, signal, { retryUnauthorized: false });
});
it('创建错配事件或目标地址拒绝接纳', async () => {
  for (const value of [{ ...row, eventType: 'other' }, { ...row, targetUrl: 'https://other.example.com/hooks' }]) {
    call.mockResolvedValueOnce(value); await expect(createWebhookSubscription({ ...row, signingSecret: 'independent-random-secret' })).rejects.toThrow();
  }
});
it('状态写入固定版本、目标和信号且不自动重放', async () => {
  call.mockResolvedValue({ ...row, isActive: false, version: 2 }); const signal = new AbortController().signal;
  await setWebhookSubscriptionStatus(id, { isActive: false, version: 1 }, signal);
  expect(call).toHaveBeenCalledExactlyOnceWith(`/api/v1/webhooks/subscriptions/${id}/status`, { method: 'POST', body: JSON.stringify({ isActive: false, version: 1 }) }, signal, { retryUnauthorized: false });
});
it.each([{ ...row, version: 0 }, { ...row, isActive: false, version: '9007199254740993' }, { ...row, id: 'bad' }, { ...row, targetUrl: 'http://example.com' }])('畸形结果拒绝接纳 %j', async value => {
  call.mockResolvedValue([value]); await expect(listWebhookSubscriptions()).rejects.toThrow();
});
it('重复订阅标识与错误状态响应拒绝接纳', async () => {
  call.mockResolvedValue([row, row]); await expect(listWebhookSubscriptions()).rejects.toThrow();
  call.mockResolvedValue(row); await expect(setWebhookSubscriptionStatus(id, { isActive: false, version: 1 })).rejects.toThrow();
});
it.each([
  { eventType: 'x'.repeat(129), signingSecret: 'valid-independent-secret' },
  { eventType: '企业事件', signingSecret: 'valid-independent-secret' },
  { eventType: 'event\ninvalid', signingSecret: 'valid-independent-secret' },
  { eventType: row.eventType, signingSecret: 'x'.repeat(129) },
  { eventType: row.eventType, signingSecret: 'x'.repeat(15) }
])('存储预算以外的输入在请求前拒绝 %j', async input => {
  await expect(createWebhookSubscription({ ...input, targetUrl: row.targetUrl })).rejects.toThrow(); expect(call).not.toHaveBeenCalled();
});
