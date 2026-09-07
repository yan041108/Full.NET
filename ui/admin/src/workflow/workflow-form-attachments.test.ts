import { beforeEach, describe, expect, it, vi } from 'vitest';
import { uploadHostFile } from '../api/host-files';
import { http } from '../api/http';
import { downloadWorkflowFormAttachment, uploadWorkflowFormAttachment } from './workflow-form-attachments';

vi.mock('../api/host-files', () => ({ uploadHostFile: vi.fn(), downloadHostFileContent: vi.fn() }));
vi.mock('../api/http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));

describe('workflow form attachment transport', () => {
  beforeEach(() => vi.clearAllMocks());

  it('passes cancellation in the upload signal slot rather than the folder slot', async () => {
    const file = new File(['review'], 'review.txt');
    const signal = new AbortController().signal;
    vi.mocked(uploadHostFile).mockResolvedValue({ id: 'file-id' } as Awaited<ReturnType<typeof uploadHostFile>>);
    expect(await uploadWorkflowFormAttachment(file, signal)).toBe('file-id');
    expect(uploadHostFile).toHaveBeenCalledWith(file, undefined, signal);
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
});
