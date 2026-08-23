import {
  codeGenerationListCatalogColumns,
  codeGenerationListCatalogTables,
  codeGenerationSyncCatalogColumns,
  isCodeGenerationCatalogColumnListResponse,
  isCodeGenerationCatalogColumnSyncResponse,
  isCodeGenerationCatalogTableResponse,
  type CodeGenerationCatalogColumnListResponse,
  type CodeGenerationCatalogColumnSyncResponse,
  type CodeGenerationCatalogTableResponse,
  type CodeGenerationPreviewColumnRequest
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listCodeGenerationCatalogTables(
  signal?: AbortSignal
): Promise<CodeGenerationCatalogTableResponse[]> {
  const value = await codeGenerationListCatalogTables(http, {}, signal);
  if (!Array.isArray(value)
    || !value.every(isCodeGenerationCatalogTableResponse)) {
    throw new Error('client.invalid_code_generation_catalog_tables');
  }

  return value;
}

export async function listCodeGenerationCatalogColumns(
  tableName: string,
  signal?: AbortSignal
): Promise<CodeGenerationCatalogColumnListResponse> {
  const value = await codeGenerationListCatalogColumns(
    http,
    { tableName },
    signal
  );
  if (!isCodeGenerationCatalogColumnListResponse(value)) {
    throw new Error('client.invalid_code_generation_catalog_columns');
  }

  return value;
}

export async function syncCodeGenerationCatalogColumns(
  tableName: string,
  columns: CodeGenerationPreviewColumnRequest[],
  signal?: AbortSignal
): Promise<CodeGenerationCatalogColumnSyncResponse> {
  const value = await codeGenerationSyncCatalogColumns(
    http,
    { body: { tableName, columns } },
    signal
  );
  if (!isCodeGenerationCatalogColumnSyncResponse(value)) {
    throw new Error('client.invalid_code_generation_catalog_sync');
  }

  return value;
}

export type {
  CodeGenerationCatalogColumnListResponse,
  CodeGenerationCatalogColumnSyncResponse,
  CodeGenerationCatalogTableResponse,
  CodeGenerationPreviewColumnRequest
};
