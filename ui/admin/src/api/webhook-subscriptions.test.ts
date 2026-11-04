import { beforeEach, expect, it, vi } from 'vitest';
import { request } from './http';
import { getPendingWebhookSigningKeyRotation, getWebhookSigningKeyRotation, prepareWebhookSigningKeyRotation, activateWebhookSigningKeyRotation, cancelWebhookSigningKeyRotation } from './webhook-subscriptions';
import { createWebhookSubscription, listWebhookSubscriptions, setWebhookSubscriptionStatus, listWebhookDeliveryHistory, getWebhookDeliveryHistory, listWebhookDeliveryAttempts, replayWebhookDelivery, getWebhookDeliveryReplayReceipt } from './webhook-subscriptions';
vi.mock('./http', () => ({ request: vi.fn() }));
const call = vi.mocked(request), id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const row = { id, tenantId: id, signingKeyId: id, eventType: 'fullnet.workflow.instance.completed', targetUrl: 'https://example.com/hooks', isActive: true, version: 1 };
const delivery = { id, tenantId: id, subscriptionId: id, eventId: id, statusKey: 'failed', attemptCount: 2, version: 3,
  nextAttemptAtUtc: null, lastErrorCode: 'webhooks.delivery.http_error', lastHttpStatus: 503, createdAtUtc: '2026-11-04T00:00:00Z', updatedAtUtc: '2026-11-04T00:01:00Z' };
