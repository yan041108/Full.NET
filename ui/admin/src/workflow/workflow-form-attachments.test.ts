import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from '../api/http';
import { downloadWorkflowFormAttachment, downloadWorkflowTodoFormAttachment, uploadWorkflowFormAttachment } from './workflow-form-attachments';

vi.mock('../api/http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));

describe('workflow form attachment transport', () => {
  beforeEach(() => vi.clearAllMocks());

  it('启动上传绑定固定版本和字段，不携带客户端租户或Host目录', async () => {
    const file = new File(['review'], 'review.txt');
    const signal = new AbortController().signal;
    vi.mocked(http.request).mockResolvedValue({ fileId: '019a0000-0000-7000-8000-000000000001', originalFileName: 'review.txt', contentType: 'text/plain', sizeBytes: 6, contentHash: null });
    const result = await uploadWorkflowFormAttachment(file, 'evidence', { kind: 'start', versionId: 'version-id' }, signal);
    expect(result.fileId).toBe('019a0000-0000-7000-8000-000000000001');
    const [path, options, passedSignal] = vi.mocked(http.request).mock.calls[0]!;
    expect(path).toBe('/api/v1/workflow/definition-versions/version-id/attachment-uploads?fieldKey=evidence');
    expect(options?.method).toBe('POST'); expect((options?.body as FormData).get('file')).toBe(file); expect(passedSignal).toBe(signal);
  });

  it('本人选票上传绑定待办修订和vote表单', async () => {
    vi.mocked(http.request).mockResolvedValue({ fileId: '019a0000-0000-7000-8000-000000000001', originalFileName: 'review.png', contentType: 'image/png', sizeBytes: 6, contentHash: null });
    await uploadWorkflowFormAttachment(new File(['review'], 'review.png'), 'proof', { kind: 'todo', todoId: 'todo-id', expectedRevision: 3, formKindKey: 'vote' });
    const url = new URL(String(vi.mocked(http.request).mock.calls[0]![0]), 'https://local');
    expect(url.pathname).toBe('/api/v1/workflow/todos/todo-id/attachment-uploads');
    expect(Object.fromEntries(url.searchParams)).toEqual({ expectedRevision: '3', fieldKey: 'proof', formKindKey: 'vote' });
  });

  it('returns the shared client binary result using the workflow instance path', async () => {
    const blob = new Blob(['review']);
    const signal = new AbortController().signal;
    vi.mocked(http.request).mockResolvedValue(blob);
    vi.mocked(http.requestBlob).mockResolvedValue(blob);
    expect(await downloadWorkflowFormAttachment('instance-id', 'file-id', signal)).toBe(blob);
    expect(http.requestBlob).toHaveBeenCalledWith(
      '/api/v1/workflow/instances/instance-id/form-attachments/file-id/content',
      { method: 'GET' }, signal
    );
  });

  it('个人选票读取绑定实际Todo，不依赖实例查询权限', async () => {
    const blob = new Blob(['private']); vi.mocked(http.requestBlob).mockResolvedValue(blob);
    expect(await downloadWorkflowTodoFormAttachment('todo-id', 'file-id')).toBe(blob);
    expect(http.requestBlob).toHaveBeenCalledWith('/api/v1/workflow/todos/todo-id/form-attachments/file-id/content', { method: 'GET', headers: { accept: 'application/octet-stream' } }, undefined);
  });
});
