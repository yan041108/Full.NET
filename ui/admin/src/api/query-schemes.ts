import { isCursorPage, isSavedQueryDefinition, isSavedQueryDetail, isSavedQuerySummary, isSavedQueryDefault,
  type CursorPage, type SavedQueryDefinition, type SavedQueryDetail, type SavedQuerySummary, type SavedQueryDefault, type SavePersonalQueryRequest } from '@fullnet/client-contracts';
import { http } from './http';
import { settingsGetQuerySchemeSchema, settingsListQuerySchemes, settingsGetQueryScheme, settingsCreateQueryScheme,
  settingsUpdateQueryScheme, settingsShareQueryScheme, settingsDeleteQueryScheme, settingsGetDefaultQueryScheme,
  settingsSetDefaultQueryScheme } from '@fullnet/client-contracts';

const requireValue = <T>(value: unknown, guard: (value: unknown) => value is T): T => {
  if (!guard(value)) throw new TypeError('client.invalid_query_scheme_response'); return value;
};
export async function getQuerySchemeSchema(grid: string, signal?: AbortSignal): Promise<SavedQueryDefinition> {
  return requireValue(await settingsGetQuerySchemeSchema(http, { gridKey: grid }, signal), isSavedQueryDefinition);
}
export async function listQuerySchemes(grid: string, shared = false, cursor?: string, signal?: AbortSignal): Promise<CursorPage<SavedQuerySummary>> {
  return requireValue(await settingsListQuerySchemes(http, { gridKey: grid, shared, pageSize: 20, cursor }, signal), value => isCursorPage(value, isSavedQuerySummary));
}
export async function getQueryScheme(grid: string, id: string, signal?: AbortSignal): Promise<SavedQueryDetail> {
  return requireValue(await settingsGetQueryScheme(http, { gridKey: grid, schemeId: id }, signal), isSavedQueryDetail);
}
export async function saveQueryScheme(grid: string, id: string | undefined, request: SavePersonalQueryRequest, signal?: AbortSignal): Promise<SavedQueryDetail> {
  const value = id ? await settingsUpdateQueryScheme(http, { gridKey: grid, schemeId: id, body: request }, signal)
    : await settingsCreateQueryScheme(http, { gridKey: grid, body: request }, signal);
  return requireValue(value, isSavedQueryDetail);
}
export async function shareQueryScheme(grid: string, id: string, isShared: boolean, version: number, signal?: AbortSignal): Promise<SavedQueryDetail> {
  return requireValue(await settingsShareQueryScheme(http, { gridKey: grid, schemeId: id, body: { isShared, version } }, signal), isSavedQueryDetail);
}
export async function deleteQueryScheme(grid: string, id: string, version: number, signal?: AbortSignal): Promise<void> {
  const value = await settingsDeleteQueryScheme(http, { gridKey: grid, schemeId: id, body: { version } }, signal);
  if (value !== true) throw new TypeError('client.invalid_query_scheme_response');
}
export async function getDefaultQueryScheme(grid: string, signal?: AbortSignal): Promise<SavedQueryDefault> {
  return requireValue(await settingsGetDefaultQueryScheme(http, { gridKey: grid }, signal), isSavedQueryDefault);
}
export async function setDefaultQueryScheme(grid: string, schemeId: string | null, version: number, signal?: AbortSignal): Promise<SavedQueryDefault> {
  return requireValue(await settingsSetDefaultQueryScheme(http, { gridKey: grid, body: { schemeId, version } }, signal), isSavedQueryDefault);
}
