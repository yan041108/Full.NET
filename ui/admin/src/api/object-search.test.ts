import { beforeEach, expect, it, vi } from 'vitest';
import { http } from './http';
import { searchEnterpriseRequests, searchHostDocuments } from './object-search';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const result = () => ({ items: [{ id, title: 'Literal_%[~', number: 'REQ-1' }], page: 1, hasMore: false, isTruncated: false, windowDays: 90 });
beforeEach(() => { vi.mocked(http.request).mockReset(); });
it('两种来源使用生成操作并保留字面关键词、取消信号和固定路径', async () => {
  vi.mocked(http.request).mockResolvedValue(result()); const signal = new AbortController().signal;
  await searchHostDocuments('_%[~', 1, signal); await searchEnterpriseRequests('_%[~', 1, signal);
  for (const [index, path] of ['/api/v1/document/host/items/search', '/api/v1/enterprise_request/enterprise-requests/search'].entries()) {
    const call = vi.mocked(http.request).mock.calls[index]!; const url = new URL(call[0], 'https://example.test');
    expect(url.pathname).toBe(path); expect(url.searchParams.get('q')).toBe('_%[~'); expect(url.searchParams.get('page')).toBe('1');
    expect(call[1]).toEqual({ method: 'GET' }); expect(call[2]).toBe(signal);
  }
});
it('基础生成校验与语义上限都失败关闭，不吞掉来源拒绝', async () => {
  const signal = new AbortController().signal; vi.mocked(http.request).mockResolvedValue({});
  await expect(searchHostDocuments('Find', 1, signal)).rejects.toThrow();
  vi.mocked(http.request).mockResolvedValue({ ...result(), page: 2 }); await expect(searchEnterpriseRequests('Find', 1, signal)).rejects.toThrow('client.invalid_object_search_page');
  vi.mocked(http.request).mockRejectedValue({ status: 403 }); await expect(searchHostDocuments('Find', 1, signal)).rejects.toEqual({ status: 403 });
});
