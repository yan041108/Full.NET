import {
  createGridPreferenceRequest,
  gridPreferenceDefinitionFor,
  isGridPreferenceResponse,
  type GridColumnFixed,
  type GridPreferenceResponse,
  type UpdateGridPreferenceRequest
} from '@fullnet/client-contracts';

/** Vue 表格列定义的最小公共形状，用于把远端偏好映射回本地列配置。 */
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
  /** 按统一 API 路径发送偏好请求，保证 load/save/reset 使用同一资源定位。 */
  const send = async (
    gridKey: string,
    init?: RequestInit
  ): Promise<unknown> =>
    request(
      `/api/v1/me/grid-preferences/${encodeURIComponent(gridKey)}`,
      init
    );

  /** 要求 gridKey 必须已在本地目录声明，未知表格禁止静默发送远端请求。 */
  const requireDefinition = (gridKey: string) => {
    const definition = gridPreferenceDefinitionFor(gridKey);
    if (!definition) {
      throw new TypeError(`unknown Grid: ${gridKey}`);
    }
    return definition;
  };

  /** 要求响应结构与请求的 gridKey 对齐，防止串用其他表格的偏好载荷。 */
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
    /** 加载表格偏好；未知表格直接返回 undefined，方便页面按默认列配置降级。 */
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
    /** 保存表格偏好；schemaVersion 不一致时失败关闭，避免旧页面覆盖新结构。 */
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
    /** 重置为服务端默认偏好。 */
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

  /** 只合并服务端显式回传的列，并保留本地未知列，兼容前端先行新增列的场景。 */
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
