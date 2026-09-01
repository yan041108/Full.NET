import {
  identityCreateHostMenu,
  identityDisableHostMenu,
  identityEnableHostMenu,
  identityListAllHostMenus,
  identityListHostMenuPermissionOptions,
  identityListHostMenus,
  identitySyncHostMenuCatalog,
  identityUpdateHostMenu,
  isHostMenu,
  isHostMenuArray,
  isHostMenuPermissionOptionArray,
  isHostMenuPage,
  type CreateHostMenuRequest,
  type HostMenu,
  type HostMenuPermissionOption,
  type HostMenuPage,
  type UpdateHostMenuRequest
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询 Host 菜单列表，并对生成契约放宽的 menuType 做失败关闭校验。 */
export async function listHostMenus(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostMenuPage> {
  const value = await identityListHostMenus(http, { page, pageSize }, signal);
  // 生成模型将 menuType 放宽为 string；页面仍要求稳定机器码联合类型。
  if (!isHostMenuPage(value)) {
    throw new Error('client.invalid_host_menu_page');
  }

  return value;
}

/** 查询全部 Host 菜单，供树构建和父级选择器复用。 */
export async function listHostMenusAll(
  signal?: AbortSignal
): Promise<HostMenu[]> {
  const value = await identityListAllHostMenus(http, {}, signal);
  if (!isHostMenuArray(value)) {
    throw new Error('client.invalid_host_menu_array');
  }

  return value;
}

/** 创建 Host 菜单。 */
export async function createHostMenu(
  body: CreateHostMenuRequest,
  signal?: AbortSignal
): Promise<HostMenu> {
  const value = await identityCreateHostMenu(http, { body }, signal);
  if (!isHostMenu(value)) {
    throw new Error('client.invalid_host_menu');
  }

  return value;
}

/** 查询菜单可绑定的页面/操作权限目录。 */
export async function listHostMenuPermissionOptions(
  signal?: AbortSignal
): Promise<HostMenuPermissionOption[]> {
  const value = await identityListHostMenuPermissionOptions(http, {}, signal);
  // 生成模型将 kind 放宽为 string；页面仍要求 page|action。
  if (!isHostMenuPermissionOptionArray(value)) {
    throw new Error('client.invalid_host_menu_permission_options');
  }

  return value;
}

/** 更新 Host 菜单。 */
export async function updateHostMenu(
  id: string,
  body: UpdateHostMenuRequest,
  signal?: AbortSignal
): Promise<HostMenu> {
  const value = await identityUpdateHostMenu(
    http,
    { menuId: id, body },
    signal
  );
  if (!isHostMenu(value)) {
    throw new Error('client.invalid_host_menu');
  }

  return value;
}

/** 禁用 Host 菜单。 */
export async function disableHostMenu(
  id: string,
  signal?: AbortSignal
): Promise<HostMenu> {
  const value = await identityDisableHostMenu(http, { menuId: id }, signal);
  if (!isHostMenu(value)) {
    throw new Error('client.invalid_host_menu');
  }

  return value;
}

/** 启用 Host 菜单。 */
export async function enableHostMenu(
  id: string,
  signal?: AbortSignal
): Promise<HostMenu> {
  const value = await identityEnableHostMenu(http, { menuId: id }, signal);
  if (!isHostMenu(value)) {
    throw new Error('client.invalid_host_menu');
  }

  return value;
}

/** 按授权目录同步 Host 菜单目录，并返回创建/跳过/重挂接统计。 */
export async function syncHostMenuCatalog(
  signal?: AbortSignal
): Promise<{
  created: number;
  skipped: number;
  reparented: number;
}> {
  return identitySyncHostMenuCatalog(http, {}, signal);
}

/** 导出菜单详情、分页、权限选项与写入模型，供菜单列表、编辑页与权限绑定选择器共享同一契约。 */
export type {
  CreateHostMenuRequest,
  HostMenu,
  HostMenuPermissionOption,
  HostMenuPage,
  UpdateHostMenuRequest
};
