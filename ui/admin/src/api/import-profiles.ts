import { importExportListMyImportProfiles, importExportGetMyImportProfile, importExportCreateMyImportProfile,
  importExportUpdateMyImportProfile, importExportDeleteMyImportProfile, readImportProfileSummary, readImportProfileDetail,
  type ImportProfileSummary, type ImportProfileDetail, type ImportColumnMappingRequest } from '@fullnet/client-contracts';
import { http } from './http';

/** 静态目标与来源表头均有界；前端校验不替代服务端的完整归一化与来源授权。 */
export function validImportMapping(value: ImportColumnMappingRequest, headers: readonly string[]): boolean {
  return headers.length > 0 && headers.length <= 32 && Array.isArray(value.headerColumns) && Array.isArray(value.columns)
    && value.headerColumns.length === headers.length && value.headerColumns.every((header, index) => header === headers[index])
    && value.columns.length === headers.length && new Set(value.columns.map(column => column.targetColumn)).size === headers.length
    && value.columns.every(column => headers.includes(column.targetColumn) && typeof column.sourceHeader === 'string'
      && column.sourceHeader.trim().length > 0 && column.sourceHeader.trim().length <= 128 && !/[\u0000-\u001f\u007f-\u009f]/.test(column.sourceHeader))
    && new Set(value.columns.map(column => column.sourceHeader.trim())).size === headers.length
    && new TextEncoder().encode(JSON.stringify(value)).length <= 16384;
}
function summary(value: unknown): value is ImportProfileSummary {
  try { readImportProfileSummary(value); } catch { return false; }
  const row = value as ImportProfileSummary;
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(row.id)
    && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(row.id) && !!row.name.trim() && row.name.length <= 64
    && /^[0-9a-f]{64}$/.test(row.headerFingerprint) && Number.isSafeInteger(row.version) && row.version > 0;
}
function detail(value: unknown, headers: readonly string[], id?: string): ImportProfileDetail {
  const parsed = readImportProfileDetail(value);
  if (!summary(parsed.summary) || id && parsed.summary.id.toLowerCase() !== id.toLowerCase())
    throw new TypeError('client.invalid_import_profile');
  if (parsed.summary.isCompatible ? !parsed.configuration || !validImportMapping(parsed.configuration, headers) : parsed.configuration !== null)
    throw new TypeError('client.invalid_import_profile');
  return parsed;
}
/** 仅查询当前本人、租户与来源，禁止列表夹带配置正文或跨来源标识。 */
export async function listImportProfiles(schemaKey: string, worksheetKey: string, signal?: AbortSignal): Promise<ImportProfileSummary[]> {
  const value = await importExportListMyImportProfiles(http, { schemaKey, worksheetKey }, signal);
  if (!Array.isArray(value) || value.length > 32 || !value.every(summary)
    || new Set(value.map(row => row.id.toLowerCase())).size !== value.length) throw new TypeError('client.invalid_import_profiles');
  return value;
}
export async function getImportProfile(schemaKey: string, worksheetKey: string, id: string, headers: readonly string[], signal?: AbortSignal) {
  return detail(await importExportGetMyImportProfile(http, { schemaKey, worksheetKey, profileId: id }, signal), headers, id);
}
export async function saveImportProfile(schemaKey: string, worksheetKey: string, id: string | undefined,
  name: string, configuration: ImportColumnMappingRequest, version: number, signal?: AbortSignal) {
  const request = { name, configuration, version };
  return detail(id ? await importExportUpdateMyImportProfile(http, { schemaKey, worksheetKey, profileId: id, body: request }, signal)
    : await importExportCreateMyImportProfile(http, { schemaKey, worksheetKey, body: request }, signal), configuration.headerColumns, id);
}
export async function deleteImportProfile(schemaKey: string, worksheetKey: string, profileId: string, version: number, signal?: AbortSignal) {
  const value = await importExportDeleteMyImportProfile(http, { schemaKey, worksheetKey, profileId, body: { version } }, signal);
  if (value !== true) throw new TypeError('client.invalid_import_profile_delete');
}
