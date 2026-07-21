import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createOrganizationUserUnit,
  disableOrganizationUserUnit,
  listOrganizationUserUnits,
  updateOrganizationUserUnit
} from './org-user-units';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sampleAssignment = {
  id: 'assignment-id',
  userId: 'user-id',
  username: 'admin',
  displayName: '系统管理员',
  unitId: 'unit-id',
  unitCode: 'hq',
  unitName: '总部',
  isPrimary: false,
  isActive: true,
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('Vue 租户用户-机构隶属 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleAssignment],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listOrganizationUserUnits()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/organization/user-units?page=1&pageSize=20'
    );
  });

  it('通过 JSON 正文创建、更新并取消隶属', async () => {
    requestMock
      .mockResolvedValueOnce(sampleAssignment)
      .mockResolvedValueOnce({ ...sampleAssignment, isPrimary: true, version: 2 })
      .mockResolvedValueOnce({
        ...sampleAssignment,
        isPrimary: false,
        isActive: false,
        version: 3
      });

    await createOrganizationUserUnit('user-id', 'unit-id');
    await updateOrganizationUserUnit('assignment-id', true, 1);
    await disableOrganizationUserUnit('assignment-id');

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/organization/user-units',
      expect.objectContaining({ method: 'POST' })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/organization/user-units/assignment-id',
      expect.objectContaining({ method: 'PUT' })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      '/api/v1/organization/user-units/assignment-id/disable',
      expect.objectContaining({ method: 'POST' })
    );
  });
});
