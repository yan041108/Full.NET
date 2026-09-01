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

/** 读取角色授权页所需的完整授权树目录。 */
export async function getAuthorizationTree(
  signal?: AbortSignal
): Promise<AuthorizationTreeModule[]> {
  return identityGetAuthorizationTree(http, {}, signal);
}

/** 分页查询 Host 角色列表。 */
export async function listHostRoles(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostRolePage> {
  return identityListHostRoles(http, { page, pageSize }, signal);
}

/** 创建 Host 角色。 */
export async function createHostRole(
  code: string,
  name: string,
  signal?: AbortSignal
): Promise<HostRole> {
  return identityCreateHostRole(http, { body: { code, name } }, signal);
}

/** 更新 Host 角色名称，并携带版本号维持乐观并发。 */
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

/** 整体替换 Host 角色的权限码集合。 */
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

/** 禁用 Host 角色。 */
export async function disableHostRole(
  id: string,
  signal?: AbortSignal
): Promise<HostRole> {
  return identityDisableHostRole(http, { roleId: id }, signal);
}

/** 读取 Host 角色数据权限范围，并对返回机器码做失败关闭校验。 */
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

/** 更新 Host 角色数据权限范围与机构范围。 */
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

/** 读取字段投影目录，并对生成契约的枚举放宽做手写校验。 */
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

/** 读取角色在指定资源上的字段授权。 */
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

/** 整体替换角色在指定资源上的字段授权集合。 */
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

/** 导出授权树、角色、数据范围与字段投影模型，供角色列表、授权页、数据范围页与字段授权页共享同一契约。 */
export type {
  AuthorizationTreeModule,
  FieldProjectionResourceDefinition,
  HostRole,
  HostRoleDataScope,
  HostRoleFieldGrants,
  HostRolePage,
  RoleDataScopeKind
};
