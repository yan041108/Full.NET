import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getDocumentPreviewCapabilities, cancelDocumentPreviewTask, retryDocumentPreviewTask, openDocumentPreviewTaskContent } from './document-preview-tasks';
import { openDocumentBlob } from './host-document-items';
vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
vi.mock('./host-document-items', () => ({ openDocumentBlob: vi.fn() }));
const request = vi.mocked(http.request);
const id = '01912345-6789-7abc-8def-0123456789ab';
const task = { id, documentItemId: id, documentTitle: 'Preview', versionId: id, sourceFileId: id, outputFileId: null,
  statusKey: 'pending', providerKey: 'external_http', errorCode: null, requestedByUserId: id,
  createdAtUtc: '2026-10-24T00:00:00Z', startedAtUtc: null, completedAtUtc: null, version: 3, attemptCount: 1, nextAttemptAtUtc: null };
describe('preview lifecycle API', () => {
  beforeEach(() => { request.mockReset(); vi.mocked(http.requestBlob).mockReset(); vi.mocked(openDocumentBlob).mockReset(); });
  it('reads server MIME and enabled capability from the public route', async () => {
    request.mockResolvedValueOnce({ enabled: false, providerKey: 'disabled', allowedMimeTypes: ['application/msword'], maxInputBytes: 52428800, maxAttempts: 3 });
    await expect(getDocumentPreviewCapabilities()).resolves.toMatchObject({ enabled: false });
    expect(request.mock.calls[0]?.[0]).toBe('/api/v1/document/public/preview-capabilities');
    expect(request.mock.calls[0]?.[3]).toEqual({ retryUnauthorized: false, skipAuthentication: true });
  });
  it('cancels with the task version and creates an independent retry', async () => {
    request.mockResolvedValueOnce({ ...task, statusKey: 'cancelled', version: 4 }).mockResolvedValueOnce({ ...task, id: '01912345-6789-7abc-8def-0123456789ac', attemptCount: 0 });
    await cancelDocumentPreviewTask(task); await retryDocumentPreviewTask(id);
    expect(request).toHaveBeenNthCalledWith(1, `/api/v1/document/host/preview-tasks/${id}/cancel`, expect.objectContaining({ method: 'POST', body: JSON.stringify({ version: 3 }) }), undefined);
    expect(request).toHaveBeenNthCalledWith(2, `/api/v1/document/host/preview-tasks/${id}/retry`, expect.objectContaining({ method: 'POST' }), undefined);
  });
  it('rejects incomplete capability replies', async () => {
    request.mockResolvedValueOnce({ enabled: true });
    await expect(getDocumentPreviewCapabilities()).rejects.toThrow();
  });
  it('拒绝未取消、错误目标、陈旧版本与复用原任务的重试响应', async () => {
    for (const value of [task, { ...task, id: '01912345-6789-7abc-8def-0123456789ac', statusKey: 'cancelled' }, { ...task, statusKey: 'cancelled', version: 2 }]) {
      request.mockResolvedValueOnce(value); await expect(cancelDocumentPreviewTask(task)).rejects.toThrow();
    }
    request.mockResolvedValueOnce(task); await expect(retryDocumentPreviewTask(id)).rejects.toThrow();
  });
  it('取消与重试传递作用域信号，失败不自行重放', async () => {
    const signal = new AbortController().signal, error = { status: 409, code: 'document.version_conflict' }; request.mockRejectedValue(error);
    await expect(cancelDocumentPreviewTask(task, signal)).rejects.toEqual(error); expect(request.mock.calls[0]?.[2]).toBe(signal); expect(request).toHaveBeenCalledTimes(1);
    await expect(retryDocumentPreviewTask(id, signal)).rejects.toEqual(error); expect(request.mock.calls[1]?.[2]).toBe(signal); expect(request).toHaveBeenCalledTimes(2);
  });
  it('切换上下文后迟到的 PDF 不再创建预览窗口', async () => {
    let resolve!: (value: Blob) => void; vi.mocked(http.requestBlob).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const controller = new AbortController(); const pending = openDocumentPreviewTaskContent(id, controller.signal); controller.abort(); resolve(new Blob(['private PDF']));
    await pending; expect(openDocumentBlob).not.toHaveBeenCalled(); expect(http.requestBlob).toHaveBeenCalledTimes(1);
  });
});
