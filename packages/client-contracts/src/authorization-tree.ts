const moduleIdPattern = /^[a-z][a-z0-9-]{0,63}$/;
const pageIdPattern = /^[a-z][a-z0-9-]{0,63}$/;
const actionIdPattern = /^[a-z][a-z0-9.-]{0,127}$/;
const permissionPattern = /^[a-z][a-z0-9_]*(?:[.-][a-z][a-z0-9_]*)+$/;

const moduleKeys = new Set([
  'id',
  'order',
  'pages',
  'title'
]);

const pageKeys = new Set([
  'actions',
  'children',
  'id',
  'order',
  'permissionCode',
  'title'
]);

const actionKeys = new Set([
  'id',
  'name',
  'order',
  'permissionCode'
]);

const forbiddenKeys = new Set([
  'componentKey',
  'path',
  'routeName',
  'href',
  'url',
  'script',
  'html'
]);

/** 角色授权页使用的页面操作节点。 */
export interface AuthorizationTreeAction {
  id: string;
  name: string;
  permissionCode: string;
  order: number;
}

/** 角色授权页使用的页面目录节点。 */
export interface AuthorizationTreePage {
  id: string;
  title: string;
  permissionCode: string;
  order: number;
  actions: AuthorizationTreeAction[];
  children: AuthorizationTreePage[];
}

/** 角色授权页使用的模块目录节点。 */
export interface AuthorizationTreeModule {
  id: string;
  title: string;
  order: number;
  pages: AuthorizationTreePage[];
}

/**
 * 校验不可信 JSON 是否为 Host 授权树模块数组。
 * 拒绝可执行元数据、缺字段、重复标识与异常嵌套。
 */
export function isAuthorizationTreeModuleArray(
  value: unknown
): value is AuthorizationTreeModule[] {
  if (!Array.isArray(value)) {
    return false;
  }

  const moduleIds = new Set<string>();
  const pageIds = new Set<string>();
  const actionIds = new Set<string>();
  const actionPermissionCodes = new Set<string>();
  return value.every(module => isAuthorizationTreeModule(
    module,
    moduleIds,
    pageIds,
    actionIds,
    actionPermissionCodes
  ));
}

/** 将模块树扁平化为页面数组，供仅关心页面目录的调用方复用。 */
export function flattenAuthorizationTreePages(
  modules: readonly AuthorizationTreeModule[]
): AuthorizationTreePage[] {
  return modules.flatMap(module => module.pages);
}

/**
 * @deprecated 使用 isAuthorizationTreeModuleArray。
 */
export function isAuthorizationTreePageArray(
  value: unknown
): value is AuthorizationTreePage[] {
  if (!Array.isArray(value)) {
    return false;
  }

  const pageIds = new Set<string>();
  const actionIds = new Set<string>();
  const actionPermissionCodes = new Set<string>();
  return value.every(page => isAuthorizationTreePage(
    page,
    pageIds,
    actionIds,
    actionPermissionCodes
  ));
}

function isAuthorizationTreeModule(
  value: unknown,
  moduleIds: Set<string>,
  pageIds: Set<string>,
  actionIds: Set<string>,
  actionPermissionCodes: Set<string>
): value is AuthorizationTreeModule {
  if (!isRecord(value)
    || !hasOnlyKeys(value, moduleKeys)
    || hasForbiddenKeys(value)
    || typeof value.id !== 'string'
    || value.id.length === 0
    || !moduleIdPattern.test(value.id)
    || moduleIds.has(value.id)
    || !isDisplayText(value.title)
    || typeof value.order !== 'number'
    || !Number.isInteger(value.order)
    || !Array.isArray(value.pages)) {
    return false;
  }

  moduleIds.add(value.id);
  return value.pages.every(page => isAuthorizationTreePage(
    page,
    pageIds,
    actionIds,
    actionPermissionCodes
  ));
}

function isAuthorizationTreePage(
  value: unknown,
  pageIds: Set<string>,
  actionIds: Set<string>,
  actionPermissionCodes: Set<string>
): value is AuthorizationTreePage {
  if (!isRecord(value)
    || !hasOnlyKeys(value, pageKeys)
    || hasForbiddenKeys(value)
    || typeof value.id !== 'string'
    || value.id.length === 0
    || !pageIdPattern.test(value.id)
    || pageIds.has(value.id)
    || !isDisplayText(value.title)
    || typeof value.permissionCode !== 'string'
    || !permissionPattern.test(value.permissionCode)
    || typeof value.order !== 'number'
    || !Number.isInteger(value.order)
    || !Array.isArray(value.actions)
    || !Array.isArray(value.children)) {
    return false;
  }

  const pagePermissionCode = value.permissionCode;
  pageIds.add(value.id);
  const pageActionIds = new Set<string>();
  if (!value.actions.every(action => isAuthorizationTreeAction(
    action,
    actionIds,
    pageActionIds,
    actionPermissionCodes,
    pagePermissionCode
  ))) {
    return false;
  }

  return value.children.every(child => isAuthorizationTreePage(
    child,
    pageIds,
    actionIds,
    actionPermissionCodes
  ));
}

function isAuthorizationTreeAction(
  value: unknown,
  globalActionIds: Set<string>,
  pageActionIds: Set<string>,
  globalActionPermissionCodes: Set<string>,
  pagePermissionCode: string
): value is AuthorizationTreeAction {
  if (!isRecord(value)
    || !hasOnlyKeys(value, actionKeys)
    || hasForbiddenKeys(value)
    || typeof value.id !== 'string'
    || value.id.length === 0
    || !actionIdPattern.test(value.id)
    || globalActionIds.has(value.id)
    || pageActionIds.has(value.id)
    || !isDisplayText(value.name)
    || typeof value.permissionCode !== 'string'
    || !permissionPattern.test(value.permissionCode)
    || value.permissionCode === pagePermissionCode
    || globalActionPermissionCodes.has(value.permissionCode)
    || typeof value.order !== 'number'
    || !Number.isInteger(value.order)) {
    return false;
  }

  globalActionIds.add(value.id);
  pageActionIds.add(value.id);
  globalActionPermissionCodes.add(value.permissionCode);
  return true;
}

function isDisplayText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function hasForbiddenKeys(value: Record<string, unknown>): boolean {
  return Object.keys(value).some(key => forbiddenKeys.has(key));
}

function hasOnlyKeys(
  value: Record<string, unknown>,
  allowed: Set<string>
): boolean {
  const keys = Object.keys(value);
  return keys.length === allowed.size
    && keys.every(key => allowed.has(key));
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
