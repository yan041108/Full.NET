import {

  codeGenerationPreviewCrud,

  isCodeGenerationPreviewResponse,

  type CodeGenerationPreviewRequest,

  type CodeGenerationPreviewResponse

} from '@fullnet/client-contracts';

import { http } from './http';



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



export type { CodeGenerationPreviewRequest, CodeGenerationPreviewResponse };


