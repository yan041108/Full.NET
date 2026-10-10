import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { createDocumentPreviewTask, getDocumentPreviewTask } from './document-preview-tasks';
import { getDocumentSharePreviewTaskByCode } from './document-shares';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
const request = vi.mocked(http.request);
const other = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
beforeEach(() => vi.resetAllMocks());
describe('文档预览任务响应归属', () => {
  it.each([['后台', (signal: AbortSignal) => getDocumentPreviewTask(task.id, signal)],
    ['公开分享', (signal: AbortSignal) => getDocumentSharePreviewTaskByCode('SHARE', task.id, { password: 'secret' }, signal)]])(
    '%s详情拒绝结构合法的其他任务', async (_, read) => {
      request.mockResolvedValue({ ...task, id: other });
      await expect(read(new AbortController().signal)).rejects.toThrow(/^client\.invalid_document_preview_task_identity$/u);
    });
  it.each([['后台', (signal: AbortSignal) => getDocumentPreviewTask(task.id, signal)],
    ['公开分享', (signal: AbortSignal) => getDocumentSharePreviewTaskByCode('SHARE', task.id, {}, signal)]])(
    '%s详情接受UUID大小写等价并保持信号', async (_, read) => {
      request.mockResolvedValue({ ...task, id: task.id.toUpperCase() }); const signal = new AbortController().signal;
      await expect(read(signal)).resolves.toMatchObject({ id: task.id.toUpperCase() });
      expect(request.mock.calls[0]?.[2]).toBe(signal);
    });
  it.each([{ documentItemId: other }, { versionId: other }])('创建拒绝文档或版本错配 %j', async change => {
    request.mockResolvedValue({ ...task, ...change });
    await expect(createDocumentPreviewTask({ documentItemId: task.documentItemId })).rejects.toThrow(/^client\.invalid_document_preview_task_identity$/u);
  });
  it('指定版本也必须精确匹配，UUID大小写兼容', async () => {
    request.mockResolvedValueOnce({ ...task, versionId: null }).mockResolvedValueOnce({ ...task, documentItemId: task.documentItemId.toUpperCase(), versionId: other.toUpperCase() });
    await expect(createDocumentPreviewTask({ documentItemId: task.documentItemId, versionId: other })).rejects.toThrow(/^client\.invalid_document_preview_task_identity$/u);
    const signal = new AbortController().signal;
    await expect(createDocumentPreviewTask({ documentItemId: task.documentItemId, versionId: other }, signal)).resolves.toMatchObject({ versionId: other.toUpperCase() });
    expect(request.mock.calls[1]?.[2]).toBe(signal);
  });
  it('空版本接受当前文件任务，显式null与省略保持等价', async () => {
    request.mockResolvedValue(task);
    await expect(createDocumentPreviewTask({ documentItemId: task.documentItemId })).resolves.toEqual(task);
    await expect(createDocumentPreviewTask({ documentItemId: task.documentItemId, versionId: null })).resolves.toEqual(task);
  });
});
