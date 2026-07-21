import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { createHostUser, disableHostUser, listHostUsers, updateHostUser } from './users';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sampleUser = {
  id: 'user-id',
  username: 'operator',
  displayName: '运维账号',
  isActive: true,
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('Vue Host 用户 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleUser],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listHostUsers()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith('/api/v1/identity/users?page=1&pageSize=20');
  });

  it('通过 JSON 正文创建并禁用用户', async () => {
    requestMock
      .mockResolvedValueOnce(sampleUser)
      .mockResolvedValueOnce({ ...sampleUser, isActive: false });

    await createHostUser('operator', '运维账号', 'FullNet!2026Secure');
    await disableHostUser('user-id');

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/identity/users',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          username: 'operator',
          displayName: '运维账号',
          password: 'FullNet!2026Secure'
        })
      })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/users/user-id/disable',
      expect.objectContaining({ method: 'POST' })
    );
  });

  it('通过 JSON 正文更新显示名称与乐观版本', async () => {
    requestMock.mockResolvedValue({
      ...sampleUser,
      displayName: '新名称',
      version: 2
    });

    await updateHostUser('user-id', '新名称', 1);

    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/identity/users/user-id',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ displayName: '新名称', version: 1 })
      })
    );
  });
});
