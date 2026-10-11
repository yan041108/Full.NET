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
  it('附件生成操作传递版本、文件和受保护 Blob 下载参数', async () => {
    const attachment = { id: unitId, fileId: id, originalFileName: 'probe.txt', sizeBytes: '12', createdAtUtc: '2026-10-09T00:00:00Z' };
    const request = vi.fn().mockResolvedValueOnce({ requestId: id, requestVersion: '2', requestStatus: 'Draft', items: [attachment] })
      .mockResolvedValueOnce({ requestId: id, requestVersion: '3', attachment }).mockResolvedValueOnce({ requestId: id, requestVersion: '4' });
    const requestBlob = vi.fn().mockResolvedValue(new Blob(['probe'])); const signal = new AbortController().signal;
    const api = createEnterpriseRequestsApi({ request, requestBlob } as unknown as HttpClient);
    expect((await api.attachments(id, signal)).items[0]!.sizeBytes).toBe(12);
    await api.uploadAttachment(id, 2, new File(['probe'], 'probe.txt'), signal);
    expect((request.mock.calls[1]![1].body as FormData).get('version')).toBe('2');
    await api.removeAttachment(id, unitId, 3, signal);
    expect(JSON.parse(request.mock.calls[2]![1].body)).toEqual({ version: 3 });
    await api.downloadAttachment(id, unitId, signal);
    expect(requestBlob.mock.calls[0]![0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${id}/attachments/${unitId}/content`);
    expect(requestBlob.mock.calls[0]![2]).toBe(signal);
  });
  it.each(['identity', 'path', 'bytes', 'duplicate', 'time'])('附件引用拒绝 %s 错配', async kind => {
    const item = { id: unitId, fileId: id, originalFileName: kind === 'path' ? '../probe.txt' : 'probe.txt',
      sizeBytes: kind === 'bytes' ? '10485761' : '12', createdAtUtc: kind === 'time' ? 'invalid' : '2026-10-09T00:00:00Z' };
    const request = vi.fn().mockResolvedValue({ requestId: kind === 'identity' ? unitId : id, requestVersion: '2', requestStatus: 'Draft', items: kind === 'duplicate' ? [item, item] : [item] });
    await expect(createEnterpriseRequestsApi({ request } as unknown as HttpClient).attachments(id)).rejects.toThrow();
  });
  it('明细读取与替换通过生成操作传递主表版本、精确金额和取消信号', async () => {
    const lines = { requestId: id, requestVersion: '3', requestStatus: 'Draft', totalAmount: '50.01', items: [
      { id: unitId, lineNumber: 1, itemDescription: 'Item', quantity: '1.00010', unitPrice: '50.000', lineAmount: '50.01' }
    ] };
    const request = vi.fn().mockResolvedValue(lines); const signal = new AbortController().signal;
    const api = createEnterpriseRequestsApi({ request } as unknown as HttpClient);
    expect((await api.lines(id, signal)).requestVersion).toBe(3);
    expect(request.mock.calls[0]![2]).toBe(signal);
    const body = { version: 2, items: [{ itemDescription: 'Item', quantity: '1.0001', unitPrice: '50' }] };
    await api.replaceLines(id, body, signal);
    expect(request.mock.calls[1]![0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${id}/lines`);
    expect(request.mock.calls[1]![1].method).toBe('PUT');
    expect(JSON.parse(request.mock.calls[1]![1].body)).toEqual(body);
  });
  it.each(['identity', 'version', 'total', 'amount', 'precision', 'duplicate', 'number'])('明细拒绝 %s 错配', async kind => {
    const line = { id: unitId, lineNumber: 1, itemDescription: 'Item', quantity: '1', unitPrice: '50', lineAmount: '50.00' };
    const value = { requestId: kind === 'identity' ? unitId : id, requestVersion: kind === 'version' ? '9007199254740992' : '2', requestStatus: 'Draft',
      totalAmount: kind === 'total' ? '51' : '50', items: [{ ...line, lineAmount: kind === 'amount' ? '49' : line.lineAmount,
        quantity: kind === 'precision' ? '1.00001' : line.quantity, lineNumber: kind === 'number' ? 2 : 1 }] };
    if (kind === 'duplicate') value.items.push({ ...line, lineNumber: 2 });
    const request = vi.fn().mockResolvedValue(value);
    await expect(createEnterpriseRequestsApi({ request } as unknown as HttpClient).lines(id)).rejects.toThrow();
  });
  it('替换回执必须对应下一版本，不能将旧快照当保存成功', async () => {
    const request = vi.fn().mockResolvedValue({ requestId: id, requestVersion: '1', requestStatus: 'Draft', totalAmount: '0', items: [] });
    await expect(createEnterpriseRequestsApi({ request } as unknown as HttpClient).replaceLines(id, { version: 1, items: [] })).rejects.toThrow();
  });

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
