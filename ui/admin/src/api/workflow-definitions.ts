import {
  workflowCreateDefinition,
  workflowDeleteDefinitionVersion,
  workflowGetDefinition,
  workflowGetNodeTypeCatalog,
  workflowListRecipientCandidates,
  workflowPreviewAssignees,
  workflowPublishDefinition,
  workflowSetDefinitionStatus,
  workflowUpdateDefinitionDraft,
  type WorkflowDefinitionDraft,
  type WorkflowDefinitionResponse,
  type WorkflowDefinitionVersionResponse,
  type WorkflowNodeTypeCatalogResponse,
  type WorkflowNodeTypeResponse,
  type WorkflowRecipientCandidatePageResponse,
  type WorkflowRecipientCandidateResponse
} from '@fullnet/client-contracts';
import { http } from './http';
import { normalizeWorkflowFormKey, workflowFormIdValid, workflowFormKeyValid, workflowFormWriteRevisionValid } from '../guards/workflow-form-guards';
import { normalizeWorkflowTitleTemplate, validWorkflowDefinition, validWorkflowDefinitionDraft, validWorkflowDefinitionVersion, validWorkflowNodeCatalog, workflowDefinitionDraftKey } from '../guards/workflow-definition-guards';

export type WorkflowCandidate = {
  readonly id: string;
  readonly code: string;
  readonly name: string;
}

