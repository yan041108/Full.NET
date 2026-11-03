import {

  buildCodeGenerationRollbackApplyRunIds,

  codeGenerationApplyRun,
  codeGenerationPrepareApplyPlan,
  codeGenerationGetWorkspaceState,
  codeGenerationRecoverWorkspace,
  isCodeGenerationWorkspaceStateResponse,
  type CodeGenerationWorkspaceStateResponse,
  type CodeGenerationWorkspaceRecoveryRequest,
  isCodeGenerationRunApplyPlanResponse,
  type CodeGenerationRunApplyPlanRequest,
  type CodeGenerationRunApplyPlanResponse,
  codeGenerationRetryRunPublication,

  codeGenerationDownloadRunArtifacts,

  codeGenerationListRuns,

  codeGenerationPreviewRun,

  codeGenerationRollbackRun,

  codeGenerationRollbackRunChain,

  isCodeGenerationRunApplyResponse,
  isCodeGenerationRunPublicationResponse,
  type CodeGenerationRunPublicationResponse,

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

/** 读取工作区全局顺序和未完成操作，不以运行目录的时间排序推断恢复资格。 */
export async function getCodeGenerationWorkspaceState(signal?: AbortSignal): Promise<CodeGenerationWorkspaceStateResponse> {
  const value = await codeGenerationGetWorkspaceState(http, {}, signal);
  if (!isCodeGenerationWorkspaceStateResponse(value)) throw new Error('client.invalid_code_generation_workspace_state');
  return value;
}

/** 恢复指定的持久化操作；不重新规划或创建 Apply。 */
export async function recoverCodeGenerationWorkspace(input: CodeGenerationWorkspaceRecoveryRequest,
  signal?: AbortSignal): Promise<CodeGenerationWorkspaceStateResponse> {
  const value = await codeGenerationRecoverWorkspace(http, { body: input }, signal);
  if (!isCodeGenerationWorkspaceStateResponse(value)) throw new Error('client.invalid_code_generation_workspace_state');
  return value;
}



/** 生成带追踪信息的代码预览，并对返回结构做失败关闭校验。 */
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



/** 先取得完整文件计划供审查，不能把内存产物预览直接当作写盘确认。 */
export async function prepareTrackedCodeGenerationApply(input: CodeGenerationRunApplyPlanRequest,
  signal?: AbortSignal): Promise<CodeGenerationRunApplyPlanResponse> {
  const value = await codeGenerationPrepareApplyPlan(http,
    { body: input as unknown as Parameters<typeof codeGenerationPrepareApplyPlan>[1]['body'] }, signal);
  if (!isCodeGenerationRunApplyPlanResponse(value)) throw new Error('client.invalid_code_generation_apply_plan');
  return value;
}

/** 按已审查计划和固定幂等键提交，响应丢失后重试相同请求。 */
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



/** 回滚单次代码生成执行。 */
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



/** 批量回滚一条连续的代码生成执行链。 */
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



/** 根据目标应用记录自动选择单次回滚或链式回滚策略。 */
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



/** 查询代码生成运行记录列表。 */
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



/** 只重试同一运行的发布，不重新生成或应用文件。 */
export async function retryCodeGenerationPublication(runId: string, signal?: AbortSignal): Promise<CodeGenerationRunPublicationResponse> {
  const value = await codeGenerationRetryRunPublication(http, { runId }, signal);
  if (!isCodeGenerationRunPublicationResponse(value)) throw new Error('client.invalid_code_generation_publication');
  return value;
}

/** 下载某次代码生成运行产物。 */
export async function downloadCodeGenerationArtifacts(
  runId: string,

  signal?: AbortSignal

): Promise<Blob> {
  return codeGenerationDownloadRunArtifacts(http, { runId }, signal);
}



/** 导出代码生成运行、预览、应用与回滚模型，供预览页、执行历史与回滚流程共享同一契约。 */
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


