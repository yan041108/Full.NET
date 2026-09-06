import {
  isCodeGenerationPreviewColumnRequest,
  type CodeGenerationPreviewColumnRequest
} from './code-generation-previews.js';

export interface CodeGenerationCatalogTableResponse {
  tableName: string;
}

export interface CodeGenerationCatalogObjectResponse {
  objectName: string;
  objectKind: 'table' | 'view';
}

export interface CodeGenerationCatalogMetadataColumnResponse {
  columnName: string;
  dataType: string;
  columnType: string;
  isNullable: boolean;
  maxLength: number | null;
  ordinalPosition: number;
  numericPrecision: number | null;
  numericScale: number | null;
}

export interface CodeGenerationCatalogMetadataResponse {
  objectName: string;
  objectKind: 'table' | 'view';
  columns: CodeGenerationCatalogMetadataColumnResponse[];
}

export interface CodeGenerationCatalogMigrationDraftRequest {
  tableName: string;
}

export interface CodeGenerationCatalogMigrationDraftResponse {
  tableName: string;
  sqlServerDraft: string;
  mySqlDraft: string;
  warnings: string[];
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

export function isCodeGenerationCatalogObjectResponse(
  value: unknown
): value is CodeGenerationCatalogObjectResponse {
  return isRecord(value)
    && isNonEmptyString(value.objectName)
    && (value.objectKind === 'table' || value.objectKind === 'view');
}

export function isCodeGenerationCatalogMetadataColumnResponse(
  value: unknown
): value is CodeGenerationCatalogMetadataColumnResponse {
  return isRecord(value)
    && isNonEmptyString(value.columnName)
    && isNonEmptyString(value.dataType)
    && isNonEmptyString(value.columnType)
    && typeof value.isNullable === 'boolean'
    && (value.maxLength === null || typeof value.maxLength === 'number')
    && typeof value.ordinalPosition === 'number'
    && (value.numericPrecision === null || typeof value.numericPrecision === 'number')
    && (value.numericScale === null || typeof value.numericScale === 'number');
}

export function isCodeGenerationCatalogMetadataResponse(
  value: unknown
): value is CodeGenerationCatalogMetadataResponse {
  return isRecord(value)
    && isNonEmptyString(value.objectName)
    && (value.objectKind === 'table' || value.objectKind === 'view')
    && Array.isArray(value.columns)
    && value.columns.every(isCodeGenerationCatalogMetadataColumnResponse);
}

export function isCodeGenerationCatalogMigrationDraftRequest(
  value: unknown
): value is CodeGenerationCatalogMigrationDraftRequest {
  return isRecord(value) && isNonEmptyString(value.tableName);
}

export function isCodeGenerationCatalogMigrationDraftResponse(
  value: unknown
): value is CodeGenerationCatalogMigrationDraftResponse {
  return isRecord(value)
    && isNonEmptyString(value.tableName)
    && typeof value.sqlServerDraft === 'string'
    && typeof value.mySqlDraft === 'string'
    && Array.isArray(value.warnings)
    && value.warnings.every(item => typeof item === 'string');
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
