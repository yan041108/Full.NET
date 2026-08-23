import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createOrganizationPositionLevel,
  disableOrganizationPositionLevel,
  listOrganizationPositionLevels,
  updateOrganizationPositionLevel
} from './org-position-levels';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const levelId = '019bc2b1-2a40-7cc3-8992-a80de51bf310';

const sampleLevel = {
  id: levelId,
  code: 'p5',
  name: 'P5',
  displayOrder: 10,
  isActive: true,
  createdAtUtc: '2026-07-25T08:00:00.000Z',
  updatedAtUtc: null,
  version: 1
};

describe('Vue 租户职级 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleLevel],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listOrganizationPositionLevels()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/organization/position-levels?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });

  it('通过 JSON 正文创建、更新并禁用职级', async () => {
    requestMock
      .mockResolvedValueOnce(sampleLevel)
      .mockResolvedValueOnce({ ...sampleLevel, name: 'P6', version: 2 })
      .mockResolvedValueOnce({ ...sampleLevel, isActive: false, version: 3 });

    await createOrganizationPositionLevel('p5', 'P5');
    await updateOrganizationPositionLevel(levelId, 'P6', 10, 1);
    await disableOrganizationPositionLevel(levelId);

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/organization/position-levels',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ code: 'p5', name: 'P5', displayOrder: 10 })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/organization/position-levels/${levelId}`,
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ name: 'P6', displayOrder: 10, version: 1 })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/organization/position-levels/${levelId}/disable`,
      { method: 'POST' },
      undefined
    );
  });
});
