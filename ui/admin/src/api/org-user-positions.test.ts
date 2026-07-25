import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createOrganizationUserPosition,
  disableOrganizationUserPosition,
  listOrganizationUserPositions,
  updateOrganizationUserPosition
} from './org-user-positions';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sampleAssignment = {
  id: 'assignment-id',
  userId: 'user-id',
  username: 'admin',
  displayName: '系统管理员',
  positionId: 'position-id',
  positionCode: 'engineer',
  positionName: '工程师',
  isPrimary: false,
  isActive: true,
  createdAtUtc: '2026-07-25T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('Vue 租户用户-职位隶属 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleAssignment],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listOrganizationUserPositions()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/organization/user-positions?page=1&pageSize=20'
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

    await createOrganizationUserPosition('user-id', 'position-id');
    await updateOrganizationUserPosition('assignment-id', true, 1);
    await disableOrganizationUserPosition('assignment-id');

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/organization/user-positions',
      expect.objectContaining({ method: 'POST' })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/organization/user-positions/assignment-id',
      expect.objectContaining({ method: 'PUT' })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      '/api/v1/organization/user-positions/assignment-id/disable',
      expect.objectContaining({ method: 'POST' })
    );
  });
});
