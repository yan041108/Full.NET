import {
  createGridPreferenceRequest,
  gridPreferenceDefinitionFor,
  isGridPreferenceResponse,
  type GridColumnFixed,
  type GridPreferenceResponse,
  type UpdateGridPreferenceRequest
} from '@fullnet/client-contracts';

export interface VueGridColumn {
  key: string;
  width?: number;
  hidden?: boolean;
  fixed?: boolean | Exclude<GridColumnFixed, null>;
}

type GridPreferenceRequest = (
  path: string,
  init?: RequestInit
) => Promise<unknown>;

/** 创建可注入 HTTP 边界的远端 Grid 偏好客户端。 */
export function createGridPreferenceClient(request: GridPreferenceRequest) {
  const send = async (
    gridKey: string,
    init?: RequestInit
  ): Promise<unknown> =>
    request(
      `/api/v1/me/grid-preferences/${encodeURIComponent(gridKey)}`,
      init
    );

  const requireDefinition = (gridKey: string) => {
    const definition = gridPreferenceDefinitionFor(gridKey);
    if (!definition) {
      throw new TypeError(`unknown Grid: ${gridKey}`);
    }
    return definition;
  };

  const requireResponse = (
    gridKey: string,
    response: unknown
  ): GridPreferenceResponse => {
    if (!isGridPreferenceResponse(response) || response.gridKey !== gridKey) {
      throw new TypeError('invalid Grid preference response');
    }
    return response;
  };

  return {
    load: async (gridKey: string): Promise<GridPreferenceResponse | undefined> => {
      const definition = gridPreferenceDefinitionFor(gridKey);
      if (!definition) {
        return undefined;
      }

      const response = await send(gridKey);
      return isGridPreferenceResponse(response)
        && response.gridKey === definition.gridKey
        ? response
        : undefined;
    },
    save: async (gridKey: string, body: UpdateGridPreferenceRequest) => {
      const definition = requireDefinition(gridKey);
      if (body.schemaVersion !== definition.schemaVersion) {
        throw new TypeError('stale Grid schema version');
      }
      const normalized = createGridPreferenceRequest(
        definition,
        body.columns,
        body.version
      );
      return requireResponse(
        gridKey,
        await send(gridKey, {
          method: 'PUT',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(normalized)
        })
      );
    },
    reset: async (gridKey: string) => {
      requireDefinition(gridKey);
      return requireResponse(
        gridKey,
        await send(gridKey, { method: 'DELETE' })
      );
    }
  };
}

/** 将可信且同版本的偏好合并到 Vue 本地列定义。 */
export function applyGridPreference<TColumn extends VueGridColumn>(
  gridKey: string,
  columns: readonly TColumn[],
  preference: GridPreferenceResponse | undefined
): TColumn[] {
  const definition = gridPreferenceDefinitionFor(gridKey);
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
            width: selected.width ?? column.width,
            hidden: !selected.visible,
            fixed: selected.fixed ?? false
          }
        : column;
    })
    .sort((left, right) => {
      const leftOrder = byKey.get(left.key)?.order
        ?? Number.MAX_SAFE_INTEGER;
      const rightOrder = byKey.get(right.key)?.order
        ?? Number.MAX_SAFE_INTEGER;
      return leftOrder - rightOrder
        || originalOrder.get(left.key)! - originalOrder.get(right.key)!;
    });
}
