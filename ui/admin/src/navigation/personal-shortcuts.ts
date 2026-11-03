export interface PersonalShortcutScope { userId: string; tenantId?: string | null }
const routeName = (value: unknown): value is string => typeof value === 'string' && /^[a-zA-Z0-9_.-]{1,128}$/.test(value);
const maximum = 12;

/** 只使用可信会话身份构造偏好键；Host 与每个租户互不复用。 */
export function personalShortcutKey(scope: PersonalShortcutScope): string {
  return `fullnet.admin.shortcuts.v1:${encodeURIComponent(scope.userId)}:${encodeURIComponent(scope.tenantId ?? 'host')}`;
}

/** 损坏或过大偏好不进入导航；历史路由仍须与当前授权白名单交集。 */
export function readPersonalShortcuts(storage: Pick<Storage, 'getItem'>, scope: PersonalShortcutScope): string[] {
  const raw = storage.getItem(personalShortcutKey(scope)); if (!raw || raw.length > 2048) return [];
  try {
    const value: unknown = JSON.parse(raw);
    if (!value || typeof value !== 'object' || Array.isArray(value)) return [];
    const document = value as Record<string, unknown>;
    return document.schemaVersion === 1 && Array.isArray(document.routes) && document.routes.length <= maximum
      && document.routes.every(routeName) && new Set(document.routes).size === document.routes.length ? [...document.routes] : [];
  } catch { return []; }
}

/** 只保存当前可见的稳定路由名，不保存路径、对象标识、参数或任意 URL。 */
export function savePersonalShortcuts(storage: Pick<Storage, 'setItem'>, scope: PersonalShortcutScope, routes: string[], allowed: ReadonlySet<string>): void {
  if (routes.length > maximum || new Set(routes).size !== routes.length || routes.some(name => !routeName(name) || !allowed.has(name)))
    throw new TypeError('client.invalid_personal_shortcuts');
  storage.setItem(personalShortcutKey(scope), JSON.stringify({ schemaVersion: 1, routes }));
}
