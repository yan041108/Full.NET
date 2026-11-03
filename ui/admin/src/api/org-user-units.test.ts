import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createOrganizationUserUnit,
  disableOrganizationUserUnit,
  listAssignableOrganizationUserUnitUsers,
  listOrganizationUserUnits,
  updateOrganizationUserUnit
} from './org-user-units';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const assignmentId = '019bc2b1-2a40-7cc3-8992-a80de51bf300';
const userId = '019bc2b1-2a40-7cc3-8992-a80de51bf301';
const unitId = '019bc2b1-2a40-7cc3-8992-a80de51bf302';

const sampleAssignment = {
  id: assignmentId,
  userId,
  username: 'admin',
  displayName: '系统管理员',
  unitId,
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

    await expect(listOrganizationUserUnits()).resolves.toMatchObject({ total: 1 });
    await expect(listAssignableOrganizationUserUnitUsers())
      .resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/organization/user-units?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/organization/user-units/assignable-users?page=1&pageSize=100',
      { method: 'GET' },
      undefined
    );
  });

  it('发送服务端筛选、页码与取消信号，保留 false 状态值', async () => {
    requestMock.mockResolvedValueOnce({ items: [], page: 2, pageSize: 10, total: 25 });
    const signal = new AbortController().signal;
    await listOrganizationUserUnits({ page: 2, pageSize: 10, userSearch: 'a%_用户', unitSearch: '目标', isActive: false }, signal);
    const [path, init, actualSignal] = requestMock.mock.calls[0]!;
    const url = new URL(path, 'http://localhost');
    expect(Object.fromEntries(url.searchParams)).toEqual({ page: '2', pageSize: '10', userSearch: 'a%_用户', unitSearch: '目标', isActive: 'false' });
    expect(init).toEqual({ method: 'GET' });
    expect(actualSignal).toBe(signal);
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

    await createOrganizationUserUnit(userId, unitId);
    await updateOrganizationUserUnit(assignmentId, true, 1);
    await disableOrganizationUserUnit(assignmentId);

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/organization/user-units',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ userId, unitId, isPrimary: false })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/organization/user-units/${assignmentId}`,
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ isPrimary: true, version: 1 })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/organization/user-units/${assignmentId}/disable`,
      { method: 'POST' },
      undefined
    );
  });
});
