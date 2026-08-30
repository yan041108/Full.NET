import {
  workflowGetInstance,
  workflowListInstanceExecutionLogs,
  type WorkflowExecutionLogResponse,
  type WorkflowInstanceResponse
} from '@fullnet/client-contracts';
import { http } from './http';

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
  WorkflowExecutionLogResponse,
  WorkflowInstanceResponse
};
