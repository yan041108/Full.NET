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

/** 查询工作流定义列表，供发起流程时选择定义。 */
export async function listWorkflowDefinitions(
  signal?: AbortSignal
): Promise<WorkflowDefinitionResponse[]> {
  return workflowListDefinitions(http, {}, signal);
}

/** 查询某个工作流定义的全部已发布版本。 */
export async function listWorkflowDefinitionVersions(
  definitionId: string,
  signal?: AbortSignal
): Promise<WorkflowDefinitionVersionResponse[]> {
  return workflowListDefinitionVersions(http, { definitionId }, signal);
}

/** 读取流程发起表单版本，并把版本中的 schema 解析为前端可直接消费的结构。 */
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

/** 发起工作流实例，并携带业务标识、初始字段值和幂等键。 */
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

/** 导出发起流程所需的定义、表单、实例与提交模型，供发起页和运行时表单共用同一契约。 */
export type {
  WorkflowDefinitionResponse,
  WorkflowDefinitionVersionResponse,
  WorkflowFormSchema,
  WorkflowInstanceResponse,
  WorkflowSubmission
};
