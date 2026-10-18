import { beforeEach, describe, expect, it, vi } from 'vitest';
import { documentHostCancelDocumentPreviewTask, documentHostDownloadDocumentPreviewTaskContent } from '@fullnet/client-contracts';
import { cancelDocumentPreviewTask, openDocumentPreviewTaskContent } from './document-preview-tasks';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
import { openDocumentBlob } from './host-document-items';
vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(), documentHostCancelDocumentPreviewTask: vi.fn(), documentHostDownloadDocumentPreviewTaskContent: vi.fn()
}));
vi.mock('./http', () => ({ http: {} }));
vi.mock('./host-document-items', () => ({ openDocumentBlob: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
describe('PDF 预览最终打开边界', () => {
  it('取消透传 UUID 和信号，大小写等价任务可以接入', async () => {
    const signal = new AbortController().signal;
    vi.mocked(documentHostCancelDocumentPreviewTask).mockResolvedValue({ ...task, statusKey: 'cancelled' });
    expect((await cancelDocumentPreviewTask(task.id.toUpperCase(), signal)).statusKey).toBe('cancelled');
    expect(documentHostCancelDocumentPreviewTask).toHaveBeenCalledWith({}, { taskId: task.id.toUpperCase() }, signal);
  });
  it.each(['identity', 'status', 'shape'])('取消响应%s不符不能作为成功', async mismatch => {
    const result = mismatch === 'shape' ? {} : { ...task, statusKey: mismatch === 'status' ? 'pending' : 'cancelled',
      id: mismatch === 'identity' ? '01980000-0000-7000-8000-000000000003' : task.id };
    vi.mocked(documentHostCancelDocumentPreviewTask).mockResolvedValue(result as typeof task);
    await expect(cancelDocumentPreviewTask(task.id)).rejects.toThrow('client.invalid_document_preview_task_cancellation');
  });
  it('保留领取冲突，不把取消失败当作终态成功', async () => {
    const error = { status: 409, title: 'claimed', code: 'document.host_preview_task.status_invalid' };
    vi.mocked(documentHostCancelDocumentPreviewTask).mockRejectedValue(error);
    await expect(cancelDocumentPreviewTask(task.id)).rejects.toBe(error);
  });
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
