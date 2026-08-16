import {
  isCodeGenerationCatalogColumnListResponse,
  isCodeGenerationCatalogColumnSyncResponse,
  isCodeGenerationCatalogTableResponse,
  type CodeGenerationCatalogColumnListResponse,
  type CodeGenerationCatalogColumnSyncResponse,
  type CodeGenerationCatalogTableResponse,
  type CodeGenerationPreviewColumnRequest
} from '@fullnet/client-contracts';
import { request } from './http';

const catalogPath = '/api/v1/code-generation/catalog';

export async function listCodeGenerationCatalogTables(): Promise<
  CodeGenerationCatalogTableResponse[]
> {
  const value = await request<unknown>(`${catalogPath}/tables`);
  if (!Array.isArray(value)
    || !value.every(isCodeGenerationCatalogTableResponse)) {
    throw new Error('client.invalid_code_generation_catalog_tables');
  }

  return value;
}

export async function listCodeGenerationCatalogColumns(
  tableName: string
): Promise<CodeGenerationCatalogColumnListResponse> {
  const value = await request<unknown>(
    `${catalogPath}/tables/${encodeURIComponent(tableName)}/columns`
  );
  if (!isCodeGenerationCatalogColumnListResponse(value)) {
    throw new Error('client.invalid_code_generation_catalog_columns');
  }

  return value;
}

export async function syncCodeGenerationCatalogColumns(
  tableName: string,
  columns: CodeGenerationPreviewColumnRequest[]
): Promise<CodeGenerationCatalogColumnSyncResponse> {
  const value = await request<unknown>(`${catalogPath}/column-sync`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ tableName, columns })
  });
  if (!isCodeGenerationCatalogColumnSyncResponse(value)) {
    throw new Error('client.invalid_code_generation_catalog_sync');
  }

  return value;
}