export type WorkflowCandidatePage = {
  readonly items: WorkflowCandidate[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

/** 对候选分页响应执行运行时校验，避免把未知 JSON 直接提升为业务契约。 */
function readWorkflowCandidatePage(value: unknown): WorkflowCandidatePage {
  if (!value || typeof value !== 'object') {
    throw new Error('client.invalid_workflow_candidate_page');
  }

  const page = value as Record<string, unknown>;
  const validItems = Array.isArray(page.items) && page.items.every(item => {
    if (!item || typeof item !== 'object') return false;
    const candidate = item as Record<string, unknown>;
    return typeof candidate.id === 'string' &&
      typeof candidate.code === 'string' &&
      typeof candidate.name === 'string';
  });
  if (!validItems ||
      !Number.isInteger(page.page) ||
      !Number.isInteger(page.pageSize) ||
      !Number.isInteger(page.total)) {
    throw new Error('client.invalid_workflow_candidate_page');
  }

  return value as WorkflowCandidatePage;
}

/** 读取单个工作流定义详情。 */
export async function getWorkflowDefinition(
  definitionId: string,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  requireId(definitionId); const row = await workflowGetDefinition(http, { definitionId }, signal); requireDefinition(row, definitionId); return row;
}

/** 查询工作流节点类型目录，供设计器构建节点面板。 */
export async function getWorkflowNodeTypeCatalog(
  signal?: AbortSignal
): Promise<WorkflowNodeTypeCatalogResponse> {
  const row = await workflowGetNodeTypeCatalog(http, {}, signal); if (!validWorkflowNodeCatalog(row)) throw Error('client.invalid_workflow_node_type_catalog_response'); return row;
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
): Promise<WorkflowCandidatePage> {
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  const value = await http.request<unknown>(
    `/api/v1/workflow/definitions/role-candidates?${query.toString()}`,
    undefined,
    signal
  );
  return readWorkflowCandidatePage(value);
}

/** 分页读取定义编辑器可选择的机构单元。 */
export async function listWorkflowOrganizationUnitCandidates(
  page = 1,
  pageSize = 50,
  signal?: AbortSignal
): Promise<WorkflowCandidatePage> {
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  const value = await http.request<unknown>(
    `/api/v1/workflow/definitions/organization-unit-candidates?${query.toString()}`,
    undefined,
    signal
  );
  return readWorkflowCandidatePage(value);
}

/** 预览办理人解析结果，供设计器范围校验。 */
export async function previewWorkflowAssignees(
  assigneePolicy: Record<string, unknown>,
  initiatorUserId?: string,
  signal?: AbortSignal
): Promise<ReadonlyArray<WorkflowRecipientCandidateResponse>> {
  const response = await workflowPreviewAssignees(http, {
    body: {
      assigneePolicy,
      initiatorUserId: initiatorUserId ?? null
    }
  }, signal);
  return response.users;
}

/** 创建工作流定义草稿。 */
export async function createWorkflowDefinition(
  definitionKey: string,
  draft: WorkflowDefinitionDraft,
  businessTitleTemplate?: string | null,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  const key = normalizeWorkflowFormKey(definitionKey), frozen = freezeDraft(draft), title = normalizeWorkflowTitleTemplate(businessTitleTemplate);
  if (!workflowFormKeyValid(key)) throw Error('client.invalid_workflow_definition_key');
  const row = await workflowCreateDefinition(http, { body: { definitionKey, draft: frozen, businessTitleTemplate: businessTitleTemplate ?? null } }, signal); requireDefinition(row);
  if (row.definitionKey !== key || row.draftRevision !== 1 || row.version !== 1 || row.latestPublishedVersionId !== null || row.statusKey !== 'active' || row.businessTitleTemplate !== title || workflowDefinitionDraftKey(row.draft) !== workflowDefinitionDraftKey(frozen)) throw Error('client.invalid_workflow_definition_create'); return row;
}

/** 更新工作流定义草稿，并携带期望修订号维持乐观并发。 */
export async function updateWorkflowDefinitionDraft(
  definitionId: string,
  expectedRevision: number,
  draft: WorkflowDefinitionDraft,
  businessTitleTemplate?: string | null,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  requireId(definitionId); requireRevision(expectedRevision); const frozen = freezeDraft(draft), title = normalizeWorkflowTitleTemplate(businessTitleTemplate);
  const row = await workflowUpdateDefinitionDraft(http, {
    definitionId,
    body: { expectedRevision, draft: frozen, businessTitleTemplate: businessTitleTemplate ?? null }
  }, signal);
  requireDefinition(row, definitionId); if (row.draftRevision !== expectedRevision + 1 || row.businessTitleTemplate !== title || workflowDefinitionDraftKey(row.draft) !== workflowDefinitionDraftKey(frozen)) throw Error('client.invalid_workflow_definition_save'); return row;
}

/** 发布工作流定义，并绑定选中的表单版本。 */
export async function publishWorkflowDefinition(
  definitionId: string,
  expectedRevision: number,
  formVersionId: string,
  signal?: AbortSignal
): Promise<WorkflowDefinitionVersionResponse> {
  requireId(definitionId); requireId(formVersionId); requireRevision(expectedRevision); const row = await workflowPublishDefinition(http, {
    definitionId,
    body: { expectedRevision, formVersionId }
  }, signal);
  if (!validWorkflowDefinitionVersion(row, definitionId) || row.formVersionId.toLowerCase() !== formVersionId.toLowerCase()) throw Error('client.invalid_workflow_definition_publication'); return row;
}

/** 变更工作流定义启停或归档状态。 */
export async function setWorkflowDefinitionStatus(
  definitionId: string,
  statusKey: 'active' | 'disabled' | 'archived',
  expectedVersion: number,
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse> {
  requireId(definitionId); requireRevision(expectedVersion); const row = await workflowSetDefinitionStatus(http, {
    definitionId,
    body: { statusKey, expectedVersion }
  }, signal);
  requireDefinition(row, definitionId); if (row.statusKey !== statusKey || row.version <= expectedVersion) throw Error('client.invalid_workflow_definition_status'); return row;
}

/** 删除未被运行实例引用的定义版本。 */
export async function deleteWorkflowDefinitionVersion(
  versionId: string,
  signal?: AbortSignal
): Promise<void> {
  requireId(versionId); await workflowDeleteDefinitionVersion(http, { versionId }, signal);
}
function requireId(value: string): void { if (!workflowFormIdValid(value)) throw Error('client.invalid_workflow_definition_id'); }
function requireRevision(value: number): void { if (!workflowFormWriteRevisionValid(value)) throw Error('client.invalid_workflow_definition_revision'); }
function requireDefinition(value: unknown, id?: string): asserts value is WorkflowDefinitionResponse { if (!validWorkflowDefinition(value) || id && value.id.toLowerCase() !== id.toLowerCase()) throw Error('client.invalid_workflow_definition'); }
function freezeDraft(value: WorkflowDefinitionDraft): WorkflowDefinitionDraft { if (!validWorkflowDefinitionDraft(value)) throw Error('client.invalid_workflow_definition_draft'); return JSON.parse(JSON.stringify(value)) as WorkflowDefinitionDraft; }

/** 导出定义设计器所需的草稿、目录与已发布版本模型，供编辑页和发布流程共享同一契约。 */
export type {
  WorkflowDefinitionDraft,
  WorkflowDefinitionResponse,
  WorkflowDefinitionVersionResponse,
  WorkflowNodeTypeCatalogResponse,
  WorkflowNodeTypeResponse,
  WorkflowRecipientCandidatePageResponse
};
