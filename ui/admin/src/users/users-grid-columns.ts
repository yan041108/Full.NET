import type { GridColumnFixed, GridColumnPreference, GridPreferenceResponse } from '@fullnet/client-contracts';
import { gridPreferenceDefinitionFor } from '@fullnet/client-contracts';

export const USERS_GRID_KEY = 'identity.users';

export type UsersGridColumnKey =
  | 'gender'
  | 'roles'
  | 'org'
  | 'position'
  | 'employeeNumber'
  | 'accountType'
  | 'sortOrder'
  | 'phone'
  | 'createdAt';

export interface UsersGridColumnState {
  key: UsersGridColumnKey;
  label: string;
  visible: boolean;
  width: number | null;
  minWidth: number;
  fixed: GridColumnFixed;
  order: number;
  disabled?: boolean;
}

const defaultDefinitions: ReadonlyArray<{
  key: UsersGridColumnKey;
  minWidth: number;
  defaultWidth: number | null;
}> = [
  { key: 'gender', minWidth: 88, defaultWidth: 88 },
  { key: 'roles', minWidth: 160, defaultWidth: null },
  { key: 'org', minWidth: 140, defaultWidth: null },
  { key: 'position', minWidth: 120, defaultWidth: null },
  { key: 'employeeNumber', minWidth: 120, defaultWidth: 120 },
  { key: 'accountType', minWidth: 110, defaultWidth: 110 },
  { key: 'sortOrder', minWidth: 88, defaultWidth: 88 },
  { key: 'phone', minWidth: 140, defaultWidth: 140 },
  { key: 'createdAt', minWidth: 180, defaultWidth: 180 }
];

/** 构建用户列表默认可配置列；调用方负责注入标签与授权过滤。 */
export function createDefaultUsersGridColumns(
  labelFor: (key: UsersGridColumnKey) => string,
  isAuthorized: (key: UsersGridColumnKey) => boolean
): UsersGridColumnState[] {
  return defaultDefinitions
    .map((definition, index) => ({
      key: definition.key,
      label: labelFor(definition.key),
      visible: true,
      width: definition.defaultWidth,
      minWidth: definition.minWidth,
      fixed: null,
      order: index,
      disabled: !isAuthorized(definition.key)
    }))
    .filter(column => isAuthorized(column.key));
}

/** 将远端偏好合并到本地列状态；忽略未授权列与未知列。 */
export function applyUsersGridPreference(
  columns: readonly UsersGridColumnState[],
  preference: GridPreferenceResponse | undefined
): UsersGridColumnState[] {
  const definition = gridPreferenceDefinitionFor(USERS_GRID_KEY);
  const fallback = columns.map(column => ({ ...column }));
  if (!definition
    || !preference
    || preference.gridKey !== definition.gridKey
    || preference.schemaVersion !== definition.schemaVersion) {
    return fallback;
  }

  const byKey = new Map(preference.columns.map(column => [column.columnKey, column]));
  const originalOrder = new Map(columns.map((column, index) => [column.key, index]));
  return fallback
    .map(column => {
      const selected = byKey.get(column.key);
      return selected
        ? {
            ...column,
            visible: selected.visible,
            width: selected.width ?? column.width,
            fixed: selected.fixed,
            order: selected.order
          }
        : column;
    })
    .sort((left, right) => {
      const leftOrder = byKey.get(left.key)?.order ?? Number.MAX_SAFE_INTEGER;
      const rightOrder = byKey.get(right.key)?.order ?? Number.MAX_SAFE_INTEGER;
      return leftOrder - rightOrder
        || originalOrder.get(left.key)! - originalOrder.get(right.key)!;
    });
}

/** 将本地列状态转换为可持久化的偏好载荷；只包含授权列。 */
export function toUsersGridPreferenceColumns(
  columns: readonly UsersGridColumnState[]
): GridColumnPreference[] {
  return columns
    .filter(column => !column.disabled)
    .map((column, index) => ({
      columnKey: column.key,
      order: index,
      width: column.width,
      visible: column.visible,
      fixed: column.fixed
    }));
}

/** 重置为默认列状态并保留当前授权边界。 */
export function resetUsersGridColumns(
  labelFor: (key: UsersGridColumnKey) => string,
  isAuthorized: (key: UsersGridColumnKey) => boolean
): UsersGridColumnState[] {
  return createDefaultUsersGridColumns(labelFor, isAuthorized);
}
