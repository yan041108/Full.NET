import {
  isCodeGenerationPreviewResponse,
  type CodeGenerationPreviewRequest,
  type CodeGenerationPreviewResponse
} from '@fullnet/client-contracts';
import { request } from './http';

export async function previewCodeGeneration(
  input: CodeGenerationPreviewRequest
): Promise<CodeGenerationPreviewResponse> {
  const value = await request<unknown>(
    '/api/v1/code-generation/previews',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(input)
    }
  );
  if (!isCodeGenerationPreviewResponse(value)) {
    throw new Error('client.invalid_code_generation_preview');
  }

  return value;
}
