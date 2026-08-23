import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  assignOrganizationPositionLevel,
  assignOrganizationPositionUnit,
  createOrganizationPosition,
  listOrganizationPositions
} from './org-positions';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const positionId = '01912345-6789-7abc-8def-0123456789ab';

const samplePosition = {
  id: positionId,
  code: 'engineer',
  name: '工程师',
  unitId: null,
  unitCode: null,
  unitName: null,
  positionLevelId: null,
  positionLevelCode: null,
  positionLevelName: null,
  displayOrder: 10,
  isActive: true,
  createdAtUtc: '2026-07-25T08:00:00.000Z',
  updatedAtUtc: null,
  version: 1
};

describe('org-positions api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists positions', async () => {
    requestMock.mockResolvedValue({
      items: [samplePosition],
      page: 1,
      pageSize: 20,
      total: 1
    });

    const page = await listOrganizationPositions(1, 20);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/organization/positions?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
    expect(page.items[0].code).toBe('engineer');
  });

  it('creates a position', async () => {
    requestMock.mockResolvedValue(samplePosition);

    await createOrganizationPosition('engineer', '工程师');
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/organization/positions',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ code: 'engineer', name: '工程师', displayOrder: 10 })
      }),
      undefined
    );
  });

  it('binds and unbinds a position unit with optimistic concurrency', async () => {
    requestMock.mockResolvedValue({
      ...samplePosition,
      updatedAtUtc: '2026-07-29T08:00:00.000Z',
      version: 2
    });

    await assignOrganizationPositionUnit(positionId, null, 1);

    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/organization/positions/${positionId}/unit`,
      {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ unitId: null, version: 1 })
      },
      undefined
    );
  });

  it('binds and unbinds a position level with optimistic concurrency', async () => {
    requestMock.mockResolvedValue({
      ...samplePosition,
      updatedAtUtc: '2026-07-29T08:00:00.000Z',
      version: 2
    });

    await assignOrganizationPositionLevel(positionId, null, 1);

    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/organization/positions/${positionId}/position-level`,
      {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ positionLevelId: null, version: 1 })
      },
      undefined
    );
  });
});
