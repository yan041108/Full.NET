import {
  isCodeGenerationRunApplyResponse,
  isCodeGenerationRunPage,
  isCodeGenerationRunPreviewResponse,
  type CodeGenerationRunApplyRequest,
  type CodeGenerationRunApplyResponse,
  type CodeGenerationRunPage,
  type CodeGenerationRunPreviewRequest,
  type CodeGenerationRunPreviewResponse,
  type CodeGenerationRunStatus
} from '@fullnet/client-contracts';
import { request } from './http';

export async function previewTrackedCodeGeneration(
  input: CodeGenerationRunPreviewRequest
): Promise<CodeGenerationRunPreviewResponse> {
  const value = await request<unknown>(
    '/api/v1/code-generation/runs/preview',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(input)
    }
  );
  if (!isCodeGenerationRunPreviewResponse(value)) {
    throw new Error('client.invalid_code_generation_run_preview');
  }

  return value;
}

export async function applyTrackedCodeGeneration(
  input: CodeGenerationRunApplyRequest
): Promise<CodeGenerationRunApplyResponse> {
  const value = await request<unknown>(
    '/api/v1/code-generation/runs/apply',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(input)
    }
  );
  if (!isCodeGenerationRunApplyResponse(value)) {
    throw new Error('client.invalid_code_generation_run_apply');
  }

  return value;
}

export async function listCodeGenerationRuns(
  status?: CodeGenerationRunStatus
): Promise<CodeGenerationRunPage> {
  const query = new URLSearchParams({
    page: '1',
    pageSize: '20'
  });
  if (status) {
    query.set('status', status);
  }

  const value = await request<unknown>(
    `/api/v1/code-generation/runs?${query.toString()}`
  );
  if (!isCodeGenerationRunPage(value)) {
    throw new Error('client.invalid_code_generation_run_page');
  }

  return value;
}
