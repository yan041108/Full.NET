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

  CodeGenerationCatalogTableResponse,

  CodeGenerationPreviewColumnRequest

};

