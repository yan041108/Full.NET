import {
  identityCreateHostRole,
  identityDisableHostRole,
  identityGetAuthorizationTree,
  identityGetHostRoleDataScope,
  identityGetHostRoleFieldGrants,
  identityListFieldProjectionCatalog,
  identityListHostRoles,
  identityReplaceHostRoleFieldGrants,
  identityReplaceHostRolePermissions,
  identityUpdateHostRole,
  identityUpdateHostRoleDataScope,
  isFieldProjectionCatalog,
  isHostRoleDataScope,
  type AuthorizationTreeModule,
  type FieldProjectionResourceDefinition,
  type HostRole,
  type HostRoleDataScope,
  type HostRoleFieldGrants,
  type HostRolePage,
  type RoleDataScopeKind
} from '@fullnet/client-contracts';
import { http } from './http';

export async function getAuthorizationTree(
  signal?: AbortSignal
): Promise<AuthorizationTreeModule[]> {
  return identityGetAuthorizationTree(http, {}, signal);
}

export async function listHostRoles(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostRolePage> {
  return identityListHostRoles(http, { page, pageSize }, signal);
}

export async function createHostRole(
  code: string,
  name: string,
  signal?: AbortSignal
): Promise<HostRole> {
  return identityCreateHostRole(http, { body: { code, name } }, signal);
}

export async function updateHostRole(
  id: string,
  name: string,
  version: number,
  signal?: AbortSignal
): Promise<HostRole> {
  return identityUpdateHostRole(
    http,
    { roleId: id, body: { name, version } },
    signal
  );
}

export async function replaceHostRolePermissions(
  id: string,
  permissionCodes: string[],
  version: number,
  signal?: AbortSignal
): Promise<HostRole> {
  return identityReplaceHostRolePermissions(
    http,
    { roleId: id, body: { permissionCodes, version } },
    signal
  );
}

export async function disableHostRole(
  id: string,
  signal?: AbortSignal
): Promise<HostRole> {
  return identityDisableHostRole(http, { roleId: id }, signal);
}

export async function getHostRoleDataScope(
  id: string,
  signal?: AbortSignal
): Promise<HostRoleDataScope> {
  const value = await identityGetHostRoleDataScope(http, { roleId: id }, signal);
  // 生成模型将 dataScopeKind 放宽为 string；页面仍要求稳定机器码联合类型。
  if (!isHostRoleDataScope(value)) {
    throw new Error('client.invalid_host_role_data_scope');
  }

  return value;
}

export async function updateHostRoleDataScope(
  id: string,
  dataScopeKind: RoleDataScopeKind,
  unitIds: string[] | null,
  version: number,
  tenantId: string | null = null,
  signal?: AbortSignal
): Promise<HostRoleDataScope> {
  const value = await identityUpdateHostRoleDataScope(
    http,
    {
      roleId: id,
      body: { dataScopeKind, unitIds, version, tenantId }
    },
    signal
  );
  if (!isHostRoleDataScope(value)) {
    throw new Error('client.invalid_host_role_data_scope');
  }

  return value;
}

export async function getFieldProjectionCatalog(
  signal?: AbortSignal
): Promise<FieldProjectionResourceDefinition[]> {
  const value = await identityListFieldProjectionCatalog(http, {}, signal);
  // OpenAPI 枚举当前导出为 number；手写契约同时接受字符串与数值，页面继续使用既有类型。
  if (!isFieldProjectionCatalog(value)) {
    throw new Error('client.invalid_field_projection_catalog');
  }

  return value;
}

export async function getHostRoleFieldGrants(
  id: string,
  resourceKey: string,
  signal?: AbortSignal
): Promise<HostRoleFieldGrants> {
  return identityGetHostRoleFieldGrants(
    http,
    { roleId: id, resourceKey },
    signal
  );
}

export async function replaceHostRoleFieldGrants(
  id: string,
  resourceKey: string,
  fieldKeys: string[],
  version: number,
  signal?: AbortSignal
): Promise<HostRoleFieldGrants> {
  return identityReplaceHostRoleFieldGrants(
    http,
    { roleId: id, body: { resourceKey, fieldKeys, version } },
    signal
  );
}
