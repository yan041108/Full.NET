import {
  isWorkflowTodoDetail,
  readActWorkflowTodoRequest,
  readPagedResultOfWorkflowTodoListItemResponse,
  readWorkflowInstanceResponse,
  readWorkflowSignatureUpload,
  readWorkflowVoteRequest,
  readWorkflowClaimPoolPage,
  readWorkflowClaimRequest,
  readWorkflowReleaseRequest,
  readDelegateWorkflowTodoRequest,
  readRecallWorkflowTodoDelegationRequest,
  readWorkflowRecipientCandidatePageResponse,
  readWorkflowTodoDelegation,
  type DelegateWorkflowTodoRequest,
  type RecallWorkflowTodoDelegationRequest,
  type WorkflowTodoDelegation,
  type WorkflowRecipientCandidatePageResponse,
  type PagedResult,
  type WorkflowClaimPool,
  type WorkflowClaimRequest,
  type WorkflowReleaseRequest,
  type WorkflowVoteRequest,
  type WorkflowSignatureUpload,
  type ActWorkflowTodoRequest,
  type WorkflowInstanceResponse,
  type WorkflowTodoDetail,
  type WorkflowTodoListItemResponse
} from '@fullnet/client-contracts';
import type { HttpClient } from '../../api/http';
import { HttpProblem } from '../../api/problem-details';
import { readTransferWorkflowTodoRequest, readReturnWorkflowTodoRequest, readCountersignWorkflowTodoRequest,
  readCancelWorkflowTodoCountersignRequest, readRequestWorkflowManualReminder, readWorkflowReturnTargetPage,
  readWorkflowActiveCountersignChain, readWorkflowReminderTargets, readWorkflowReminderReceipt,
  type TransferWorkflowTodoRequest, type ReturnWorkflowTodoRequest, type CountersignWorkflowTodoRequest,
  type CancelWorkflowTodoCountersignRequest, type RequestWorkflowManualReminder,
  type WorkflowTodoReturnTargetResponse, type WorkflowTodoCountersignChainResponse,
  type WorkflowManualReminderTargetsResponse, type WorkflowManualReminderResponse } from '@fullnet/client-contracts';
import type { WorkflowSchemaCache } from './workflow-schema-cache';

