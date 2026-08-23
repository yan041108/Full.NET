import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createOrganizationUnit,
  disableOrganizationUnit,
  listOrganizationUnits,
  updateOrganizationUnit
} from './org-units';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const sampleUnit = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf290',
  parentId: null,
  code: 'hq',
  name: '总部',
  displayOrder: 10,
  isActive: true,
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

const parentUnitId = '019bc2b1-2a40-7cc3-8992-a80de51bf291';

describe('Vue 租户机构 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleUnit],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listOrganizationUnits()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/organization/units?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });

  it('通过 JSON 正文创建、更新并禁用机构', async () => {
    requestMock
      .mockResolvedValueOnce(sampleUnit)
      .mockResolvedValueOnce({ ...sampleUnit, name: '新名称', version: 2 })
      .mockResolvedValueOnce({ ...sampleUnit, isActive: false, version: 3 });

    await createOrganizationUnit('hq', '总部', 10, parentUnitId);
    await updateOrganizationUnit(sampleUnit.id, '新名称', 10, 1, parentUnitId);
    await disableOrganizationUnit(sampleUnit.id);

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/organization/units',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          parentId: parentUnitId,
          code: 'hq',
          name: '总部',
          displayOrder: 10
        })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/organization/units/${sampleUnit.id}`,
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          parentId: parentUnitId,
          name: '新名称',
          displayOrder: 10,
          version: 1
        })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/organization/units/${sampleUnit.id}/disable`,
      { method: 'POST' },
      undefined
    );
  });
});
