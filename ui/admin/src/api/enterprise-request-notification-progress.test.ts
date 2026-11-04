import { describe, expect, it, vi } from 'vitest';
import type { HttpClient } from '@fullnet/client-contracts';
import { createEnterpriseRequestsApi } from './enterprise-requests';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const progress = { requestId: id, requestStatus: 'Approved', requestVersion: '3', deliveryState: 'finalized',
  workflowDefinitionVersionId: id, workflowInstanceId: id, submittedVersion: '2',
  submittedAtUtc: '2026-10-08T00:00:00Z', startedAtUtc: null, completedAtUtc: '2026-10-08T00:00:03Z' };
const notification = { intentId: id, acceptedAtUtc: '2026-10-08T00:00:04Z', totalDeliveryCount: 7,
  pendingDeliveryCount: 1, sentDeliveryCount: 2, failedDeliveryCount: 1, deadLetteredDeliveryCount: 1,
  unknownDeliveryCount: 1, otherDeliveryCount: 1, nextAttemptAtUtc: '2026-10-08T00:01:00Z' };
function api(value: unknown) { return createEnterpriseRequestsApi({ request: vi.fn().mockResolvedValue(value) } as unknown as HttpClient); }

describe('审批终态通知摘要的协议与业务边界', () => {
  it('已有送达、已读、抑制和持久化状态各自计数，不归为其他', async () => {
    const finalNotification = { ...notification, totalDeliveryCount: 11, persistedDeliveryCount: 1,
      deliveredDeliveryCount: 1, readDeliveryCount: 1, suppressedDeliveryCount: 1 };
    const response = await api({ ...progress, finalNotification }).approvalProgress(id);
    expect(response.finalNotification).toMatchObject(finalNotification);
  });
  it.each([undefined, null])('兼容旧服务缺少摘要与新服务尚未受理：%j', async finalNotification => {
    const response = await api({ ...progress, finalNotification }).approvalProgress(id);
    expect(response.requestStatus).toBe('Approved'); expect(response.finalNotification).toBe(finalNotification);
  });
  it('通知部分失败不会将业务审批终态改成失败', async () => {
    const response = await api({ ...progress, finalNotification: notification }).approvalProgress(id);
    expect(response.deliveryState).toBe('finalized'); expect(response.finalNotification?.failedDeliveryCount).toBe(1);
  });
  it('嵌套整数线格式由生成守卫规范化', async () => {
    const wire = Object.fromEntries(Object.entries(notification).map(([key, value]) => [key, key.endsWith('Count') ? String(value) : value]));
    expect((await api({ ...progress, finalNotification: wire }).approvalProgress(id)).finalNotification?.totalDeliveryCount).toBe(7);
  });
  it.each(['negative', 'sum', 'fraction', 'overflow', 'zero-id', 'accept-time', 'retry-time', 'pending-stage', 'extra-zero-date'])(
    '拒绝矛盾或非法通知摘要：%s', async kind => {
      const value = { ...progress, finalNotification: { ...notification } };
      if (kind === 'negative') value.finalNotification.pendingDeliveryCount = -1;
      if (kind === 'sum') value.finalNotification.totalDeliveryCount = 8;
      if (kind === 'fraction') value.finalNotification.sentDeliveryCount = 1.5;
      if (kind === 'overflow') value.finalNotification.totalDeliveryCount = 2147483648;
      if (kind === 'zero-id') value.finalNotification.intentId = '00000000-0000-0000-0000-000000000000';
      if (kind === 'accept-time') value.finalNotification.acceptedAtUtc = 'invalid';
      if (kind === 'retry-time') value.finalNotification.nextAttemptAtUtc = 'invalid';
      if (kind === 'extra-zero-date') { value.finalNotification.pendingDeliveryCount = 0; value.finalNotification.totalDeliveryCount = 6; }
      if (kind === 'pending-stage') Object.assign(value, { requestStatus: 'Submitted', requestVersion: '2', deliveryState: 'queued', completedAtUtc: null });
      await expect(api(value).approvalProgress(id)).rejects.toThrow();
    });
});
