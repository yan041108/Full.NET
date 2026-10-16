import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { createDocumentPreviewTask, listDocumentPreviewTasks } from './document-preview-tasks';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
beforeEach(() => vi.resetAllMocks());
describe('文档预览查询与空身份边界', () => {
  it.each([
    { items: [task], page: 2, pageSize: 20, total: 1 }, { items: [task], page: 1, pageSize: 10, total: 1 },
    { items: [task, { ...task, id: task.id.toUpperCase() }], page: 1, pageSize: 20, total: 2 },
    { items: [{ ...task, id: '00000000-0000-0000-0000-000000000000' }], page: 1, pageSize: 20, total: 1 }
  ])('拒绝错误分页、重复或空任务身份 %j', async value => {
    vi.mocked(http.request).mockResolvedValue(value); await expect(listDocumentPreviewTasks()).rejects.toThrow(/^client.invalid_document_preview_task_page$/u);
  });
  it('筛选文档必须匹配，UUID大小写等价仍可使用', async () => {
    vi.mocked(http.request).mockResolvedValue({ items: [task], page: 1, pageSize: 20, total: 1 }); await expect(listDocumentPreviewTasks(1, 20, task.documentItemId.toUpperCase())).resolves.toMatchObject({ items: [task] });
    await expect(listDocumentPreviewTasks(1, 20, '019bc2b1-2a40-7cc3-8992-a80de51bf298')).rejects.toThrow(/^client.invalid_document_preview_task_page$/u);
  });
  it.each([{ documentItemId: '00000000-0000-0000-0000-000000000000', versionId: null }, { documentItemId: task.documentItemId, versionId: '00000000-0000-0000-0000-000000000000' }])('空创建身份在请求前拒绝 %j', async body => {
    await expect(createDocumentPreviewTask(body)).rejects.toThrow(/^client.invalid_create_document_preview_task_request$/u); expect(http.request).not.toHaveBeenCalled();
  });
});
