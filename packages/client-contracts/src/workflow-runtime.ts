import type { WorkflowFormVersionResponse } from './generated/index.generated.js';
import {
  isWorkflowFormSchema,
  type WorkflowFormSchema
} from './workflow-todos.js';

/** 从已发布版本的冻结 JSON 中读取受支持的静态表单协议。 */
export function readWorkflowFormVersionSchema(
  version: WorkflowFormVersionResponse
): WorkflowFormSchema {
  let value: unknown;
  try {
    value = JSON.parse(version.formSchemaJson);
  } catch {
    throw new Error('client.invalid_workflow_form_schema');
  }

  if (!isWorkflowFormSchema(value)
    || value.adapterVersion !== version.adapterVersion
    || value.schemaVersion !== version.schemaVersion) {
    throw new Error('client.invalid_workflow_form_schema');
  }

  return value;
}
