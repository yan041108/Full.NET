import {
  workflowCreateDefinition,
  workflowGetDefinition,
  workflowGetNodeTypeCatalog,
  workflowPublishDefinition,
  workflowUpdateDefinitionDraft,
  type WorkflowDefinitionDraft,
  type WorkflowDefinitionResponse,
  type WorkflowDefinitionVersionResponse,
  type WorkflowNodeTypeCatalogResponse,
  type WorkflowNodeTypeResponse
} from '@fullnet/client-contracts';
import { http } from './http';

export async function getWorkflowDefinition(
  definitionId: string,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  return workflowGetDefinition(http, { definitionId }, signal);
}

export async function getWorkflowNodeTypeCatalog(
  signal?: AbortSignal
): Promise<WorkflowNodeTypeCatalogResponse> {
  return workflowGetNodeTypeCatalog(http, {}, signal);
}

export async function createWorkflowDefinition(
  definitionKey: string,
  draft: WorkflowDefinitionDraft,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  return workflowCreateDefinition(http, {
    body: { definitionKey, draft }
  }, signal);
}

export async function updateWorkflowDefinitionDraft(
  definitionId: string,
  expectedRevision: number,
  draft: WorkflowDefinitionDraft,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  return workflowUpdateDefinitionDraft(http, {
    definitionId,
    body: { expectedRevision, draft }
  }, signal);
}

export async function publishWorkflowDefinition(
  definitionId: string,
  expectedRevision: number,
  formVersionId: string,
  signal?: AbortSignal
): Promise<WorkflowDefinitionVersionResponse> {
  return workflowPublishDefinition(http, {
    definitionId,
    body: { expectedRevision, formVersionId }
  }, signal);
}

export type {
  WorkflowDefinitionDraft,
  WorkflowDefinitionResponse,
  WorkflowDefinitionVersionResponse,
  WorkflowNodeTypeCatalogResponse,
  WorkflowNodeTypeResponse
};
