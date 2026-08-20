import {
  isCodeGenerationPreviewColumnRequest,
  type CodeGenerationPreviewColumnRequest
} from './code-generation-previews.js';

export interface CodeGenerationCatalogTableResponse {
  tableName: string;
}

export interface CodeGenerationCatalogColumnListResponse {
  tableName: string;
  columns: CodeGenerationPreviewColumnRequest[];
  skippedColumnNames: string[];
}

export interface CodeGenerationCatalogColumnSyncRequest {
  tableName: string;
  columns: CodeGenerationPreviewColumnRequest[];
}

export interface CodeGenerationCatalogColumnSyncResponse {
  tableName: string;
  columns: CodeGenerationPreviewColumnRequest[];
  addedColumnNames: string[];
  removedColumnNames: string[];
  skippedColumnNames: string[];
}

export function isCodeGenerationCatalogTableResponse(
  value: unknown
): value is CodeGenerationCatalogTableResponse {
  return isRecord(value) && isNonEmptyString(value.tableName);
}

export function isCodeGenerationCatalogColumnListResponse(
  value: unknown
): value is CodeGenerationCatalogColumnListResponse {
  return isRecord(value)
    && isNonEmptyString(value.tableName)
    && Array.isArray(value.columns)
    && value.columns.every(isCodeGenerationPreviewColumnRequest)
    && Array.isArray(value.skippedColumnNames)
    && value.skippedColumnNames.every(isNonEmptyString);
}

export function isCodeGenerationCatalogColumnSyncResponse(
  value: unknown
): value is CodeGenerationCatalogColumnSyncResponse {
  if (!isCodeGenerationCatalogColumnListResponse(value)) {
    return false;
  }

  const record = value as unknown as Record<string, unknown>;
  return Array.isArray(record.addedColumnNames)
    && record.addedColumnNames.every(isNonEmptyString)
    && Array.isArray(record.removedColumnNames)
    && record.removedColumnNames.every(isNonEmptyString);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
