import {
  workflowCancelInstance as cancelInstance,
  workflowGetInstance,
  workflowListInstanceExecutionLogs,
  type CancelWorkflowInstanceRequest,
  type WorkflowExecutionLogResponse,
  type WorkflowInstanceResponse
} from '@fullnet/client-contracts';
import { http } from './http';

export async function cancelWorkflowInstance(
  instanceId: string,
  body: CancelWorkflowInstanceRequest,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return cancelInstance(http, { instanceId, body }, signal);
}

export async function getWorkflowInstance(
  instanceId: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return workflowGetInstance(http, { instanceId }, signal);
}

export async function listWorkflowInstanceExecutionLogs(
  instanceId: string,
  signal?: AbortSignal
): Promise<WorkflowExecutionLogResponse[]> {
  return workflowListInstanceExecutionLogs(http, { instanceId }, signal);
}

export type {
  CancelWorkflowInstanceRequest,
  WorkflowExecutionLogResponse,
  WorkflowInstanceResponse
};
