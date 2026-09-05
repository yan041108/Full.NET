import {
  workflowCreateDefinition,
  workflowGetDefinition,
  workflowGetNodeTypeCatalog,
  workflowListRecipientCandidates,
  workflowPublishDefinition,
  workflowUpdateDefinitionDraft,
  type WorkflowDefinitionDraft,
  type WorkflowDefinitionResponse,
  type WorkflowDefinitionVersionResponse,
  type WorkflowNodeTypeCatalogResponse,
  type WorkflowNodeTypeResponse,
  type WorkflowRecipientCandidatePageResponse
} from '@fullnet/client-contracts';
import { http } from './http';

export interface WorkflowRoleCandidatePageResponse {
  readonly items: ReadonlyArray<{ readonly id: string; readonly code: string; readonly name: string }>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface WorkflowOrganizationUnitCandidatePageResponse {
  readonly items: ReadonlyArray<{ readonly id: string; readonly code: string; readonly name: string }>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

/** 读取单个工作流定义详情。 */
export async function getWorkflowDefinition(
  definitionId: string,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  return workflowGetDefinition(http, { definitionId }, signal);
}

/** 查询工作流节点类型目录，供设计器构建节点面板。 */
export async function getWorkflowNodeTypeCatalog(
  signal?: AbortSignal
): Promise<WorkflowNodeTypeCatalogResponse> {
  return workflowGetNodeTypeCatalog(http, {}, signal);
}

/** 分页读取定义编辑器可选择的活动抄送人。 */
export async function listWorkflowRecipientCandidates(
  page = 1,
  pageSize = 50,
  signal?: AbortSignal
): Promise<WorkflowRecipientCandidatePageResponse> {
  return workflowListRecipientCandidates(http, { page, pageSize }, signal);
}

/** 分页读取定义编辑器可选择的办理人角色。 */
export async function listWorkflowRoleCandidates(
  page = 1,
  pageSize = 50,
  signal?: AbortSignal
): Promise<WorkflowRoleCandidatePageResponse> {
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  return http.request<WorkflowRoleCandidatePageResponse>(
    `/api/v1/workflow/definitions/role-candidates?${query.toString()}`,
    undefined,
    signal
  );
}

/** 分页读取定义编辑器可选择的机构单元。 */
export async function listWorkflowOrganizationUnitCandidates(
  page = 1,
  pageSize = 50,
  signal?: AbortSignal
): Promise<WorkflowOrganizationUnitCandidatePageResponse> {
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  return http.request<WorkflowOrganizationUnitCandidatePageResponse>(
    `/api/v1/workflow/definitions/organization-unit-candidates?${query.toString()}`,
    undefined,
    signal
  );
}

/** 创建工作流定义草稿。 */
export async function createWorkflowDefinition(
  definitionKey: string,
  draft: WorkflowDefinitionDraft,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  return workflowCreateDefinition(http, {
    body: { definitionKey, draft }
  }, signal);
}

/** 更新工作流定义草稿，并携带期望修订号维持乐观并发。 */
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

/** 发布工作流定义，并绑定选中的表单版本。 */
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

/** 导出定义设计器所需的草稿、目录与已发布版本模型，供编辑页和发布流程共享同一契约。 */
export type {
  WorkflowDefinitionDraft,
  WorkflowDefinitionResponse,
  WorkflowDefinitionVersionResponse,
  WorkflowNodeTypeCatalogResponse,
  WorkflowNodeTypeResponse,
  WorkflowRecipientCandidatePageResponse
};
