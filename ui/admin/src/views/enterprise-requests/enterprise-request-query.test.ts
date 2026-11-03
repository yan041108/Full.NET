import { describe, expect, it, vi } from 'vitest';
import type { HttpClient } from '@fullnet/client-contracts';
import { defaultEnterpriseRequestQuery, enterpriseRequestQueryHttp } from './enterprise-request-query';

describe('企业申请查询方案派发', () => {
  it('只将受控筛选和排序追加到原列表读取，保留分页及取消信号', async () => {
    const request = vi.fn().mockResolvedValue({}); const configuration = defaultEnterpriseRequestQuery();
    configuration.filters = [{ fieldKey: 'title', operatorKey: 'contains', value: 'Literal_%[~' }]; configuration.descending = true;
    const client = enterpriseRequestQueryHttp({ request } as unknown as HttpClient, () => configuration); const signal = new AbortController().signal;
    await client.request('/api/v1/enterprise_request/enterprise-requests?page=2&pageSize=20', { method: 'GET' }, signal);
    const query = new URL(request.mock.calls[0]![0], 'https://localhost').searchParams;
    expect(query.get('page')).toBe('2'); expect(query.get('filterTitle')).toBe('Literal_%[~');
    expect(query.get('descending')).toBe('true'); expect(request.mock.calls[0]![2]).toBe(signal);
  });
  it('不把方案条件放进创建、详情、提交或其他模块操作', async () => {
    const request = vi.fn().mockResolvedValue({}), client = enterpriseRequestQueryHttp({ request } as unknown as HttpClient, defaultEnterpriseRequestQuery);
    for (const [path, method] of [['/api/v1/enterprise_request/enterprise-requests', 'POST'], ['/api/v1/enterprise_request/enterprise-requests/id', 'GET'], ['/api/v1/reporting/export-tasks', 'GET']]) {
      const init = { method, body: method === 'POST' ? '{}' : undefined }; await client.request(path!, init); expect(request.mock.lastCall![0]).toBe(path); expect(request.mock.lastCall![1]).toBe(init);
    }
  });
  it('未知字段和运算符不能借方案扩展原接口', () => {
    const value = defaultEnterpriseRequestQuery(), request = vi.fn(); const client = enterpriseRequestQueryHttp({ request } as unknown as HttpClient, () => value);
    value.filters = [{ fieldKey: 'tenantId', operatorKey: 'contains', value: 'other' }];
    expect(() => client.request('/api/v1/enterprise_request/enterprise-requests')).toThrow(); expect(request).not.toHaveBeenCalled();
    value.filters = []; value.sortBy = 'id;DROP'; expect(() => client.request('/api/v1/enterprise_request/enterprise-requests')).toThrow();
  });
});
