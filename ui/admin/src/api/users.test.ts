import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createHostUser,
  disableHostUser,
  downloadHostUserImportTemplate,
  enableHostUser,
  exportHostUsersWorkbook,
  getHostUserRoles,
  importHostUsersWorkbook,
  listHostUsers,
  replaceHostUserRoles,
  resetHostUserPassword,
  updateHostUser
} from './users';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);
const requestBlobMock = vi.mocked(http.requestBlob);

const userId = '01912345-6789-7abc-8def-0123456789ab';
const roleAId = '01912345-6789-7abc-8def-0123456789ac';
const roleBId = '01912345-6789-7abc-8def-0123456789ad';

const sampleUser = {
  id: userId,
  username: 'operator',
  displayName: '运维账号',
  accountType: 'normal_user',
  isActive: true,
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('Vue Host 用户 API', () => {
  beforeEach(() => {
    requestMock.mockReset();
    requestBlobMock.mockReset();
  });

  it('下载导入模板与 Excel 导出使用认证 Blob 客户端', async () => {
    const template = new Blob(['template']);
    const exported = new Blob(['export']);
    requestBlobMock
      .mockResolvedValueOnce(template)
      .mockResolvedValueOnce(exported);

    await expect(downloadHostUserImportTemplate()).resolves.toBe(template);
    await expect(exportHostUsersWorkbook()).resolves.toBe(exported);

    expect(requestBlobMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/identity/users/import-template',
      { method: 'GET', headers: { accept: 'application/octet-stream' } },
      undefined
    );
    expect(requestBlobMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/users/export-file',
      { method: 'GET', headers: { accept: 'application/octet-stream' } },
      undefined
    );
  });

  it('通过 multipart 上传用户工作簿并返回逐行结果', async () => {
    requestMock.mockResolvedValueOnce({
      succeededCount: 1,
      results: [{ line: 1, succeeded: true, userId, errorCode: null, message: null }]
    });
    const file = new File(['xlsx'], 'host-users.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
    });

    const result = await importHostUsersWorkbook(file);

    expect(result.succeededCount).toBe(1);
    const [path, init] = requestMock.mock.calls[0] ?? [];
    expect(path).toBe('/api/v1/identity/users/import-file');
    expect(init?.method).toBe('POST');
    expect(init?.body).toBeInstanceOf(FormData);
    expect((init?.body as FormData).get('file')).toBe(file);
  });

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleUser],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listHostUsers()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/identity/users?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });

  it('通过 JSON 正文创建、禁用并启用用户', async () => {
    requestMock
      .mockResolvedValueOnce(sampleUser)
      .mockResolvedValueOnce({ ...sampleUser, isActive: false })
      .mockResolvedValueOnce({ ...sampleUser, isActive: true, version: 2 });

    await createHostUser('operator', '运维账号', 'FullNet!2026Secure');
    await disableHostUser(userId);
    await enableHostUser(userId);

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
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/identity/users/${userId}/disable`,
      expect.objectContaining({ method: 'POST' }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/identity/users/${userId}/enable`,
      expect.objectContaining({ method: 'POST' }),
      undefined
    );
  });

  it('通过 JSON 正文更新显示名称与乐观版本', async () => {
    requestMock.mockResolvedValue({
      ...sampleUser,
      displayName: '新名称',
      version: 2
    });

    await updateHostUser(userId, '新名称', 1);

    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/identity/users/${userId}`,
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ displayName: '新名称', version: 1 })
      }),
      undefined
    );
  });

  it('通过 JSON 正文重置用户密码', async () => {
    requestMock.mockResolvedValueOnce({ ...sampleUser, version: 2 });

    await resetHostUserPassword(userId, 'FullNet!2026Rotate');

    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/identity/users/${userId}/reset-password`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ password: 'FullNet!2026Rotate' })
      }),
      undefined
    );
  });

  it('读取并替换用户可分配角色', async () => {
    requestMock
      .mockResolvedValueOnce({
        userId,
        roleIds: [roleAId],
        version: 1
      })
      .mockResolvedValueOnce({
        userId,
        roleIds: [roleAId, roleBId],
        version: 2
      });

    await expect(getHostUserRoles(userId)).resolves.toMatchObject({
      roleIds: [roleAId]
    });
    await replaceHostUserRoles(userId, [roleAId, roleBId], 1);

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      `/api/v1/identity/users/${userId}/roles`,
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/identity/users/${userId}/roles`,
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          roleIds: [roleAId, roleBId],
          version: 1
        })
      }),
      undefined
    );
  });

  it('拒绝畸形成功响应并透传取消信号', async () => {
    requestMock.mockResolvedValueOnce({
      items: [{ ...sampleUser, isActive: 'yes' }],
      page: 1,
      pageSize: 20,
      total: 1
    });
    const controller = new AbortController();

    await expect(listHostUsers(1, 20, controller.signal))
      .rejects.toThrow('client.invalid_paged_result_of_host_user_response');
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/identity/users?page=1&pageSize=20',
      { method: 'GET' },
      controller.signal
    );
  });
});
