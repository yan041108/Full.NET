import {
  readWorkflowFormVersionSchema,
  workflowGetFormVersion,
  workflowListDefinitions,
  workflowListDefinitionVersions,
  workflowStartInstance,
  type WorkflowDefinitionResponse,
  type WorkflowDefinitionVersionResponse,
  type WorkflowFormSchema,
  type WorkflowFormVersionResponse,
  type WorkflowInstanceResponse,
  type WorkflowSubmission
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listWorkflowDefinitions(
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse[]> {
  return workflowListDefinitions(http, {}, signal);
}

export async function listWorkflowDefinitionVersions(
  definitionId: string,
  signal?: AbortSignal
): Promise<WorkflowDefinitionVersionResponse[]> {
  return workflowListDefinitionVersions(http, { definitionId }, signal);
}

export async function getWorkflowStartForm(
  formVersionId: string,
  signal?: AbortSignal
): Promise<{
  version: WorkflowFormVersionResponse;
  schema: WorkflowFormSchema;
}> {
  const version = await workflowGetFormVersion(http, { versionId: formVersionId }, signal);
  return { version, schema: readWorkflowFormVersionSchema(version) };
}

export async function startWorkflowInstance(
  definitionVersionId: string,
  businessType: string,
  businessId: string,
  initialValues: WorkflowSubmission,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return workflowStartInstance(http, {
    body: {
      definitionVersionId,
      businessType,
      businessId,
      initialValues,
      idempotencyKey
    }
  }, signal);
}

export type {
  WorkflowDefinitionResponse,
  WorkflowDefinitionVersionResponse,
  WorkflowFormSchema,
  WorkflowInstanceResponse,
  WorkflowSubmission
};
