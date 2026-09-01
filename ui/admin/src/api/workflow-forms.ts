import {
  workflowCreateForm,
  workflowGetForm,
  workflowGetFormComponentCatalog,
  workflowListForms,
  workflowPublishForm,
  workflowUpdateFormDraft,
  isWorkflowFormSchema,
  type PublishWorkflowFormRequest,
  type WorkflowFormComponentCatalogResponse,
  type WorkflowFormResponse as GeneratedWorkflowFormResponse,
  type WorkflowFormSchema,
  type WorkflowFormVersionResponse
} from '@fullnet/client-contracts';
import { http } from './http';

/** 将生成契约中的 draft 收紧为已通过运行时校验的表单草稿结构。 */
export type WorkflowFormResponse = Omit<GeneratedWorkflowFormResponse, 'draft'> & {
  readonly draft: WorkflowFormSchema;
};

/** 查询全部工作流表单，并对每条草稿执行一次前端结构校验。 */
export async function listWorkflowForms(signal?: AbortSignal): Promise<WorkflowFormResponse[]> {
  return (await workflowListForms(http, {}, signal)).map(readSafeForm);
}

/** 读取单个工作流表单详情。 */
export async function getWorkflowForm(formId: string, signal?: AbortSignal): Promise<WorkflowFormResponse> {
  return readSafeForm(await workflowGetForm(http, { formId }, signal));
}

/** 获取表单设计器组件目录，供可视化编辑器构建字段面板。 */
export async function getWorkflowFormComponentCatalog(
  signal?: AbortSignal
): Promise<WorkflowFormComponentCatalogResponse> {
  return workflowGetFormComponentCatalog(http, {}, signal);
}

/** 创建工作流表单，并把前端草稿投影为生成契约期望的纯数据结构。 */
export async function createWorkflowForm(
  formKey: string,
  draft: WorkflowFormSchema,
  signal?: AbortSignal
): Promise<WorkflowFormResponse> {
  return readSafeForm(await workflowCreateForm(http, {
    body: { formKey, draft: toGeneratedDraft(draft) }
  }, signal));
}

/** 基于期望修订号更新表单草稿，保持与服务端乐观并发语义一致。 */
export async function updateWorkflowFormDraft(
  formId: string,
  expectedRevision: number,
  draft: WorkflowFormSchema,
  signal?: AbortSignal
): Promise<WorkflowFormResponse> {
  return readSafeForm(await workflowUpdateFormDraft(http, {
    formId,
    body: { expectedRevision, draft: toGeneratedDraft(draft) }
  }, signal));
}

/** 发布表单草稿并生成可运行版本。 */
export async function publishWorkflowForm(
  formId: string,
  body: PublishWorkflowFormRequest,
  signal?: AbortSignal
): Promise<WorkflowFormVersionResponse> {
  return workflowPublishForm(http, { formId, body }, signal);
}

/** 导出表单发布、组件目录与版本模型，供设计器、发布确认弹窗与版本面板共享同一契约。 */
export type {
  PublishWorkflowFormRequest,
  WorkflowFormComponentCatalogResponse,
  WorkflowFormVersionResponse
};

/** 校验服务端返回的草稿结构，失败时立即关闭而不是把脏数据传给设计器。 */
function readSafeForm(value: GeneratedWorkflowFormResponse): WorkflowFormResponse {
  if (!isWorkflowFormSchema(value.draft)) {
    throw new Error('client.invalid_workflow_form_draft');
  }
  return { ...value, draft: value.draft };
}

/** 去掉多余原型与运行时附加字段，生成适合序列化提交的稳定草稿载荷。 */
function toGeneratedDraft(draft: WorkflowFormSchema) {
  return {
    schemaVersion: draft.schemaVersion,
    adapterVersion: draft.adapterVersion,
    sections: draft.sections.map(section => ({
      sectionKey: section.sectionKey,
      fields: section.fields.map(field => ({
        fieldKey: field.fieldKey,
        fieldTypeKey: field.fieldTypeKey,
        required: field.required,
        constraints: { ...field.constraints }
      }))
    }))
  };
}
