import { describe, expect, it, vi } from 'vitest';
import type { HttpClient } from '@fullnet/client-contracts';
import { createEnterpriseRequestsApi } from './enterprise-requests';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const unitId = '019bc2b1-2a40-7cc3-8992-a80de51bf331';
const requestBody = {
  requestNumber: 'REQ-E2E', title: 'Request probe', status: 'Draft', totalAmount: 12.5,
  applicantUserId: id
};
const response = {
  ...requestBody, id, tenantId: id, organizationUnitId: unitId, version: 1,
  createdAtUtc: '2026-09-26T00:00:00Z', createdById: id, updatedAtUtc: null,
  updatedById: null, isDeleted: false, deletedAtUtc: null, deletedById: null
};

describe('企业样例请求适配', () => {
  it('详情通过生成操作读取并传递取消信号，字符串版本归一化', async () => {
    const request = vi.fn().mockResolvedValue({ ...response, version: '3' });
    const signal = new AbortController().signal;
    const value = await createEnterpriseRequestsApi({ request } as unknown as HttpClient).get(id, signal);
    expect(value.version).toBe(3);
    expect(request.mock.calls[0]![0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${id}`);
    expect(request.mock.calls[0]![2]).toBe(signal);
  });
  it.each([{ id }, { ...response, id: unitId }, { ...response, version: '9007199254740992' }])(
    '详情拒绝残缺、错配或越界响应 %#', async value => {
      const request = vi.fn().mockResolvedValue(value);
      await expect(createEnterpriseRequestsApi({ request } as unknown as HttpClient).get(id)).rejects.toThrow();
    });
  it('提交审批使用生成操作的 POST 与取消参数，不发送正文', async () => {
    const request = vi.fn().mockResolvedValue({ ...response, status: 'Submitted' });
    const signal = new AbortController().signal;
    await createEnterpriseRequestsApi({ request } as unknown as HttpClient).submitForApproval(id, signal);
    expect(request.mock.calls[0]![0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${id}/submit-for-approval`);
    expect(request.mock.calls[0]![1]).toEqual({ method: 'POST' });
    expect(request.mock.calls[0]![2]).toBe(signal);
  });
  const progress = { requestId: id, requestStatus: 'Submitted', requestVersion: '2', deliveryState: 'queued',
    workflowDefinitionVersionId: unitId, workflowInstanceId: unitId, submittedVersion: '2',
    submittedAtUtc: '2026-10-08T00:00:00Z', startedAtUtc: null, completedAtUtc: null };
  it('审批进度使用生成操作与守卫，传递取消信号并规范化版本', async () => {
    const request = vi.fn().mockResolvedValue(progress); const signal = new AbortController().signal;
    const value = await createEnterpriseRequestsApi({ request } as unknown as HttpClient).approvalProgress(id, signal);
    expect(value.requestVersion).toBe(2); expect(value.submittedVersion).toBe(2);
    expect(request.mock.calls[0]![0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${id}/approval-progress`);
    expect(request.mock.calls[0]![2]).toBe(signal);
  });
  it.each([{ requestId: id }, { ...progress, requestId: unitId }, { ...progress, deliveryState: 'unknown' },
    { ...progress, requestVersion: '9007199254740992' }, { ...progress, workflowInstanceId: 'bad-id' },
    { ...progress, submittedAtUtc: 'bad-date' }, { ...progress, workflowInstanceId: null },
    { ...progress, requestVersion: '0' }, { ...progress, deliveryState: 'finalized', requestStatus: 'Draft' }])(
    '审批进度拒绝残缺、错配或非法响应 %#', async value => {
      const request = vi.fn().mockResolvedValue(value);
      await expect(createEnterpriseRequestsApi({ request } as unknown as HttpClient).approvalProgress(id)).rejects.toThrow();
    });
  it('审批响应复用完整守卫并规范化字符串版本', async () => {
    const request = vi.fn().mockResolvedValue({ ...response, status: 'Submitted', version: '2' });
    const value = await createEnterpriseRequestsApi({ request } as unknown as HttpClient).submitForApproval(id);
    expect(value.version).toBe(2);
  });
  it.each([{ id }, { ...response, id: unitId }, { ...response, version: '9007199254740992' }])(
    '拒绝残缺、身份错配或越界的审批响应 %#', async value => {
      const request = vi.fn().mockResolvedValue(value);
      await expect(createEnterpriseRequestsApi({ request } as unknown as HttpClient).submitForApproval(id)).rejects.toThrow();
    });
  it('读取服务端字符串版本后保持客户端并发版本为安全整数', async () => {
    const request = vi.fn().mockResolvedValue({ ...response, version: '1' });
    const result = await createEnterpriseRequestsApi({ request } as unknown as HttpClient)
      .create({ ...requestBody, organizationUnitId: unitId });
    expect(result.version).toBe(1);
  });
  it('将机构上下文放入约定请求头，JSON 只发送可写业务字段', async () => {
    const request = vi.fn().mockResolvedValue(response);
    const http = { request } as unknown as HttpClient;
    const input = { ...requestBody, organizationUnitId: unitId, createdById: id };
    await createEnterpriseRequestsApi(http).create(input);
    const [, init, , options] = request.mock.calls[0]!;
    expect(new Headers(options?.headers).get('X-FullNet-Organization-Unit-Id')).toBe(unitId);
    expect(JSON.parse(init.body)).toEqual(requestBody);
  });
});
