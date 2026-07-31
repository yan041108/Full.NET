import {
  createGridPreferenceRequest,
  gridPreferenceDefinitionFor,
  isGridPreferenceResponse
} from '@fullnet/client-contracts';

/** 创建可注入 HTTP 边界的远端 Grid 偏好客户端。 */
export function createGridPreferenceClient(request) {
  const send = (gridKey, init) =>
    request(
      `/api/v1/me/grid-preferences/${encodeURIComponent(gridKey)}`,
      init
    );

  const requireDefinition = gridKey => {
    const definition = gridPreferenceDefinitionFor(gridKey);
    if (!definition) {
      throw new TypeError(`unknown Grid: ${gridKey}`);
    }
    return definition;
  };

  const requireResponse = (gridKey, response) => {
    if (!isGridPreferenceResponse(response) || response.gridKey !== gridKey) {
      throw new TypeError('invalid Grid preference response');
    }
    return response;
  };

  return {
    load: async gridKey => {
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
    save: async (gridKey, body) => {
      const definition = requireDefinition(gridKey);
      if (body.schemaVersion !== definition.schemaVersion) {
        throw new TypeError('stale Grid schema version');
      }
      const normalized = createGridPreferenceRequest(
        definition,
        body.columns,
        body.version
      );
      return requireResponse(gridKey, await send(gridKey, {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(normalized)
      }));
    },
    reset: async gridKey => {
      requireDefinition(gridKey);
      return requireResponse(
        gridKey,
        await send(gridKey, { method: 'DELETE' })
      );
    }
  };
}

/** 将可信且同版本的偏好合并到 Layui 本地列定义。 */
export function applyGridPreference(gridKey, columns, preference) {
  const definition = gridPreferenceDefinitionFor(gridKey);
  const fallback = columns.map(column => ({ ...column }));
  if (!definition
    || !preference
    || preference.gridKey !== definition.gridKey
    || preference.schemaVersion !== definition.schemaVersion) {
    return fallback;
  }

  const byKey = new Map(preference.columns.map(column => [column.columnKey, column]));
  const originalOrder = new Map(columns.map((column, index) => [column.field, index]));
  return fallback
    .map(column => {
      const selected = byKey.get(column.field);
      return selected
        ? {
            ...column,
            width: selected.width ?? column.width,
            hide: !selected.visible,
            fixed: selected.fixed ?? false
          }
        : column;
    })
    .sort((left, right) => {
      const leftOrder = byKey.get(left.field)?.order ?? Number.MAX_SAFE_INTEGER;
      const rightOrder = byKey.get(right.field)?.order ?? Number.MAX_SAFE_INTEGER;
      return leftOrder - rightOrder
        || originalOrder.get(left.field) - originalOrder.get(right.field);
    });
}
