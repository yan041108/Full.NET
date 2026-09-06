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
  version: number;
}

export interface ImportExportTaskDetailResponse extends ImportExportTaskResponse {
  previewRows: StaticImportRowPreviewResult[];
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
    && Number.isInteger(value.version);
}

export function isImportExportTaskDetailResponse(
  value: unknown
): value is ImportExportTaskDetailResponse {
  return isImportExportTaskResponse(value)
    && Array.isArray(value.previewRows);
}

export function isImportExportTaskPage(value: unknown): value is ImportExportTaskPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isImportExportTaskResponse)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}
