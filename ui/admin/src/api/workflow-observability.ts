import { workflowSimulateDefinition, workflowListOperationsSteps, workflowListOperationsBacklog,
  workflowGetSimulationStarterSelections, workflowListSimulationStarterSelectionCandidates,
  type SimulateWorkflowDefinitionRequest, type WorkflowSimulationResponse,
  type WorkflowRecipientCandidatePageResponse,
  type WorkflowOperationsStepsResponse, type WorkflowOperationsBacklogResponse } from '@fullnet/client-contracts';
import { http } from './http';
import { readWorkflowStarterSelectionDescriptors } from './workflow-starter-selections';

export interface WorkflowSimulationSelectionSource {
  readonly kind: 'simulation'; readonly definitionId: string; readonly draftRevision: number; readonly formVersionId: string;
}

/** 只发送服务器草稿锚点，不能传候选范围或借用历史发布版本。 */
export async function getWorkflowSimulationStarterSelections(source: WorkflowSimulationSelectionSource, signal?: AbortSignal) {
  const value = await workflowGetSimulationStarterSelections(http,
    { definitionId: source.definitionId, expectedRevision: source.draftRevision, formVersionId: source.formVersionId }, signal);
  if (value.definitionId !== source.definitionId || value.draftRevision !== source.draftRevision || value.formVersionId !== source.formVersionId)
    throw new TypeError('workflow.simulation.binding_mismatch');
  return { ...value, items: readWorkflowStarterSelectionDescriptors(value.items) };
}

export async function listWorkflowSimulationStarterSelectionCandidates(source: WorkflowSimulationSelectionSource, selectionKey: string,
  page: number, signal?: AbortSignal): Promise<WorkflowRecipientCandidatePageResponse> {
  const value = await workflowListSimulationStarterSelectionCandidates(http, { definitionId: source.definitionId,
    expectedRevision: source.draftRevision, formVersionId: source.formVersionId, selectionKey, page, pageSize: 20 }, signal);
  if (value.page !== page || value.pageSize !== 20 || !Number.isSafeInteger(value.total) || value.total < 0 || value.items.length > 20 ||
    new Set(value.items.map(user => user.id)).size !== value.items.length) throw new TypeError('workflow.invalid_starter_selection');
  return value;
}

/** 仿真读取服务器固定修订，不上传设计器临时定义或调用启动接口。 */
export function simulateWorkflowDefinition(definitionId: string, body: SimulateWorkflowDefinitionRequest, signal?: AbortSignal): Promise<WorkflowSimulationResponse> {
  return workflowSimulateDefinition(http, { definitionId, body }, signal);
}
export function listWorkflowOperationsSteps(query: { page: number; pageSize: number; definitionVersionId?: string; minimumDwellSeconds?: number; onlyOverdue?: boolean }, signal?: AbortSignal): Promise<WorkflowOperationsStepsResponse> {
  return workflowListOperationsSteps(http, query, signal);
}
export function listWorkflowOperationsBacklog(query: { page: number; pageSize: number; definitionVersionId?: string }, signal?: AbortSignal): Promise<WorkflowOperationsBacklogResponse> {
  return workflowListOperationsBacklog(http, query, signal);
}
