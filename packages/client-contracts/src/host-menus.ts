import { ADMIN_NAVIGATION_CATALOG } from './navigation-catalog.js';

/** 创建 Host 菜单时可选择的组件键（与服务端白名单一致）。 */
export const HOST_MENU_COMPONENT_OPTIONS = ADMIN_NAVIGATION_CATALOG;

/** Host 菜单可引用的权限码（与后端 Catalog 校验一致）。 */
export const HOST_MENU_ASSIGNABLE_PERMISSIONS = [
  'platform.dashboard.read',
  'identity.navigation.read',
  'identity.users.read',
  'identity.users.write',
  'identity.roles.read',
  'identity.roles.write',
  'identity.menus.read',
  'identity.menus.write',
  'organization.units.read',
  'organization.units.write',
  'organization.user_units.read',
  'organization.user_units.write',
  'tenancy.tenants.read',
  'tenancy.tenants.switch'
] as const;

export type HostMenuAssignablePermission =
  typeof HOST_MENU_ASSIGNABLE_PERMISSIONS[number];

export interface HostMenu {
  id: string;
  parentId: string | null;
  routeName: string;
  path: string;
  componentKey: string;
  title: string;
  caption: string;
  icon: string;
  displayOrder: number;
  requiredPermission: string;
  isSystem: boolean;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface HostMenuPage {
  items: HostMenu[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateHostMenuRequest {
  parentId: string | null;
  routeName: string;
  path: string;
  componentKey: string;
  title: string;
  caption: string;
  icon: string;
  displayOrder: number;
  requiredPermission: string;
}

export interface UpdateHostMenuRequest {
  parentId: string | null;
  path: string;
  componentKey: string;
  title: string;
  caption: string;
  icon: string;
  displayOrder: number;
  requiredPermission: string;
  version: number;
}

/** 校验不可信 JSON 是否为 Host 菜单分页结果。 */
export function isHostMenuPage(value: unknown): value is HostMenuPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostMenu)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

/** 校验不可信 JSON 是否为单个 Host 菜单。 */
export function isHostMenu(value: unknown): value is HostMenu {
  return isRecord(value)
    && isText(value.id)
    && (value.parentId === null || isText(value.parentId))
    && typeof value.routeName === 'string'
    && typeof value.path === 'string'
    && typeof value.componentKey === 'string'
    && typeof value.title === 'string'
    && typeof value.caption === 'string'
    && typeof value.icon === 'string'
    && typeof value.displayOrder === 'number'
    && typeof value.requiredPermission === 'string'
    && typeof value.isSystem === 'boolean'
    && typeof value.isActive === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

/** 校验不可信 JSON 是否为 Host 菜单更新请求。 */
export function isUpdateHostMenuRequest(
  value: unknown
): value is UpdateHostMenuRequest {
  return isRecord(value)
    && (value.parentId === null || typeof value.parentId === 'string')
    && typeof value.path === 'string'
    && typeof value.componentKey === 'string'
    && typeof value.title === 'string'
    && value.title.length > 0
    && typeof value.caption === 'string'
    && typeof value.icon === 'string'
    && typeof value.displayOrder === 'number'
    && typeof value.requiredPermission === 'string'
    && typeof value.version === 'number';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}
