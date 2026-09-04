import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listMyWorkflowCc, markWorkflowCcRead } from './workflow-cc';

vi.mock('./http', () => ({ http: { request: vi.fn() } }));

describe('workflow cc api', () => {
  beforeEach(() => vi.mocked(http.request).mockReset());

  it('查询本人抄送并标记单条已读', async () => {
    vi.mocked(http.request)
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce({
        id: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001',
        readAtUtc: '2026-09-04T00:00:00Z'
      });

    await listMyWorkflowCc();
    await markWorkflowCcRead('019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001');

    expect(http.request).toHaveBeenNthCalledWith(
      1,
      '/api/v1/workflow/cc/mine',
      { method: 'GET' },
      undefined
    );
    expect(http.request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/workflow/cc/019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001/read',
      { method: 'POST' },
      undefined
    );
  });
});