export interface WorkflowTodoClient {
  listHistory(page?: number): Promise<PagedResult<WorkflowTodoListItemResponse>>;
  listManagementCandidates(todoId: string, action: 'transfer' | 'countersign', page?: number): Promise<WorkflowRecipientCandidatePageResponse>;
  listReturnTargets(todoId: string, page?: number): Promise<WorkflowTodoReturnTargetResponse[]>;
  countersignChain(todoId: string): Promise<WorkflowTodoCountersignChainResponse | null>;
  transfer(todoId: string, request: TransferWorkflowTodoRequest): Promise<WorkflowInstanceResponse>;
  returnTodo(todoId: string, request: ReturnWorkflowTodoRequest): Promise<WorkflowInstanceResponse>;
  countersign(todoId: string, request: CountersignWorkflowTodoRequest): Promise<WorkflowInstanceResponse>;
  cancelCountersign(todoId: string, request: CancelWorkflowTodoCountersignRequest): Promise<WorkflowInstanceResponse>;
  reminderTargets(instanceId: string): Promise<WorkflowManualReminderTargetsResponse>;
  remind(instanceId: string, request: RequestWorkflowManualReminder): Promise<WorkflowManualReminderResponse>;
  listDelegated(page?: number): Promise<PagedResult<WorkflowTodoListItemResponse>>;
  listDelegationCandidates(todoId: string, page?: number): Promise<WorkflowRecipientCandidatePageResponse>;
  delegation(todoId: string): Promise<WorkflowTodoDelegation | null>;
  delegate(todoId: string, request: DelegateWorkflowTodoRequest): Promise<WorkflowInstanceResponse>;
  recallDelegation(todoId: string, request: RecallWorkflowTodoDelegationRequest): Promise<WorkflowInstanceResponse>;
  downloadAttachment(todoId: string, fileId: string): Promise<ArrayBuffer>;
  listClaims(): Promise<readonly WorkflowClaimPool[]>;
  claim(poolId: string, request: WorkflowClaimRequest): Promise<WorkflowInstanceResponse>;
  release(todoId: string, request: WorkflowReleaseRequest): Promise<WorkflowInstanceResponse>;
  uploadSignature(todoId: string, expectedRevision: number, filePath: string): Promise<WorkflowSignatureUpload>;
  listMine(): Promise<readonly WorkflowTodoListItemResponse[]>;
  get(todoId: string): Promise<WorkflowTodoDetail>;
  approve(todoId: string, request: ActWorkflowTodoRequest): Promise<WorkflowInstanceResponse>;
  complete(todoId: string, request: ActWorkflowTodoRequest): Promise<WorkflowInstanceResponse>;
  reject(todoId: string, request: ActWorkflowTodoRequest): Promise<WorkflowInstanceResponse>;
  vote(todoId: string, request: WorkflowVoteRequest): Promise<WorkflowInstanceResponse>;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

/** 创建只消费本人待办 API 的移动端客户端；所有响应在进入状态层前执行运行时守卫。 */
export function createWorkflowTodoClient(
  http: HttpClient,
  schemaCache: WorkflowSchemaCache
): WorkflowTodoClient {
  return {

    async listHistory(page = 1) {
      requirePage(page);
      return readPagedResultOfWorkflowTodoListItemResponse(await http.request<unknown>({ path: `/api/v1/workflow/todos/mine/history?page=${page}&pageSize=20` }));
    },
    async listManagementCandidates(todoId, action, page = 1) {
      requirePage(page);
      if (action !== 'transfer' && action !== 'countersign') throw new TypeError('workflow.todo.invalid-management-action');
      return readWorkflowRecipientCandidatePageResponse(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/${action}-candidates?page=${page}&pageSize=20` }));
    },
    async listReturnTargets(todoId, page = 1) {
      requirePage(page);
      return readWorkflowReturnTargetPage(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/return-targets?page=${page}&pageSize=20` }));
    },
    async countersignChain(todoId) {
      try {
        return readWorkflowActiveCountersignChain(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/countersign-chain` }));
      } catch (error) {
        if (error instanceof HttpProblem && error.status === 404 && error.code === 'workflow.todo.countersign_chain_not_found') return null;
        throw error;
      }
    },
    async transfer(todoId, request) {
      const body = readTransferWorkflowTodoRequest({ assigneeUserId: request.assigneeUserId, expectedRevision: request.expectedRevision,
        expectedFormRevision: request.expectedFormRevision, comment: request.comment, signatureFileId: request.signatureFileId, idempotencyKey: request.idempotencyKey });
      return readWorkflowInstanceResponse(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/transfer`, method: 'POST', data: body }));
    },
    async returnTodo(todoId, request) {
      const body = readReturnWorkflowTodoRequest({ targetStepId: request.targetStepId, expectedRevision: request.expectedRevision,
        expectedFormRevision: request.expectedFormRevision, fieldPatch: request.fieldPatch, comment: request.comment,
        signatureFileId: request.signatureFileId, idempotencyKey: request.idempotencyKey });
      return readWorkflowInstanceResponse(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/return`, method: 'POST', data: body }));
    },
    async countersign(todoId, request) {
      const body = readCountersignWorkflowTodoRequest({ directionKey: request.directionKey, assigneeUserIds: request.assigneeUserIds,
        expectedRevision: request.expectedRevision, comment: request.comment, idempotencyKey: request.idempotencyKey });
      return readWorkflowInstanceResponse(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/countersign`, method: 'POST', data: body }));
    },
    async cancelCountersign(todoId, request) {
      const body = readCancelWorkflowTodoCountersignRequest({ expectedRevision: request.expectedRevision, comment: request.comment, idempotencyKey: request.idempotencyKey });
      return readWorkflowInstanceResponse(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/countersign/cancel`, method: 'POST', data: body }));
    },
    async reminderTargets(instanceId) {
      return readWorkflowReminderTargets(await http.request<unknown>({ path: `/api/v1/workflow/instances/${requireTodoId(instanceId)}/reminder-targets` }));
    },
    async remind(instanceId, request) {
      const body = readRequestWorkflowManualReminder({ stepId: requireTodoId(request.stepId), expectedInstanceRevision: request.expectedInstanceRevision,
        expectedStepRevision: request.expectedStepRevision, idempotencyKey: request.idempotencyKey });
      return readWorkflowReminderReceipt(await http.request<unknown>({ path: `/api/v1/workflow/instances/${requireTodoId(instanceId)}/remind`, method: 'POST', data: body }),
        body.stepId, body.expectedInstanceRevision, body.expectedStepRevision);
    },
    async listDelegated(page = 1) {
      requirePage(page);
      return readPagedResultOfWorkflowTodoListItemResponse(await http.request<unknown>({
        path: `/api/v1/workflow/todos/mine/history?resultActionKey=delegate&page=${page}&pageSize=20`
      }));
    },
    async listDelegationCandidates(todoId, page = 1) {
      requirePage(page);
      return readWorkflowRecipientCandidatePageResponse(await http.request<unknown>({
        path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/delegation-candidates?page=${page}&pageSize=20`
      }));
    },
    async delegation(todoId) {
      return readWorkflowTodoDelegation(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/delegation` }));
    },
    async delegate(todoId, request) {
      return readWorkflowInstanceResponse(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/delegate`,
        method: 'POST', data: readDelegateWorkflowTodoRequest(request) }));
    },
    async recallDelegation(todoId, request) {
      return readWorkflowInstanceResponse(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/delegation/recall`,
        method: 'POST', data: readRecallWorkflowTodoDelegationRequest(request) }));
    },
    async downloadAttachment(todoId, fileId) {
      const value = await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/form-attachments/${requireTodoId(fileId)}/content`,
        responseType: 'arraybuffer', headers: { Accept: 'application/octet-stream' } });
      if (!(value instanceof ArrayBuffer)) throw new TypeError('workflow.todo.invalid-attachment-content');
      return value;
    },
    async listClaims() {
      return readWorkflowClaimPoolPage(await http.request<unknown>({ path: '/api/v1/workflow/claim-pools?page=1&pageSize=100' })).items;
    },
    async claim(poolId, request) {
      return readWorkflowInstanceResponse(await http.request<unknown>({ path: `/api/v1/workflow/claim-pools/${requireTodoId(poolId)}/claim`,
        method: 'POST', data: readWorkflowClaimRequest(request) }));
    },
    async release(todoId, request) {
      return readWorkflowInstanceResponse(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/release`,
        method: 'POST', data: readWorkflowReleaseRequest(request) }));
    },
    async uploadSignature(todoId, expectedRevision, filePath) {
      const id = requireTodoId(todoId);
      if (!http.upload) throw new TypeError('workflow.todo.signature-upload-unavailable');
      return readWorkflowSignatureUpload(await http.upload<unknown>({
        path: `/api/v1/workflow/todos/${id}/signature-uploads?expectedRevision=${expectedRevision}`, filePath
      }));
    },
    async listMine() {
      const value = await http.request<unknown>({
        path: '/api/v1/workflow/todos/mine'
      });
      return readPagedResultOfWorkflowTodoListItemResponse(value).items;
    },
    async get(todoId) {
      const normalizedTodoId = requireTodoId(todoId);
      const value = await http.request<unknown>({
        path: `/api/v1/workflow/todos/${normalizedTodoId}/runtime`
      });
      if (!isWorkflowTodoDetail(value)) {
        throw new TypeError('workflow.todo.invalid-detail');
      }

      const cachedSchema = schemaCache.read(value.formVersionId, value.formSchemaHash);
      if (cachedSchema) {
        return { ...value, formSchema: cachedSchema };
      }

      schemaCache.write(value.formVersionId, value.formSchemaHash, value.formSchema);
      return value;
    },
    approve(todoId, request) {
      return act(http, todoId, 'approve', request);
    },
    complete(todoId, request) {
      return act(http, todoId, 'complete', request);
    },
    reject(todoId, request) {
      return act(http, todoId, 'reject', request);
    },
    async vote(todoId, request) {
      const body = readWorkflowVoteRequest(request);
      return readWorkflowInstanceResponse(await http.request<unknown>({ path: `/api/v1/workflow/todos/${requireTodoId(todoId)}/vote`, method: 'POST', data: body }));
    }
  };
}

async function act(
  http: HttpClient,
  todoId: string,
  action: 'approve' | 'reject' | 'complete',
  request: ActWorkflowTodoRequest
): Promise<WorkflowInstanceResponse> {
  const normalizedTodoId = requireTodoId(todoId);
  const body = readActWorkflowTodoRequest(request);
  const value = await http.request<unknown>({
    path: `/api/v1/workflow/todos/${normalizedTodoId}/${action}`,
    method: 'POST',
    data: body
  });
  return readWorkflowInstanceResponse(value);
}

function requireTodoId(value: string): string {
  if (!guidPattern.test(value)) {
    throw new TypeError('workflow.todo.invalid-id');
  }
  return value.toLowerCase();
}

/** 页码保持整数且与服务端int偏移上限一致，候选与委托历史都使用真实20条分页。 */
function requirePage(value: number): void {
  if (!Number.isSafeInteger(value) || value < 1 || value > Math.floor(2147483647 / 20))
    throw new TypeError('workflow.todo.invalid-page');
}
