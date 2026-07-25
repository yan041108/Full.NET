import { describe, expect, it, vi, beforeEach } from 'vitest';
import { listOrganizationPositions, createOrganizationPosition } from './org-positions';

vi.mock('./http', () => ({
  request: vi.fn()
}));

import { request } from './http';

describe('org-positions api', () => {
  beforeEach(() => {
    vi.mocked(request).mockReset();
  });

  it('lists positions', async () => {
    vi.mocked(request).mockResolvedValue({
      items: [{
        id: '01912345-6789-7abc-8def-0123456789ab',
        code: 'engineer',
        name: '工程师',
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-07-25T08:00:00.000Z',
        updatedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });

    const page = await listOrganizationPositions(1, 20);
    expect(request).toHaveBeenCalledWith(
      '/api/v1/organization/positions?page=1&pageSize=20'
    );
    expect(page.items[0].code).toBe('engineer');
  });

  it('creates a position', async () => {
    vi.mocked(request).mockResolvedValue({
      id: '01912345-6789-7abc-8def-0123456789ab',
      code: 'engineer',
      name: '工程师',
      displayOrder: 10,
      isActive: true,
      createdAtUtc: '2026-07-25T08:00:00.000Z',
      updatedAtUtc: null,
      version: 1
    });

    await createOrganizationPosition('engineer', '工程师');
    expect(request).toHaveBeenCalledWith('/api/v1/organization/positions', expect.objectContaining({
      method: 'POST'
    }));
  });
});
