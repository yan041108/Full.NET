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
import { validWorkflowDefinition, validWorkflowDefinitionVersion, workflowDefinitionTrim } from '../guards/workflow-definition-guards';
import { validWorkflowFormVersion, workflowFormIdValid, workflowFormRevisionValid } from '../guards/workflow-form-guards';

/** 查询工作流定义列表，供发起流程时选择定义。 */
export async function listWorkflowDefinitions(
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse[]> {
  const rows = await workflowListDefinitions(http, {}, signal); if (!rows.every(validWorkflowDefinition) || new Set(rows.map(row => row.id.toLowerCase())).size !== rows.length) throw Error('client.invalid_workflow_definitions'); return rows;
}

/** 查询某个工作流定义的全部已发布版本。 */
export async function listWorkflowDefinitionVersions(
  definitionId: string,
  signal?: AbortSignal
): Promise<WorkflowDefinitionVersionResponse[]> {
  requireId(definitionId); const rows = await workflowListDefinitionVersions(http, { definitionId }, signal); if (!rows.every(row => validWorkflowDefinitionVersion(row, definitionId)) || new Set(rows.map(row => row.id.toLowerCase())).size !== rows.length || new Set(rows.map(row => row.versionNumber)).size !== rows.length) throw Error('client.invalid_workflow_definition_versions'); return rows;
}

/** 读取流程发起表单版本，并把版本中的 schema 解析为前端可直接消费的结构。 */
export async function getWorkflowStartForm(
  formVersionId: string,
  signal?: AbortSignal
): Promise<{
  version: WorkflowFormVersionResponse;
  schema: WorkflowFormSchema;
}> {
  requireId(formVersionId); const version = await workflowGetFormVersion(http, { versionId: formVersionId }, signal); if (!validWorkflowFormVersion(version) || version.id.toLowerCase() !== formVersionId.toLowerCase()) throw Error('client.invalid_workflow_start_form');
  return { version, schema: readWorkflowFormVersionSchema(version) };
}

/** 发起工作流实例，并携带业务标识、初始字段值和幂等键。 */
export async function startWorkflowInstance(
  definitionVersionId: string,
  businessType: string,
  businessId: string,
  initialValues: WorkflowSubmission,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(definitionVersionId); const frozen = JSON.parse(JSON.stringify(initialValues)) as WorkflowSubmission, type = workflowDefinitionTrim(businessType), id = workflowDefinitionTrim(businessId);
  if (!type || type.length > 64 || !id || id.length > 128 || !workflowDefinitionTrim(idempotencyKey) || workflowDefinitionTrim(idempotencyKey).length > 128 || !frozen || typeof frozen !== 'object' || Array.isArray(frozen)) throw Error('client.invalid_workflow_start_request');
  const row = await workflowStartInstance(http, {
    body: {
      definitionVersionId,
      businessType,
      businessId,
      initialValues: frozen,
      idempotencyKey
    }
  }, signal);
  if (!workflowFormIdValid(row.id) || row.definitionVersionId.toLowerCase() !== definitionVersionId.toLowerCase() || !workflowFormIdValid(row.formVersionId) || row.businessType !== type || row.businessId !== id || !workflowFormRevisionValid(row.revision)) throw Error('client.invalid_workflow_started_instance'); return row;
}
function requireId(value: string): void { if (!workflowFormIdValid(value)) throw Error('client.invalid_workflow_definition_id'); }

/** 导出发起流程所需的定义、表单、实例与提交模型，供发起页和运行时表单共用同一契约。 */
export type {
  WorkflowDefinitionResponse,
  WorkflowDefinitionVersionResponse,
  WorkflowFormSchema,
  WorkflowInstanceResponse,
  WorkflowSubmission
};
