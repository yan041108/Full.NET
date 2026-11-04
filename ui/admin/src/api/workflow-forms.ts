import {
  workflowCreateForm,
  workflowGetDesignerAsset,
  workflowSaveDesignerAsset,
  type WorkflowDesignerAssetResponse,
  workflowDeleteFormVersion,
  workflowGetForm,
  workflowGetFormComponentCatalog,
  workflowListForms,
  workflowListFormVersions,
  workflowPublishForm,
  workflowSetFormStatus,
  workflowUpdateFormDraft,
  isWorkflowFormSchema,
  type PublishWorkflowFormRequest,
  type WorkflowFormComponentCatalogResponse,
  type WorkflowFormResponse as GeneratedWorkflowFormResponse,
  type WorkflowFormSchema,
  type WorkflowFormVersionResponse
} from '@fullnet/client-contracts';
import { http } from './http';
import { normalizeWorkflowFormKey, sameWorkflowFormDraft, validWorkflowForm, validWorkflowFormCatalog, validWorkflowFormVersion, workflowFormIdValid, workflowFormKeyValid, workflowFormWriteRevisionValid } from '../guards/workflow-form-guards';

/** 将生成契约中的 draft 收紧为已通过运行时校验的表单草稿结构。 */
export type WorkflowFormResponse = Omit<GeneratedWorkflowFormResponse, 'draft'> & {
  readonly draft: WorkflowFormSchema;
};

/** 查询全部工作流表单，并对每条草稿执行一次前端结构校验。 */
export async function listWorkflowForms(signal?: AbortSignal): Promise<WorkflowFormResponse[]> {
  const rows = (await workflowListForms(http, {}, signal)).map(readSafeForm);
  if (new Set(rows.map(row => row.id.toLowerCase())).size !== rows.length) throw new Error('client.invalid_workflow_form_list');
  return rows;
}

/** 读取单个工作流表单详情。 */
export async function getWorkflowForm(formId: string, signal?: AbortSignal): Promise<WorkflowFormResponse> {
  requireId(formId); const row = readSafeForm(await workflowGetForm(http, { formId }, signal)); requireSameId(row.id, formId); return row;
}

/** 获取表单设计器组件目录，供可视化编辑器构建字段面板。 */
export async function getWorkflowFormComponentCatalog(
  signal?: AbortSignal
): Promise<WorkflowFormComponentCatalogResponse> {
  const catalog = await workflowGetFormComponentCatalog(http, {}, signal);
  if (!validWorkflowFormCatalog(catalog)) throw new Error('client.invalid_workflow_form_catalog'); return catalog;
}

/** 创建工作流表单，并把前端草稿投影为生成契约期望的纯数据结构。 */
export async function createWorkflowForm(
  formKey: string,
  draft: WorkflowFormSchema,
  signal?: AbortSignal
): Promise<WorkflowFormResponse> {
  const body = { formKey: normalizeWorkflowFormKey(formKey), draft: toGeneratedDraft(draft), designerJson: { widgetList: [], formConfig: { jsonVersion: 3 } } }, key = normalizeWorkflowFormKey(formKey);
  if (!workflowFormKeyValid(key)) throw new Error('client.invalid_workflow_form_key');
  const row = readSafeForm(await workflowCreateForm(http, { body }, signal));
  if (row.formKey !== key || row.draftRevision !== 1 || row.version !== 1 || row.statusKey !== 'active' || row.latestPublishedVersionId !== null || !sameWorkflowFormDraft(row.draft, body.draft)) throw new Error('client.invalid_workflow_form_create'); return row;
}

/** 基于期望修订号更新表单草稿，保持与服务端乐观并发语义一致。 */
export async function updateWorkflowFormDraft(
  formId: string,
  expectedRevision: number,
  draft: WorkflowFormSchema,
  signal?: AbortSignal
): Promise<WorkflowFormResponse> {
  requireId(formId); requireRevision(expectedRevision); const frozenDraft = toGeneratedDraft(draft);
  const row = readSafeForm(await workflowUpdateFormDraft(http, {
    formId,
    body: { expectedRevision, draft: frozenDraft }
  }, signal));
  requireSameId(row.id, formId);
  if (row.draftRevision !== expectedRevision + 1 || !sameWorkflowFormDraft(row.draft, frozenDraft)) throw new Error('client.invalid_workflow_form_save'); return row;
}

/** 发布表单草稿并生成可运行版本。 */
export async function publishWorkflowForm(
  formId: string,
  body: PublishWorkflowFormRequest,
  signal?: AbortSignal
): Promise<WorkflowFormVersionResponse> {
  requireId(formId); const frozen = { expectedRevision: body.expectedRevision }; requireRevision(frozen.expectedRevision);
  const row = await workflowPublishForm(http, { formId, body: frozen }, signal);
  if (!validWorkflowFormVersion(row, formId)) throw new Error('client.invalid_workflow_form_version'); return row;
}

