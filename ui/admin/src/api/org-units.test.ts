import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createOrganizationUnit,
  disableOrganizationUnit,
  listOrganizationUnits,
  updateOrganizationUnit
} from './org-units';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sampleUnit = {
  id: 'unit-id',
  parentId: null,
  code: 'hq',
  name: '总部',
  displayOrder: 10,
  isActive: true,
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

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
      '/api/v1/organization/units?page=1&pageSize=20'
    );
  });

  it('通过 JSON 正文创建、更新并禁用机构', async () => {
    requestMock
      .mockResolvedValueOnce(sampleUnit)
      .mockResolvedValueOnce({ ...sampleUnit, name: '新名称', version: 2 })
      .mockResolvedValueOnce({ ...sampleUnit, isActive: false, version: 3 });

    await createOrganizationUnit('hq', '总部', 10, 'parent-id');
    await updateOrganizationUnit('unit-id', '新名称', 10, 1, 'parent-id');
    await disableOrganizationUnit('unit-id');

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/organization/units',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          parentId: 'parent-id',
          code: 'hq',
          name: '总部',
          displayOrder: 10
        })
      })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/organization/units/unit-id',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          parentId: 'parent-id',
          name: '新名称',
          displayOrder: 10,
          version: 1
        })
      })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      '/api/v1/organization/units/unit-id/disable',
      expect.objectContaining({ method: 'POST' })
    );
  });
});
