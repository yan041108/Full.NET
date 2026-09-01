import {

  codeGenerationPreviewCrud,

  isCodeGenerationPreviewResponse,

  type CodeGenerationPreviewRequest,

  type CodeGenerationPreviewResponse

} from '@fullnet/client-contracts';

import { http } from './http';



/** 生成 CRUD 代码预览，并对返回结构做失败关闭校验。 */
export async function previewCodeGeneration(

  input: CodeGenerationPreviewRequest,

  signal?: AbortSignal

): Promise<CodeGenerationPreviewResponse> {

  const value = await codeGenerationPreviewCrud(
    http,
    { body: input as unknown as Parameters<typeof codeGenerationPreviewCrud>[1]['body'] },
    signal
  );

  if (!isCodeGenerationPreviewResponse(value)) {

    throw new Error('client.invalid_code_generation_preview');

  }



  return value;

}



/** 导出代码生成预览请求与响应模型，供模板页、预览页与测试夹具共享同一契约。 */
export type { CodeGenerationPreviewRequest, CodeGenerationPreviewResponse };