/** 变更工作流表单启停或归档状态。 */
export async function setWorkflowFormStatus(
  formId: string,
  statusKey: 'active' | 'disabled' | 'archived',
  expectedVersion: number,
  signal?: AbortSignal
): Promise<WorkflowFormResponse> {
  requireId(formId); requireRevision(expectedVersion);
  if (!['active', 'disabled', 'archived'].includes(statusKey)) throw new Error('client.invalid_workflow_form_status');
  const row = readSafeForm(await workflowSetFormStatus(http, {
    formId,
    body: { statusKey, expectedVersion }
  }, signal));
  requireSameId(row.id, formId);
  // 保存草稿与状态管理分别使用 DraftRevision、Version；并发保存可以使响应草稿更新。
  if (row.version <= expectedVersion || row.statusKey !== statusKey) throw new Error('client.invalid_workflow_form_status'); return row;
}

/** 列出工作流表单的全部已发布版本。 */
export async function listWorkflowFormVersions(
  formId: string,
  signal?: AbortSignal
): Promise<WorkflowFormVersionResponse[]> {
  requireId(formId); const rows = await workflowListFormVersions(http, { formId }, signal);
  if (!rows.every(row => validWorkflowFormVersion(row, formId)) || new Set(rows.map(row => row.id.toLowerCase())).size !== rows.length || new Set(rows.map(row => row.versionNumber)).size !== rows.length) throw new Error('client.invalid_workflow_form_versions'); return rows;
}

/** 删除未被运行实例引用的表单版本。 */
export async function deleteWorkflowFormVersion(
  versionId: string,
  signal?: AbortSignal
): Promise<void> {
  requireId(versionId); await workflowDeleteFormVersion(http, { versionId }, signal);
}

/** 导出表单发布、组件目录与版本模型，供设计器、发布确认弹窗与版本面板共享同一契约。 */
export type {
  PublishWorkflowFormRequest,
  WorkflowFormComponentCatalogResponse,
  WorkflowFormVersionResponse
};

/** 校验服务端返回的草稿结构，失败时立即关闭而不是把脏数据传给设计器。 */
function readSafeForm(value: GeneratedWorkflowFormResponse): WorkflowFormResponse {
  if (!validWorkflowForm(value)) {
    throw new Error('client.invalid_workflow_form_draft');
  }
  return { ...value, draft: value.draft };
}

/** 去掉多余原型与运行时附加字段，生成适合序列化提交的稳定草稿载荷。 */
function toGeneratedDraft(draft: WorkflowFormSchema) {
  if (!isWorkflowFormSchema(draft)) throw new Error('client.invalid_workflow_form_draft');
  const payload = {
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
  }; return JSON.parse(JSON.stringify(payload)) as typeof payload;
}

/** 读取独立完整设计稿；旧业务 Schema 不作为设计稿的静默替代。 */
export async function getWorkflowDesignerAsset(formId: string, signal?: AbortSignal): Promise<WorkflowDesignerAsset | undefined> {
  try { return readDesignerAsset(await workflowGetDesignerAsset(http, { formId }, signal)); }
  catch (error) { if (typeof error === 'object' && error !== null && 'status' in error && error.status === 404) return undefined; throw error; }
}

/** 原样提交完整 JSON，由服务端计算摘要并原子推进草稿修订。 */
export async function saveWorkflowDesignerAsset(formId: string, expectedRevision: number, designerJson: Record<string, unknown>): Promise<WorkflowDesignerAsset> {
  return readDesignerAsset(await workflowSaveDesignerAsset(http, { formId, body: { expectedRevision, sourceCommit: 'c67479e496bab56a93a3dff168a4f529d8293c67', formatVersion: 3, designerJson } }));
}

type WorkflowDesignerAsset = Omit<WorkflowDesignerAssetResponse, 'designerJson'> & { readonly designerJson: Record<string, unknown> };

/** 原版 JSON 保持完整，但根必须为对象，才能进入设计器对象协议。 */
function readDesignerAsset(value: WorkflowDesignerAssetResponse): WorkflowDesignerAsset {
  const json = value.designerJson;
  if (!isDesignObject(json) || !Array.isArray(json.widgetList) || !isDesignObject(json.formConfig)) throw new Error('client.invalid_workflow_designer_asset');
  // 只校验必要根结构，完整组件与扩展配置原样返回。
  return { ...value, designerJson: json };
}
function isDesignObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}
function requireId(value: string): void { if (!workflowFormIdValid(value)) throw new Error('client.invalid_workflow_form_id'); }
function requireSameId(actual: string, expected: string): void { if (actual.toLowerCase() !== expected.toLowerCase()) throw new Error('client.invalid_workflow_form_id'); }
function requireRevision(value: number): void { if (!workflowFormWriteRevisionValid(value)) throw new Error('client.invalid_workflow_form_revision'); }
