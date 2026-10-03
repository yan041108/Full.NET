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
