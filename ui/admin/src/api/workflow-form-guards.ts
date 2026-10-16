import { isGuid, isWorkflowFormSchema } from '@fullnet/client-contracts';
import type { WorkflowFormComponentCatalogResponse, WorkflowFormResponse, WorkflowFormVersionResponse } from './workflow-forms';
export const workflowFormIdValid = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
export const workflowFormRevisionValid = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) > 0;
export const workflowFormWriteRevisionValid = (value: unknown): value is number => workflowFormRevisionValid(value) && value < Number.MAX_SAFE_INTEGER;
export const normalizeWorkflowFormKey = (value: string): string => value.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '').toLowerCase();
/** 稳定键与服务端 ASCII 规则一致，不能借用 JavaScript trim 接受 BOM。 */
export function workflowFormKeyValid(value: unknown): value is string { return typeof value === 'string' && /^[a-z0-9_.-]{3,128}$/u.test(value); }
/** 对象成员顺序不影响语义，数组顺序仍属于表单内容；不执行任意布局或脚本。 */
export function workflowFormValueKey(value: unknown): string {
  if (Array.isArray(value)) return '[' + value.map(workflowFormValueKey).join(',') + ']';
  if (value !== null && typeof value === 'object') return '{' + Object.keys(value).sort().map(key => JSON.stringify(key) + ':' + workflowFormValueKey((value as Record<string, unknown>)[key])).join(',') + '}';
  return JSON.stringify(value) ?? 'undefined';
}
export const sameWorkflowFormDraft = (left: unknown, right: unknown): boolean => workflowFormValueKey(left) === workflowFormValueKey(right);
export function validWorkflowForm(value: unknown): value is WorkflowFormResponse {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false; const row = value as Record<string, unknown>;
  return workflowFormIdValid(row.id) && workflowFormKeyValid(row.formKey) && isWorkflowFormSchema(row.draft) && workflowFormRevisionValid(row.draftRevision) && workflowFormRevisionValid(row.version) && ['active', 'disabled', 'archived'].includes(String(row.statusKey)) && (row.latestPublishedVersionId === null || workflowFormIdValid(row.latestPublishedVersionId)) && typeof row.createdAtUtc === 'string' && (row.updatedAtUtc === null || typeof row.updatedAtUtc === 'string');
}
export function validWorkflowFormVersion(value: unknown, formId?: string): value is WorkflowFormVersionResponse {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false; const row = value as Record<string, unknown>;
  if (!workflowFormIdValid(row.id) || !workflowFormIdValid(row.formDefinitionId) || formId && row.formDefinitionId.toLowerCase() !== formId.toLowerCase() || !workflowFormRevisionValid(row.versionNumber) || row.versionNumber > 2147483647 || row.schemaVersion !== 1 || row.adapterVersion !== 1 || !workflowFormRevisionValid(row.componentCatalogVersion) || !workflowFormIdValid(row.publishedById) || typeof row.publishedAtUtc !== 'string' || typeof row.contentHash !== 'string' || !/^[a-f0-9]{64}$/iu.test(row.contentHash) || typeof row.formSchemaJson !== 'string' || typeof row.webRenderSchemaJson !== 'string') return false;
  try { const schema: unknown = JSON.parse(row.formSchemaJson), render: unknown = JSON.parse(row.webRenderSchemaJson); return isWorkflowFormSchema(schema) && isWorkflowFormSchema(render) && sameWorkflowFormDraft(schema, render); } catch { return false; }
}
export function validWorkflowFormCatalog(value: unknown): value is WorkflowFormComponentCatalogResponse {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false; const row = value as Record<string, unknown>;
  if (!workflowFormRevisionValid(row.catalogVersion) || row.schemaVersion !== 1 || row.adapterVersion !== 1 || !Array.isArray(row.components)) return false;
  const keys = new Set<string>(); return row.components.every(value => { if (!value || typeof value !== 'object') return false; const item = value as Record<string, unknown>;
    if (typeof item.fieldTypeKey !== 'string' || !item.fieldTypeKey || keys.has(item.fieldTypeKey) || typeof item.designable !== 'boolean' || typeof item.publishable !== 'boolean' || typeof item.executable !== 'boolean' || !Array.isArray(item.constraintKeys) || !item.constraintKeys.every(key => typeof key === 'string') || new Set(item.constraintKeys).size !== item.constraintKeys.length) return false; keys.add(item.fieldTypeKey); return true; });
}
