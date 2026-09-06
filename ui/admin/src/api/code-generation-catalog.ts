import {
  codeGenerationGenerateCatalogMigrationDraft,
  codeGenerationGetCatalogMetadata,
  codeGenerationListCatalogColumns,
  codeGenerationListCatalogObjects,
  codeGenerationListCatalogTables,
  codeGenerationSyncCatalogColumns,
  isCodeGenerationCatalogColumnListResponse,
  isCodeGenerationCatalogColumnSyncResponse,
  isCodeGenerationCatalogMetadataResponse,
  isCodeGenerationCatalogMigrationDraftResponse,
  isCodeGenerationCatalogObjectResponse,
  isCodeGenerationCatalogTableResponse,
  type CodeGenerationCatalogColumnListResponse,
  type CodeGenerationCatalogColumnSyncResponse,
  type CodeGenerationCatalogMetadataResponse,
  type CodeGenerationCatalogMigrationDraftResponse,
  type CodeGenerationCatalogObjectResponse,
  type CodeGenerationCatalogTableResponse,
  type CodeGenerationPreviewColumnRequest
} from '@fullnet/client-contracts';
import { http } from './http';

/** 查询代码生成目录中的表列表，并对每个条目做失败关闭校验。 */
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

/** 查询代码生成目录中的表与视图对象列表。 */
export async function listCodeGenerationCatalogObjects(
  signal?: AbortSignal
): Promise<CodeGenerationCatalogObjectResponse[]> {
  const value = await codeGenerationListCatalogObjects(http, {}, signal);
  if (!Array.isArray(value)
    || !value.every(isCodeGenerationCatalogObjectResponse)) {
    throw new Error('client.invalid_code_generation_catalog_objects');
  }

  return value;
}

/** 查询指定表的代码生成列目录。 */
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

/** 查询表或视图的原始列元数据。 */
export async function getCodeGenerationCatalogMetadata(
  objectName: string,
  signal?: AbortSignal
): Promise<CodeGenerationCatalogMetadataResponse> {
  const value = await codeGenerationGetCatalogMetadata(
    http,
    { objectName },
    signal
  );
  if (!isCodeGenerationCatalogMetadataResponse(value)) {
    throw new Error('client.invalid_code_generation_catalog_metadata');
  }

  return value;
}

/** 基于基础表生成双库迁移草案文本。 */
export async function generateCodeGenerationCatalogMigrationDraft(
  tableName: string,
  signal?: AbortSignal
): Promise<CodeGenerationCatalogMigrationDraftResponse> {
  const value = await codeGenerationGenerateCatalogMigrationDraft(
    http,
    { body: { tableName } },
    signal
  );
  if (!isCodeGenerationCatalogMigrationDraftResponse(value)) {
    throw new Error('client.invalid_code_generation_catalog_migration_draft');
  }

  return value;
}

/** 同步指定表的预览列定义，并对同步结果做失败关闭校验。 */
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

/** 导出代码生成目录查询与列同步模型，供目录页、预览编排和测试夹具共享同一契约。 */
export type {
  CodeGenerationCatalogColumnListResponse,
  CodeGenerationCatalogColumnSyncResponse,
  CodeGenerationCatalogMetadataResponse,
  CodeGenerationCatalogMigrationDraftResponse,
  CodeGenerationCatalogObjectResponse,
  CodeGenerationCatalogTableResponse,
  CodeGenerationPreviewColumnRequest
};
