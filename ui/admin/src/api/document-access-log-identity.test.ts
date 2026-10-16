import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listDocumentAccessLogs } from './document-access-logs';
import { shareId, otherShareId } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const log = { id: shareId, documentItemId: otherShareId, documentTitle: '文档', accessTypeKey: 'download', sourceKey: 'share', actorUserId: null, occurredAtUtc: '2026-10-16T00:00:00Z', clientIpFingerprint: null };
const page = { items: [log], page: 1, pageSize: 20, total: 1 };
beforeEach(() => vi.resetAllMocks());
describe('文档访问日志分页与过滤归属', () => {
  it.each([{ page: 2 }, { pageSize: 10 }, { total: -1 }, { total: Number.MAX_SAFE_INTEGER + 1 }, { items: [log, { ...log, id: shareId.toUpperCase() }] }, { items: [{ ...log, id: '00000000-0000-0000-0000-000000000000' }] }, { items: Array.from({ length: 21 }, (_, index) => ({ ...log, id: '00000000-0000-0000-0000-' + String(index + 1).padStart(12, '0') })) }])('拒绝错误分页、总数、重复或空身份 %j', async change => {
    vi.mocked(http.request).mockResolvedValue({ ...page, ...change }); await expect(listDocumentAccessLogs()).rejects.toThrow(/^client.invalid_document_access_log_page$/u);
  });
  it.each([{ documentItemId: shareId }, { accessTypeKey: 'preview' }, { sourceKey: 'authenticated' }])('拒绝不符合已提交过滤的日志 %j', async filters => {
    vi.mocked(http.request).mockResolvedValue(page); await expect(listDocumentAccessLogs(1, 20, filters)).rejects.toThrow(/^client.invalid_document_access_log_page$/u);
  });
  it('UUID大小写和过滤前后空白允许等价匹配', async () => {
    vi.mocked(http.request).mockResolvedValue(page); await expect(listDocumentAccessLogs(1, 20, { documentItemId: ' ' + otherShareId.toUpperCase() + ' ', accessTypeKey: ' download ', sourceKey: ' share ' })).resolves.toEqual(page);
  });
  it('响应归属核对使用原过滤副本', async () => {
    const pending = deferred<unknown>(); vi.mocked(http.request).mockReturnValueOnce(pending.promise); const filters = { documentItemId: otherShareId }, reading = listDocumentAccessLogs(1, 20, filters); filters.documentItemId = shareId; pending.resolve(page); await expect(reading).resolves.toEqual(page);
  });
  it.each([{ page: 0 }, { page: 1.5 }, { pageSize: 101 }, { documentItemId: 'invalid' }, { documentItemId: '00000000-0000-0000-0000-000000000000' }])('无效输入HTTP前拒绝 %j', async input => {
    await expect(listDocumentAccessLogs('page' in input ? input.page : 1, 'pageSize' in input ? input.pageSize : 20, 'documentItemId' in input ? { documentItemId: input.documentItemId } : {})).rejects.toThrow(/^client.invalid_document_access_log_query$/u); expect(http.request).not.toHaveBeenCalled();
  });
});