beforeEach(() => call.mockReset());
const rotation={operationId:id,tenantId:id,subscriptionId:id,previousKeyId:id,signingKeyId:'019bc2b1-2a40-7cc3-8992-a80de51bf298',previousVersion:1,preparedVersion:2,stateKey:'prepared',preparedAtUtc:'2026-11-05T00:00:00Z',completedAtUtc:null,completedVersion:null};
it('准备固定原操作及修订，秘密只在 POST 输入且禁止自动认证重发',async()=>{
 call.mockResolvedValue({...rotation,protectedSecret:'private',protectedFingerprint:'private'});const signal=new AbortController().signal,body={operationId:id,version:1,signingSecret:'new-independent-secret'};
 await expect(prepareWebhookSigningKeyRotation(id,body,signal)).resolves.toEqual(rotation);expect(call).toHaveBeenCalledExactlyOnceWith(`/api/v1/webhooks/subscriptions/${id}/signing-key-rotations`,{method:'POST',body:JSON.stringify(body)},signal,{retryUnauthorized:false});
});
it('当前准备和原轮换回执只读，绑定原订阅和操作',async()=>{
 call.mockResolvedValue(rotation);await expect(getPendingWebhookSigningKeyRotation(id)).resolves.toEqual(rotation);await expect(getWebhookSigningKeyRotation(id,id)).resolves.toEqual(rotation);
 expect(call.mock.calls.map(args=>args[1]?.method)).toEqual(['GET','GET']);call.mockResolvedValue({...rotation,subscriptionId:rotation.signingKeyId});await expect(getWebhookSigningKeyRotation(id,id)).rejects.toThrow();
});
it('激活与取消按明确动作、身份和本次修订核对不可变完成回执',async()=>{
 const body={version:2,signingKeyId:rotation.signingKeyId},signal=new AbortController().signal;
 for(const [action,submit] of [['activated',activateWebhookSigningKeyRotation],['cancelled',cancelWebhookSigningKeyRotation]] as const){
  call.mockClear();const completed={...rotation,stateKey:action,completedAtUtc:'2026-11-05T00:01:00Z',completedVersion:3};call.mockResolvedValue(completed);await expect(submit(id,id,body,signal)).resolves.toEqual(completed);
  expect(call).toHaveBeenCalledExactlyOnceWith(`/api/v1/webhooks/subscriptions/${id}/signing-key-rotations/${id}/${action==='activated'?'activate':'cancel'}`,{method:'POST',body:JSON.stringify(body)},signal,{retryUnauthorized:false});
 }
});
it.each([{...rotation,preparedVersion:3},{...rotation,signingKeyId:id},{...rotation,stateKey:'activated'},{...rotation,completedVersion:3},{...rotation,operationId:'invalid'}])('轮换矛盾状态或身份拒绝接纳 %j',async value=>{
 call.mockResolvedValue(value);await expect(getWebhookSigningKeyRotation(id,id)).rejects.toThrow();
});
it('激活不能接纳另一动作、键或修订；无效输入不发送',async()=>{
 const body={version:2,signingKeyId:rotation.signingKeyId};for(const value of [rotation,{...rotation,stateKey:'cancelled',completedAtUtc:'2026-11-05T00:01:00Z',completedVersion:3},{...rotation,stateKey:'activated',completedAtUtc:'2026-11-05T00:01:00Z',completedVersion:4}]){call.mockResolvedValue(value);await expect(activateWebhookSigningKeyRotation(id,id,body)).rejects.toThrow();}
 call.mockClear();await expect(prepareWebhookSigningKeyRotation(id,{operationId:id,version:2_147_483_646,signingSecret:'new-independent-secret'})).rejects.toThrow();await expect(activateWebhookSigningKeyRotation(id,id,{...body,version:2_147_483_647})).rejects.toThrow();expect(call).not.toHaveBeenCalled();
});
const replayReceipt = { operationId: id, tenantId: id, subscriptionId: id, deliveryId: id, eventId: id, previousVersion: 3, version: 4, attemptCount: 2, queuedAtUtc: '2026-11-05T00:00:00Z' };
it('重放固定原操作和修订，关闭认证重发并剔除敏感多余字段', async () => {
  call.mockResolvedValue({ ...replayReceipt, signingSecret: 'private', actorSessionId: 'private' }); const signal = new AbortController().signal;
  await expect(replayWebhookDelivery(id,id,{ operationId:id,version:3 },signal)).resolves.toEqual(replayReceipt);
  expect(call).toHaveBeenCalledExactlyOnceWith(`/api/v1/webhooks/subscriptions/${id}/deliveries/${id}/replays`, { method:'POST', body:JSON.stringify({ operationId:id,version:3 }) },signal,{ retryUnauthorized:false });
});
it('读取原回执只发 GET，按原操作和资源核对', async () => {
  call.mockResolvedValue(replayReceipt); await expect(getWebhookDeliveryReplayReceipt(id,id,id)).resolves.toEqual(replayReceipt);
  expect(call).toHaveBeenCalledExactlyOnceWith(`/api/v1/webhooks/subscriptions/${id}/deliveries/${id}/replays/${id}`,{ method:'GET' },undefined);
});
it('重放拒绝矛盾修订、错误操作、错误来源及不安全数值', async () => {
  for (const value of [{ ...replayReceipt,version:5 },{ ...replayReceipt,operationId:'019bc2b1-2a40-7cc3-8992-a80de51bf298' },{ ...replayReceipt,deliveryId:'019bc2b1-2a40-7cc3-8992-a80de51bf298' },{ ...replayReceipt,attemptCount:'9007199254740993' }]) { call.mockResolvedValueOnce(value); await expect(replayWebhookDelivery(id,id,{ operationId:id,version:3 })).rejects.toThrow(); }
  call.mockClear(); await expect(replayWebhookDelivery(id,id,{ operationId:id,version:2_147_483_645 })).rejects.toThrow(); expect(call).not.toHaveBeenCalled();
});
const attempt = { id, tenantId: id, subscriptionId: id, deliveryId: id, outcomeKey: 'delivered', startedAtUtc: '2026-11-04T00:00:00Z', completedAtUtc: '2026-11-04T00:00:01Z', httpStatus: 200, errorCode: null, appliedToDelivery: false };
it('逐次记录绑定订阅和投递，保留迟到应答但剔除敏感字段', async () => {
  call.mockResolvedValue({ items: [{ ...attempt, payloadBody: 'private', lastError: 'private' }], page: 1, pageSize: 20, total: '1' });
  const signal = new AbortController().signal;
  await expect(listWebhookDeliveryAttempts(id, id, 1, 20, signal)).resolves.toEqual({ items: [attempt], page: 1, pageSize: 20, total: 1 });
  expect(call).toHaveBeenCalledExactlyOnceWith(`/api/v1/webhooks/subscriptions/${id}/deliveries/${id}/attempts?page=1&pageSize=20`, { method: 'GET' }, signal);
});
it('未确认与失败记录保留各自可验证事实', async () => {
  for (const value of [{ ...attempt, outcomeKey: 'unconfirmed', completedAtUtc: null, httpStatus: null, appliedToDelivery: null }, { ...attempt, outcomeKey: 'failed', httpStatus: 503, errorCode: 'webhooks.delivery.http_error', appliedToDelivery: true }]) {
    call.mockResolvedValueOnce({ items: [value], page: 1, pageSize: 20, total: 1 });
    await expect(listWebhookDeliveryAttempts(id, id)).resolves.toEqual({ items: [value], page: 1, pageSize: 20, total: 1 });
  }
});
it('逐次读取拒绝跨资源、矛盾结果、重复标识及越界页码', async () => {
  for (const value of [{ ...attempt, deliveryId: '019bc2b1-2a40-7cc3-8992-a80de51bf298' }, { ...attempt, subscriptionId: '019bc2b1-2a40-7cc3-8992-a80de51bf298' }, { ...attempt, outcomeKey: 'processing' }, { ...attempt, appliedToDelivery: null }, { ...attempt, errorCode: 'private exception' }, { ...attempt, startedAtUtc: '2026-11-04T08:00:00+08:00' }]) {
    call.mockResolvedValueOnce({ items: [value], page: 1, pageSize: 20, total: 1 }); await expect(listWebhookDeliveryAttempts(id, id)).rejects.toThrow();
  }
  call.mockResolvedValueOnce({ items: [attempt, attempt], page: 1, pageSize: 20, total: 2 }); await expect(listWebhookDeliveryAttempts(id, id)).rejects.toThrow();
  call.mockClear(); await expect(listWebhookDeliveryAttempts(id, id, 0)).rejects.toThrow(); await expect(listWebhookDeliveryAttempts(id, id, 2_147_483_647)).rejects.toThrow(); expect(call).not.toHaveBeenCalled();
});
it('读取规范化版本并移除意外签名字段', async () => {
  call.mockResolvedValue([{ ...row, version: '1', signingSecret: 'must-not-escape' }]); expect(await listWebhookSubscriptions()).toEqual([row]);
});
it.each([undefined, '', '00000000-0000-0000-0000-000000000000', 'invalid'])('缺失或无效公开密钥身份拒绝接纳 %j', async signingKeyId => {
  call.mockResolvedValue([{ ...row, signingKeyId }]); await expect(listWebhookSubscriptions()).rejects.toThrow();
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
it('历史页绑定明确订阅、页码、筛选和信号，剔除多余敏感字段', async () => {
  call.mockResolvedValue({ items: [{ ...delivery, lastError: 'private', payloadBody: 'private', signingSecret: 'private' }], page: 2, pageSize: 20, total: '21' });
  const signal = new AbortController().signal; expect(await listWebhookDeliveryHistory(id, 2, 20, 'failed', signal)).toEqual({ items: [delivery], page: 2, pageSize: 20, total: 21 });
  expect(call).toHaveBeenCalledExactlyOnceWith(`/api/v1/webhooks/subscriptions/${id}/deliveries?page=2&pageSize=20&statusKey=failed`, { method: 'GET' }, signal);
});
it('历史页错误来源、筛选和损坏分页拒绝接纳', async () => {
  for (const result of [
    { items: [{ ...delivery, subscriptionId: '019bc2b1-2a40-7cc3-8992-a80de51bf298' }], page: 1, pageSize: 20, total: 1 },
    { items: [{ ...delivery, statusKey: 'delivered' }], page: 1, pageSize: 20, total: 1 },
    { items: [delivery], page: 2, pageSize: 20, total: 1 },
    { items: [delivery, delivery], page: 1, pageSize: 20, total: 2 }
  ]) { call.mockResolvedValueOnce(result); await expect(listWebhookDeliveryHistory(id, 1, 20, 'failed')).rejects.toThrow(); }
});
it('详情资源错配、失败类别错配和非 UTC 时间不能进入读取模型', async () => {
  for (const value of [{ ...delivery, id: '019bc2b1-2a40-7cc3-8992-a80de51bf298' }, { ...delivery, lastErrorCode: 'raw error' }, { ...delivery, lastHttpStatus: null }, { ...delivery, createdAtUtc: '2026-11-04T00:00:00+08:00' }]) {
    call.mockResolvedValueOnce(value); await expect(getWebhookDeliveryHistory(id, id)).rejects.toThrow();
  }
});
it('历史查询边界在请求前拒绝，空结果及正确单条可以读取', async () => {
  await expect(listWebhookDeliveryHistory(id, 0)).rejects.toThrow(); await expect(listWebhookDeliveryHistory(id, 1, 101)).rejects.toThrow(); await expect(listWebhookDeliveryHistory(id, 2_147_483_647, 100)).rejects.toThrow(); expect(call).not.toHaveBeenCalled();
  call.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0 }); await expect(listWebhookDeliveryHistory(id)).resolves.toEqual({ items: [], page: 1, pageSize: 20, total: 0 });
  call.mockResolvedValueOnce(delivery); await expect(getWebhookDeliveryHistory(id, id)).resolves.toEqual(delivery);
});
