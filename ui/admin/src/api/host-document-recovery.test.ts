import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { restoreDocumentItem, rollbackDocumentVersion, deleteDocumentVersion } from './host-document-items';
import { shareDocument as doc, otherShareId as other } from '../test/document-share-fixtures';

vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
beforeEach(() => vi.resetAllMocks());
function invoke(kind: string, signal: AbortSignal) {
  if (kind === 'restore') return restoreDocumentItem(doc.id, doc.version, signal);
  if (kind === 'rollback') return rollbackDocumentVersion(doc.id, other, doc.version, signal);
  return deleteDocumentVersion(doc.id, other, doc.version, signal);
}
describe('文档恢复与版本操作响应身份', () => {
  it.each(['restore', 'rollback', 'delete'])('%s拒绝结构合法的其他文档回包', async kind => {
    vi.mocked(http.request).mockResolvedValue({ ...doc, id: other });
    await expect(invoke(kind, new AbortController().signal)).rejects.toThrow(/^client\.invalid_document_item_identity$/u);
  });
  it.each(['restore', 'rollback', 'delete'])('%s接受UUID大小写等价并保留取消信号', async kind => {
    vi.mocked(http.request).mockResolvedValue({ ...doc, id: doc.id.toUpperCase() }); const signal = new AbortController().signal;
    await expect(invoke(kind, signal)).resolves.toMatchObject({ id: doc.id.toUpperCase() }); expect(vi.mocked(http.request).mock.calls[0]?.[2]).toBe(signal);
  });
});
