import {
  readWorkflowDefinitionResponse,
  readWorkflowDefinitionVersionResponse,
  type WorkflowDefinitionDraft,
  type WorkflowDefinitionResponse,
  type WorkflowDefinitionVersionResponse
} from '@fullnet/client-contracts';
import { http } from './http';

export async function getWorkflowDefinition(
  definitionId: string,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  const value = await http.request<unknown>(
    `/api/v1/workflow/definitions/${encodeURIComponent(definitionId)}`,
    { method: 'GET' },
    signal
  );
  return readWorkflowDefinitionResponse(value);
}

export async function getWorkflowNodeTypeCatalog(
  signal?: AbortSignal
): Promise<WorkflowNodeTypeCatalogResponse> {
  const value = await http.request<unknown>(
    '/api/v1/workflow/definitions/node-type-catalog',
    { method: 'GET' },
    signal
  );
  if (!isWorkflowNodeTypeCatalogResponse(value)) {
    throw new Error('client.invalid_workflow_node_catalog');
  }
  return value;
}

export async function createWorkflowDefinition(
  definitionKey: string,
  draft: WorkflowDefinitionDraft,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  const value = await http.request<unknown>('/api/v1/workflow/definitions/', jsonRequest('POST', {
    definitionKey,
    draft
  }), signal);
  return readWorkflowDefinitionResponse(value);
}

export async function updateWorkflowDefinitionDraft(
  definitionId: string,
  expectedRevision: number,
  draft: WorkflowDefinitionDraft,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  const value = await http.request<unknown>(
    `/api/v1/workflow/definitions/${encodeURIComponent(definitionId)}/draft`,
    jsonRequest('PUT', { expectedRevision, draft }),
    signal
  );
  return readWorkflowDefinitionResponse(value);
}

export async function publishWorkflowDefinition(
  definitionId: string,
  expectedRevision: number,
  formVersionId: string,
  signal?: AbortSignal
): Promise<WorkflowDefinitionVersionResponse> {
  const value = await http.request<unknown>(
    `/api/v1/workflow/definitions/${encodeURIComponent(definitionId)}/publish`,
    jsonRequest('POST', { expectedRevision, formVersionId }),
    signal
  );
  return readWorkflowDefinitionVersionResponse(value);
}

function jsonRequest(method: 'POST' | 'PUT', body: unknown): RequestInit {
  return {
    method,
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  };
}

export type {
  WorkflowDefinitionDraft,
  WorkflowDefinitionResponse,
  WorkflowDefinitionVersionResponse
};

export interface WorkflowNodeTypeCatalogResponse {
  readonly catalogVersion: number;
  readonly definitionSchemaVersion: number;
  readonly nodeTypes: readonly WorkflowNodeTypeResponse[];
}

export interface WorkflowNodeTypeResponse {
  readonly nodeTypeKey: string;
  readonly nodeSchemaVersion: number;
  readonly designable: boolean;
  readonly publishable: boolean;
  readonly executable: boolean;
  readonly supportsFieldPolicies: boolean;
}

function isWorkflowNodeTypeCatalogResponse(value: unknown): value is WorkflowNodeTypeCatalogResponse {
  if (!isRecord(value) || !Number.isInteger(value.catalogVersion)
    || !Number.isInteger(value.definitionSchemaVersion) || !Array.isArray(value.nodeTypes)) {
    return false;
  }
  return value.nodeTypes.every(node => isRecord(node)
    && typeof node.nodeTypeKey === 'string'
    && Number.isInteger(node.nodeSchemaVersion)
    && typeof node.designable === 'boolean'
    && typeof node.publishable === 'boolean'
    && typeof node.executable === 'boolean'
    && typeof node.supportsFieldPolicies === 'boolean');
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
