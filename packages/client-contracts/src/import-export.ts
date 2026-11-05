import type { ImportColumnMapping } from './generated/models.generated.js';

/** 导入历史的已提交查询；状态和 Schema 与分页共同作用于服务端结果。 */
export interface ImportTaskListQuery { page?: number; pageSize?: number; schemaKey?: string; statusKey?: string }

export interface StaticImportWorksheetDefinition {
  worksheetKey: string;
  displayName: string;
  headerColumns: string[];
}

export interface StaticImportSchemaDefinition {
  schemaKey: string;
  displayName: string;
  scopeKey: string;
  requiredPermission: string;
  worksheets: StaticImportWorksheetDefinition[];
}

export interface StaticImportRowPreviewResult {
  lineNumber: number;
  isValid: boolean;
  errorCode: string | null;
  message: string | null;
}

export interface ImportExportSourceRetention {
  expiresAtUtc: string;
  stateKey: 'available' | 'retained_for_execution' | 'expired' | 'releasing' | 'released';
  releasedAtUtc: string | null;
}

export interface ImportExportTaskResponse {
  id: string;
  tenantId: string;
  schemaKey: string;
  schemaDisplayName: string;
  worksheetKey: string;
  sourceFileId: string;
  sourceFileName: string | null;
  statusKey: string;
  totalRows: number;
  validRowCount: number;
  invalidRowCount: number;
  errorCode: string | null;
  requestedByUserId: string;
  createdAtUtc: string;
  previewCompletedAtUtc: string | null;
  processedRowCount: number;
  succeededRowCount: number;
  executionFailedRowCount: number;
  nextLineNumber: number;
  executionStartedAtUtc: string | null;
  executionCompletedAtUtc: string | null;
  hasErrorReceipt: boolean;
  version: number;
  sourceRetention: ImportExportSourceRetention;
}

export interface ImportExportTaskDetailResponse extends ImportExportTaskResponse {
  previewRows: StaticImportRowPreviewResult[];
  columnMappings?: ImportColumnMapping[] | null;
}

export interface ImportExportTaskPage {
  items: ImportExportTaskResponse[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

export function isStaticImportSchemaDefinition(value: unknown): value is StaticImportSchemaDefinition {
  return isRecord(value)
    && typeof value.schemaKey === 'string'
    && typeof value.displayName === 'string'
    && typeof value.scopeKey === 'string'
    && typeof value.requiredPermission === 'string'
    && Array.isArray(value.worksheets);
}

export function isImportExportSourceRetention(value: unknown): value is ImportExportSourceRetention {
  return isRecord(value) && typeof value.expiresAtUtc === 'string' && Number.isFinite(Date.parse(value.expiresAtUtc))
    && typeof value.stateKey === 'string'
    && ['available', 'retained_for_execution', 'expired', 'releasing', 'released'].includes(value.stateKey)
    && (value.stateKey === 'released'
      ? typeof value.releasedAtUtc === 'string' && Number.isFinite(Date.parse(value.releasedAtUtc))
      : value.releasedAtUtc === null);
}

export function isImportExportTaskResponse(value: unknown): value is ImportExportTaskResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.tenantId)
    && typeof value.schemaKey === 'string'
    && typeof value.schemaDisplayName === 'string'
    && typeof value.worksheetKey === 'string'
    && isGuid(value.sourceFileId)
    && isNullableString(value.sourceFileName)
    && typeof value.statusKey === 'string'
    && Number.isInteger(value.totalRows)
    && Number.isInteger(value.validRowCount)
    && Number.isInteger(value.invalidRowCount)
    && isNullableString(value.errorCode)
    && isGuid(value.requestedByUserId)
    && typeof value.createdAtUtc === 'string'
    && isNullableString(value.previewCompletedAtUtc)
    && Number.isInteger(value.processedRowCount)
    && Number.isInteger(value.succeededRowCount)
    && Number.isInteger(value.executionFailedRowCount)
    && Number.isInteger(value.nextLineNumber)
    && isNullableString(value.executionStartedAtUtc)
    && isNullableString(value.executionCompletedAtUtc)
    && typeof value.hasErrorReceipt === 'boolean'
    && Number.isInteger(value.version)
    && isImportExportSourceRetention(value.sourceRetention);
}

export function isImportExportTaskDetailResponse(
  value: unknown
): value is ImportExportTaskDetailResponse {
  return isImportExportTaskResponse(value)
    && 'previewRows' in value
    && Array.isArray(value.previewRows)
    && (!('columnMappings' in value) || value.columnMappings === undefined || value.columnMappings === null
      || Array.isArray(value.columnMappings) && value.columnMappings.length > 0 && value.columnMappings.length <= 32
        && value.columnMappings.every(column => isRecord(column) && typeof column.targetColumn === 'string'
          && column.targetColumn.length > 0 && column.targetColumn.length <= 64 && typeof column.sourceHeader === 'string'
          && column.sourceHeader.trim().length > 0 && column.sourceHeader.length <= 128 && !/[\u0000-\u001f\u007f-\u009f]/.test(column.sourceHeader))
        && new Set(value.columnMappings.map(column => (column as ImportColumnMapping).targetColumn)).size === value.columnMappings.length
        && new Set(value.columnMappings.map(column => (column as ImportColumnMapping).sourceHeader.trim())).size === value.columnMappings.length);
}

export function isImportExportTaskPage(value: unknown): value is ImportExportTaskPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isImportExportTaskResponse)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}
