import { beforeEach, describe, expect, it, vi } from 'vitest';
import { documentHostDownloadDocumentPreviewTaskContent } from '@fullnet/client-contracts';
import { openDocumentPreviewTaskContent } from './document-preview-tasks';
import { openDocumentBlob } from './host-document-items';
vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(), documentHostDownloadDocumentPreviewTaskContent: vi.fn()
}));
vi.mock('./http', () => ({ http: {} }));
vi.mock('./host-document-items', () => ({ openDocumentBlob: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
describe('PDF 预览最终打开边界', () => {
  it('下载返回前取消，即使下载器返回 Blob 也不打开', async () => {
    const controller = new AbortController();
    vi.mocked(documentHostDownloadDocumentPreviewTaskContent).mockImplementation(async () => { controller.abort(); return new Blob(['private']); });
    await expect(openDocumentPreviewTaskContent('fixture', controller.signal)).rejects.toMatchObject({ name: 'AbortError' });
    expect(openDocumentBlob).not.toHaveBeenCalled();
  });
  it('有效下载只打开一次并传递取消信号', async () => {
    const blob = new Blob(['pdf']); const controller = new AbortController();
    vi.mocked(documentHostDownloadDocumentPreviewTaskContent).mockResolvedValue(blob);
    await openDocumentPreviewTaskContent('fixture', controller.signal);
    expect(documentHostDownloadDocumentPreviewTaskContent).toHaveBeenCalledWith({}, { taskId: 'fixture' }, controller.signal);
    expect(openDocumentBlob).toHaveBeenCalledExactlyOnceWith(blob);
  });
});
