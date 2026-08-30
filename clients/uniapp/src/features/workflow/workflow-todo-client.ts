import {
  isWorkflowTodoDetail,
  readActWorkflowTodoRequest,
  readWorkflowInstanceResponse,
  readWorkflowListMyTodosResponse,
  type ActWorkflowTodoRequest,
  type WorkflowInstanceResponse,
  type WorkflowTodoDetail,
  type WorkflowTodoResponse
} from '@fullnet/client-contracts';
import type { HttpClient } from '../../api/http';
import type { WorkflowSchemaCache } from './workflow-schema-cache';

export interface WorkflowTodoClient {
  listMine(): Promise<readonly WorkflowTodoResponse[]>;
  get(todoId: string): Promise<WorkflowTodoDetail>;
  approve(todoId: string, request: ActWorkflowTodoRequest): Promise<WorkflowInstanceResponse>;
  reject(todoId: string, request: ActWorkflowTodoRequest): Promise<WorkflowInstanceResponse>;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

/** 创建只消费本人待办 API 的移动端客户端；所有响应在进入状态层前执行运行时守卫。 */
export function createWorkflowTodoClient(
  http: HttpClient,
  schemaCache: WorkflowSchemaCache
): WorkflowTodoClient {
  return {
    async listMine() {
      const value = await http.request<unknown>({
        path: '/api/v1/workflow/todos/mine'
      });
      return readWorkflowListMyTodosResponse(value);
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
    reject(todoId, request) {
      return act(http, todoId, 'reject', request);
    }
  };
}

async function act(
  http: HttpClient,
  todoId: string,
  action: 'approve' | 'reject',
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
