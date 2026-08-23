import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createOrganizationUserPosition,
  disableOrganizationUserPosition,
  listAssignableOrganizationUserPositionUsers,
  listOrganizationUserPositions,
  updateOrganizationUserPosition
} from './org-user-positions';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const assignmentId = '019bc2b1-2a40-7cc3-8992-a80de51bf320';
const userId = '019bc2b1-2a40-7cc3-8992-a80de51bf321';
const positionId = '019bc2b1-2a40-7cc3-8992-a80de51bf322';

const sampleAssignment = {
  id: assignmentId,
  userId,
  username: 'admin',
  displayName: '系统管理员',
  positionId,
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
    }).mockResolvedValueOnce({
      items: [{
        id: userId,
        username: 'admin',
        displayName: '系统管理员'
      }],
      page: 1,
      pageSize: 100,
      total: 1
    });

    await expect(listOrganizationUserPositions()).resolves.toMatchObject({ total: 1 });
    await expect(listAssignableOrganizationUserPositionUsers())
      .resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/organization/user-positions?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/organization/user-positions/assignable-users?page=1&pageSize=100',
      { method: 'GET' },
      undefined
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

    await createOrganizationUserPosition(userId, positionId);
    await updateOrganizationUserPosition(assignmentId, true, 1);
    await disableOrganizationUserPosition(assignmentId);

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/organization/user-positions',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ userId, positionId, isPrimary: false })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/organization/user-positions/${assignmentId}`,
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ isPrimary: true, version: 1 })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/organization/user-positions/${assignmentId}/disable`,
      { method: 'POST' },
      undefined
    );
  });
});
