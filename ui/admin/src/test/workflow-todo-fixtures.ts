import type { WorkflowTodoDetail, WorkflowTodoListItemResponse } from '../api/workflow-todos';
import { workflowInstance } from './workflow-instance-fixtures';
export const workflowTodo: WorkflowTodoDetail = {
  id: workflowInstance.activeTodoId!, instanceId: workflowInstance.id,
  stepId: '01912345-6789-7abc-8def-0123456789b1', assigneeUserId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
  statusKey: 'active', revision: 3, formVersionId: workflowInstance.formVersionId, formSchemaHash: 'a'.repeat(64),
  formSchema: { schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: 'request', fields: [{ fieldKey: 'summary', fieldTypeKey: 'text', required: true, constraints: {} }] }] },
  submission: { summary: '申请' }, fieldPolicies: { summary: 'required' }, submissionRevision: 4,
  approvalModeKey: 'single', requiredApprovalCount: 1, approvedCount: 0, rejectedCount: 0, pendingCount: 1
};
export const workflowTodoRow: WorkflowTodoListItemResponse = {
  id: workflowTodo.id, instanceId: workflowTodo.instanceId, stepId: workflowTodo.stepId, statusKey: 'active',
  arrivedAtUtc: '2026-10-16T00:00:00Z', completedAtUtc: null, resultActionKey: null, revision: 3,
  businessTitle: workflowInstance.businessTitle, businessType: workflowInstance.businessType, businessId: workflowInstance.businessId,
  instanceStatusKey: 'active', definitionKey: 'request.approval', nodeKey: 'manager'
};
export const workflowTodoPage = (items = [workflowTodoRow], page = 1, total = items.length) => ({ items, page, pageSize: 20, total });
export const workflowTodoReturnTarget = { stepId: '01912345-6789-7abc-8def-0123456789b2', nodeKey: 'origin', assigneeUserId: workflowTodo.assigneeUserId, completedAtUtc: '2026-10-15T00:00:00Z' };
export const workflowTodoCandidate = { id: '01912345-6789-7abc-8def-0123456789bb', username: 'reviewer', displayName: '复核人' };
export const workflowTodoChain = { chainId: '01912345-6789-7abc-8def-0123456789b3', directionKey: 'after' as const, statusKey: 'active', items: [{ itemId: '01912345-6789-7abc-8def-0123456789b4', sequenceNo: 1, assigneeUserId: workflowTodoCandidate.id, statusKey: 'pending', todoId: null }] };
