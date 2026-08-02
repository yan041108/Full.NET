import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createHostRole,
  disableHostRole,
  getAuthorizationTree,
  getHostRoleDataScope,
  listHostRoles,
  replaceHostRolePermissions,
  updateHostRole,
  updateHostRoleDataScope
} from './roles';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sampleRole = {
  id: 'role-id',
  code: 'support',
  name: '支持角色',
  isSystem: false,
  isActive: true,
  isSuperAdministrator: false,
  permissionCodes: ['identity.users.read'],
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('Vue Host 角色 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验授权树响应', async () => {
    requestMock.mockResolvedValueOnce([
      {
        id: 'users',
        title: '用户管理',
        permissionCode: 'identity.users.read',
        order: 10,
        actions: [
          {
            id: 'identity.users.reset-password',
            name: '重置密码',
            permissionCode: 'identity.users.reset_password',
            order: 50
          }
        ],
        children: []
      }
    ]);

    await expect(getAuthorizationTree()).resolves.toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith('/api/v1/identity/authorization-tree');
  });

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleRole],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listHostRoles()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith('/api/v1/identity/roles?page=1&pageSize=20');
  });

  it('通过 JSON 正文创建、更新权限并禁用角色', async () => {
    requestMock
      .mockResolvedValueOnce(sampleRole)
      .mockResolvedValueOnce({ ...sampleRole, permissionCodes: [], version: 2 })
      .mockResolvedValueOnce({ ...sampleRole, isActive: false, version: 3 });

    await createHostRole('support', '支持角色');
    await replaceHostRolePermissions('role-id', ['identity.users.read'], 1);
    await disableHostRole('role-id');

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/identity/roles',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ code: 'support', name: '支持角色' })
      })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/roles/role-id/permissions',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          permissionCodes: ['identity.users.read'],
          version: 1
        })
      })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      '/api/v1/identity/roles/role-id/disable',
      expect.objectContaining({ method: 'POST' })
    );
  });

  it('通过 JSON 正文更新显示名称与乐观版本', async () => {
    requestMock.mockResolvedValue({
      ...sampleRole,
      name: '新名称',
      version: 2
    });

    await updateHostRole('role-id', '新名称', 1);

    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/identity/roles/role-id',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ name: '新名称', version: 1 })
      })
    );
  });

  it('读取并更新角色数据范围', async () => {
    requestMock
      .mockResolvedValueOnce({
        roleId: 'role-id',
        dataScopeKind: 'identity.data_scope.all',
        unitIds: [],
        version: 1
      })
      .mockResolvedValueOnce({
        roleId: 'role-id',
        dataScopeKind: 'identity.data_scope.self',
        unitIds: [],
        version: 2
      });

    await expect(getHostRoleDataScope('role-id')).resolves.toMatchObject({
      dataScopeKind: 'identity.data_scope.all'
    });
    await updateHostRoleDataScope('role-id', 'identity.data_scope.self', null, 1);

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/identity/roles/role-id/data-scope'
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/roles/role-id/data-scope',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          dataScopeKind: 'identity.data_scope.self',
          unitIds: null,
          version: 1,
          tenantId: null
        })
      })
    );
  });
});
