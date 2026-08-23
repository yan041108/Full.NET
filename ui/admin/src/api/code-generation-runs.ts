import {

  buildCodeGenerationRollbackApplyRunIds,

  codeGenerationApplyRun,

  codeGenerationDownloadRunArtifacts,

  codeGenerationListRuns,

  codeGenerationPreviewRun,

  codeGenerationRollbackRun,

  codeGenerationRollbackRunChain,

  isCodeGenerationRunApplyResponse,

  isCodeGenerationRunPage,

  isCodeGenerationRunPreviewResponse,

  isCodeGenerationRunRollbackChainResponse,

  isCodeGenerationRunRollbackResponse,

  type CodeGenerationRunApplyRequest,

  type CodeGenerationRunApplyResponse,

  type CodeGenerationRunPage,

  type CodeGenerationRunPreviewRequest,

  type CodeGenerationRunPreviewResponse,

  type CodeGenerationRunResponse,

  type CodeGenerationRunRollbackChainRequest,

  type CodeGenerationRunRollbackChainResponse,

  type CodeGenerationRunRollbackRequest,

  type CodeGenerationRunRollbackResponse,

  type CodeGenerationRunStatus

} from '@fullnet/client-contracts';

import { http } from './http';



export async function previewTrackedCodeGeneration(

  input: CodeGenerationRunPreviewRequest,

  signal?: AbortSignal

): Promise<CodeGenerationRunPreviewResponse> {

  const value = await codeGenerationPreviewRun(
    http,
    { body: input as unknown as Parameters<typeof codeGenerationPreviewRun>[1]['body'] },
    signal
  );

  if (!isCodeGenerationRunPreviewResponse(value)) {

    throw new Error('client.invalid_code_generation_run_preview');

  }



  return value;

}



export async function applyTrackedCodeGeneration(

  input: CodeGenerationRunApplyRequest,

  signal?: AbortSignal

): Promise<CodeGenerationRunApplyResponse> {

  const value = await codeGenerationApplyRun(http, { body: input }, signal);

  if (!isCodeGenerationRunApplyResponse(value)) {

    throw new Error('client.invalid_code_generation_run_apply');

  }



  return value;

}



export async function rollbackTrackedCodeGeneration(

  input: CodeGenerationRunRollbackRequest,

  signal?: AbortSignal

): Promise<CodeGenerationRunRollbackResponse> {

  const value = await codeGenerationRollbackRun(http, { body: input }, signal);

  if (!isCodeGenerationRunRollbackResponse(value)) {

    throw new Error('client.invalid_code_generation_run_rollback');

  }



  return value;

}



export async function rollbackChainTrackedCodeGeneration(

  input: CodeGenerationRunRollbackChainRequest,

  signal?: AbortSignal

): Promise<CodeGenerationRunRollbackChainResponse> {

  const value = await codeGenerationRollbackRunChain(http, { body: input }, signal);

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

  status?: CodeGenerationRunStatus,

  signal?: AbortSignal

): Promise<CodeGenerationRunPage> {

  const value = await codeGenerationListRuns(

    http,

    {

      page: 1,

      pageSize: 20,

      status

    },

    signal

  );

  if (!isCodeGenerationRunPage(value)) {

    throw new Error('client.invalid_code_generation_run_page');

  }



  return value;

}



export async function downloadCodeGenerationArtifacts(

  runId: string,

  signal?: AbortSignal

): Promise<Blob> {

  return codeGenerationDownloadRunArtifacts(http, { runId }, signal);

}



export type {

  CodeGenerationRunApplyRequest,

  CodeGenerationRunApplyResponse,

  CodeGenerationRunPage,

  CodeGenerationRunPreviewRequest,

  CodeGenerationRunPreviewResponse,

  CodeGenerationRunResponse,

  CodeGenerationRunRollbackChainRequest,

  CodeGenerationRunRollbackChainResponse,

  CodeGenerationRunRollbackRequest,

  CodeGenerationRunRollbackResponse,

  CodeGenerationRunStatus

};


