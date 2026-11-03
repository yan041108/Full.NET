import { describe, expect, it } from 'vitest';
import { readObjectSearchPage } from './objectSearch';
import { createObjectSearchSources } from './objectSearchSources';
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const page = () => ({ items: [{ id, title: 'Literal_%[~', number: 'REQ-1' }], page: 1, hasMore: false, isTruncated: false, windowDays: 90 });
describe('来源摘要与静态装配', () => {
  it('只接受固定有界响应，不把 URL 当作对象身份', () => {
    expect(readObjectSearchPage(page(), 1).items[0]!.title).toBe('Literal_%[~');
    for (const value of [null, { ...page(), page: 2 }, { ...page(), windowDays: 365 }, { ...page(), hasMore: true },
      { ...page(), isTruncated: true }, { ...page(), items: Array(6).fill(page().items[0]) },
      { ...page(), items: [{ ...page().items[0], id: 'https://example.com' }] }, { ...page(), items: [{ ...page().items[0], title: '' }] }])
      expect(() => readObjectSearchPage(value, 1)).toThrow('client.invalid_object_search_page');
  });
  it('拒绝同一 UUID 的大小写重复及空 UUID；最后一页明确表达截断', () => {
    expect(() => readObjectSearchPage({ ...page(), items: [page().items[0], { ...page().items[0], id: id.toUpperCase() }] }, 1)).toThrow();
    expect(() => readObjectSearchPage({ ...page(), items: [{ ...page().items[0], id: '00000000-0000-0000-0000-000000000000' }] }, 1)).toThrow();
    const items = Array.from({ length: 5 }, (_, index) => ({ id: id.slice(0, -1) + index, title: 'Result', number: null }));
    expect(readObjectSearchPage({ ...page(), items, page: 20, isTruncated: true }, 20).isTruncated).toBe(true);
  });
  it('Host 只启用实际装配和授权的文档，Tenant 只启用应用业务来源', () => {
    const options = { permissions: ['document.host_documents.read', 'enterprise_request.enterprise_requests.read'], routes: new Set(['host-document-items', 'enterprise-requests']), documentLabel: 'Docs', requestLabel: 'Requests' };
    expect(createObjectSearchSources(options).map(source => source.key)).toEqual(['document']);
    expect(createObjectSearchSources({ ...options, tenantId: id }).map(source => source.key)).toEqual(['enterprise-request']);
    expect(createObjectSearchSources({ ...options, permissions: ['*'] })).toEqual([]);
    expect(createObjectSearchSources({ ...options, routes: new Set() })).toEqual([]);
  });
});
