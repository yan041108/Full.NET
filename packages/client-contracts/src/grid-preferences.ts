export type GridColumnFixed = 'left' | 'right' | null;

export interface GridColumnPreference {
  columnKey: string;
  order: number;
  width: number | null;
  visible: boolean;
  fixed: GridColumnFixed;
}

export interface UpdateGridPreferenceRequest {
  schemaVersion: number;
  columns: GridColumnPreference[];
  version: number;
}

export interface GridPreferenceResponse {
  gridKey: string;
  schemaVersion: number;
  columns: GridColumnPreference[];
  version: number;
}

export interface GridPreferenceDefinition {
  gridKey: string;
  schemaVersion: number;
  columnKeys: readonly string[];
}

const definitions = new Map<string, Readonly<GridPreferenceDefinition>>([
  ['identity.users', Object.freeze({
    gridKey: 'identity.users',
    schemaVersion: 1,
    columnKeys: Object.freeze([
      'displayName',
      'username',
      'status',
      'actions'
    ])
  })]
]);

/** 返回当前客户端版本显式发布的 Grid 定义，未知键始终拒绝。 */
export function gridPreferenceDefinitionFor(
  gridKey: string
): Readonly<GridPreferenceDefinition> | undefined {
  return definitions.get(gridKey);
}

/** 创建只包含可信列键和规范展示值的保存请求。 */
export function createGridPreferenceRequest(
  definition: Readonly<GridPreferenceDefinition>,
  columns: readonly GridColumnPreference[],
  version: number
): UpdateGridPreferenceRequest {
  if (!Number.isInteger(version) || version < 0) {
    throw new TypeError('invalid preference version');
  }

  const allowed = new Set(definition.columnKeys);
  const seenKeys = new Set<string>();
  const seenOrders = new Set<number>();
  for (const column of columns) {
    if (!allowed.has(column.columnKey)) {
      throw new TypeError(`unknown column: ${column.columnKey}`);
    }
    if (seenKeys.has(column.columnKey)) {
      throw new TypeError(`duplicate column: ${column.columnKey}`);
    }
    seenKeys.add(column.columnKey);
    if (!isGridColumnPreference(column) || seenOrders.has(column.order)) {
      throw new TypeError('invalid column preference');
    }
    seenOrders.add(column.order);
  }

  return {
    schemaVersion: definition.schemaVersion,
    columns: columns
      .map(column => ({ ...column }))
      .sort((left, right) => left.order - right.order
        || left.columnKey.localeCompare(right.columnKey)),
    version
  };
}

export function isGridPreferenceResponse(
  value: unknown
): value is GridPreferenceResponse {
  if (!isRecord(value)
    || typeof value.gridKey !== 'string'
    || !Number.isInteger(value.schemaVersion)
    || (value.schemaVersion as number) < 1
    || !Number.isInteger(value.version)
    || (value.version as number) < 0
    || !Array.isArray(value.columns)) {
    return false;
  }

  const definition = gridPreferenceDefinitionFor(value.gridKey);
  if (!definition || value.schemaVersion !== definition.schemaVersion) {
    return false;
  }

  const keys = new Set<string>();
  const orders = new Set<number>();
  return value.columns.every(column => {
    if (!isGridColumnPreference(column)
      || !definition.columnKeys.includes(column.columnKey)
      || keys.has(column.columnKey)
      || orders.has(column.order)) {
      return false;
    }

    keys.add(column.columnKey);
    orders.add(column.order);
    return true;
  });
}

function isGridColumnPreference(
  value: unknown
): value is GridColumnPreference {
  return isRecord(value)
    && typeof value.columnKey === 'string'
    && Number.isInteger(value.order)
    && (value.order as number) >= 0
    && (value.width === null
      || (Number.isInteger(value.width)
        && (value.width as number) >= 48
        && (value.width as number) <= 2000))
    && typeof value.visible === 'boolean'
    && (value.fixed === null || value.fixed === 'left' || value.fixed === 'right');
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
