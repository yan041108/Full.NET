import {
  isCodeGenerationRunApplyResponse,
  isCodeGenerationRunPage,
  isCodeGenerationRunRollbackResponse,
  isCodeGenerationRunRollbackChainResponse,
  isCodeGenerationRunPreviewResponse,
  buildCodeGenerationRollbackApplyRunIds,
  type CodeGenerationRunApplyRequest,
  type CodeGenerationRunApplyResponse,
  type CodeGenerationRunPage,
  type CodeGenerationRunRollbackRequest,
  type CodeGenerationRunRollbackResponse,
  type CodeGenerationRunRollbackChainRequest,
  type CodeGenerationRunRollbackChainResponse,
  type CodeGenerationRunPreviewRequest,
  type CodeGenerationRunPreviewResponse,
  type CodeGenerationRunResponse,
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

export async function rollbackTrackedCodeGeneration(
  input: CodeGenerationRunRollbackRequest
): Promise<CodeGenerationRunRollbackResponse> {
  const value = await request<unknown>(
    '/api/v1/code-generation/runs/rollback',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(input)
    }
  );
  if (!isCodeGenerationRunRollbackResponse(value)) {
    throw new Error('client.invalid_code_generation_run_rollback');
  }

  return value;
}

export async function rollbackChainTrackedCodeGeneration(
  input: CodeGenerationRunRollbackChainRequest
): Promise<CodeGenerationRunRollbackChainResponse> {
  const value = await request<unknown>(
    '/api/v1/code-generation/runs/rollback-chain',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(input)
    }
  );
  if (!isCodeGenerationRunRollbackChainResponse(value)) {
    throw new Error('client.invalid_code_generation_run_rollback_chain');
  }

  return value;
}

export async function executeTrackedCodeGenerationRollback(
  runs: readonly CodeGenerationRunResponse[],
  targetApplyRunId: string
): Promise<void> {
  const applyRunIds = buildCodeGenerationRollbackApplyRunIds(
    runs,
    targetApplyRunId
  );
  if (applyRunIds.length === 1) {
    await rollbackTrackedCodeGeneration({ applyRunId: applyRunIds[0] });
    return;
  }

  await rollbackChainTrackedCodeGeneration({ applyRunIds });
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
