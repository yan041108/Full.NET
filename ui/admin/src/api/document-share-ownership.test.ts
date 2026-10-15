import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { batchCreateDocumentShares, createDocumentShare, updateDocumentShareStatus } from './document-shares';
import { documentShare as share, otherShareId as other } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
const request = vi.mocked(http.request);
const batch = { succeededCount: 1, results: [{ documentId: share.documentId, succeeded: true, share, errorCode: null, message: null }, { documentId: other, succeeded: false, share: null, errorCode: 'denied', message: '拒绝' }] };
beforeEach(() => vi.resetAllMocks());
describe('文档分享响应归属', () => {
    it('创建拒绝其他文档的合法分享', async () => { request.mockResolvedValue({ ...share, documentId: other }); await expect(createDocumentShare({ documentId: share.documentId, validDays: 7 })).rejects.toThrow(/^client\.invalid_document_share_identity$/u); });
    it('状态更新拒绝其他分享', async () => { request.mockResolvedValue({ ...share, id: other }); await expect(updateDocumentShareStatus(share.id, { isEnabled: false, version: 1 })).rejects.toThrow(/^client\.invalid_document_share_identity$/u); });
    it('单项创建及状态更新接受UUID大小写等价并传递信号', async () => { const signal = new AbortController().signal; request.mockResolvedValueOnce({ ...share, id: share.id.toUpperCase(), documentId: share.documentId.toUpperCase() }).mockResolvedValueOnce({ ...share, id: share.id.toUpperCase(), documentId: share.documentId.toUpperCase(), version: 2 }); await expect(createDocumentShare({ documentId: share.documentId, validDays: 7 }, signal)).resolves.toMatchObject({ id: share.id.toUpperCase() }); await expect(updateDocumentShareStatus(share.id, { isEnabled: true, version: 1 }, signal)).resolves.toMatchObject({ id: share.id.toUpperCase() }); expect(request.mock.calls.every(call => call[2] === signal)).toBe(true); });
    it.each([
        { ...batch, succeededCount: 2 },
        { ...batch, results: batch.results.slice(0, 1) },
        { ...batch, results: [batch.results[0], batch.results[0]] },
        { ...batch, results: [{ ...batch.results[0], share: { ...share, documentId: other } }, batch.results[1]] },
        { ...batch, results: [{ ...batch.results[0], share: null }, batch.results[1]] },
        { ...batch, results: [batch.results[0], { ...batch.results[1], share }] },
        { ...batch, results: [{ ...batch.results[0], share: { ...share, password: 'leaked' } }, batch.results[1]] }
    ])('批量拒绝身份、成功计数或嵌套分享错配 %j', async (value) => { request.mockResolvedValue(value); await expect(batchCreateDocumentShares({ documentIds: [share.documentId, other], validDays: 7 })).rejects.toThrow(/^client\.invalid_document_share_batch$/u); });
    it('正常部分成功允许返回，结果乱序和UUID大小写等价', async () => { request.mockResolvedValue({ ...batch, results: [batch.results[1], { ...batch.results[0], documentId: share.documentId.toUpperCase(), share: { ...share, documentId: share.documentId.toUpperCase() } }] }); const signal = new AbortController().signal; await expect(batchCreateDocumentShares({ documentIds: [share.documentId, other], validDays: 7 }, signal)).resolves.toMatchObject({ succeededCount: 1 }); expect(request.mock.calls[0]?.[2]).toBe(signal); });
    it.each([{ isEnabled: true, version: 2 }, { isEnabled: false, version: 1 }, { isEnabled: false, version: 3 }])('状态更新不能接受状态或递增修订错配 %j', async change => {
        request.mockResolvedValue({ ...share, ...change });
        await expect(updateDocumentShareStatus(share.id, { isEnabled: false, version: 1 })).rejects.toThrow(/^client\.invalid_document_share_identity$/u);
    });
    it('请求对象在途修改后仍按原启停值和修订核对响应', async () => {
        const pending = deferred<typeof share>(); request.mockReturnValueOnce(pending.promise);
        const body = { isEnabled: false, version: 1 }; const result = updateDocumentShareStatus(share.id, body);
        body.isEnabled = true; body.version = 9; pending.resolve({ ...share, isEnabled: false, version: 2 });
        await expect(result).resolves.toMatchObject({ isEnabled: false, version: 2 });
        expect(JSON.parse(request.mock.calls[0]?.[1]?.body as string)).toEqual({ isEnabled: false, version: 1 });
    });
    it('在途改写的请求对象不能为其他状态和修订回包背书', async () => {
        const pending = deferred<typeof share>(); request.mockReturnValueOnce(pending.promise);
        const body = { isEnabled: false, version: 1 }; const result = updateDocumentShareStatus(share.id, body);
        body.isEnabled = true; body.version = 9; pending.resolve({ ...share, isEnabled: true, version: 10 });
        await expect(result).rejects.toThrow(/^client\.invalid_document_share_identity$/u);
    });
});
