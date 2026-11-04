import { isRecord, isInteger, type PagedResult, type PrintingTemplateSummary, isPrintingTemplateSummary } from '@fullnet/client-contracts';
import {
  printingListPublishedTemplates,
  printingPreviewPublishedTemplate,
  printingListTemplateVersions,
  printingListTenantVersionGrants,
  printingGrantTenantVersion,
  printingRevokeTenantVersion,
  type PrintingPublishedTemplateResponse,
  type PrintingTemplateVersionResponse,
  type PagedResultOfGuid,
  isPrintingTemplate,
  isPrintingTemplatePreview,
  isGuid,
  printingGetFormSchema,
  printingListFormSchemas,
  printingGetTemplate,
  printingGetTemplateVersion,

  printingListTemplates,
  printingCreateTemplate,
  printingUpdateTemplate,
  printingPublishTemplate,
  printingPreviewTemplate,
  type CreatePrintingTemplateRequest,
  type PreviewPrintingTemplateRequest,
  type PrintingFormSchemaDefinition,
  type PrintingTemplate,
  type PrintingTemplatePreview,
  type PrintingTemplateVersion,
  type PrintingTemplateVersionSummary,
  type PublishPrintingTemplateRequest,
  type UpdatePrintingTemplateRequest
} from '@fullnet/client-contracts';
import { http, request } from './http';
import { validHostPrintingTemplate, validHostPrintingVersion } from '../guards/printing-host-guards';

export async function listPrintingTemplates(
  nameContains?: string,
  signal?: AbortSignal, page = 1, pageSize = 20
): Promise<PagedResult<PrintingTemplateSummary>> {
  const value = await printingListTemplates(http, { page, pageSize, nameContains }, signal);
  if (!isRecord(value) || !Array.isArray(value.items) || !value.items.every(isPrintingTemplateSummary) || !isInteger(value.page) || !isInteger(value.pageSize) || typeof value.total !== 'number') {
    throw new Error('client.invalid_printing_template_list');
  }
  return value as unknown as PagedResult<PrintingTemplateSummary>;
}

export async function createPrintingTemplate(
  body: CreatePrintingTemplateRequest,
  signal?: AbortSignal
): Promise<PrintingTemplate> {
  const payload = { ...body };
  const value = await printingCreateTemplate(http, { body: payload }, signal);
  // 布局由服务端净化，不能要求危险输入原样回显。
  if (!validHostPrintingTemplate(value) || value.version !== 1 || value.latestPublishedVersionNumber !== 0
    || value.templateKey !== payload.templateKey.trim() || value.name !== payload.name.trim() || value.formSchemaKey !== payload.formSchemaKey.trim() || value.isEnabled !== payload.isEnabled) {
    throw new Error('client.invalid_printing_template');
  }
  return value;
}

export async function updatePrintingTemplate(
  templateId: string,
  body: UpdatePrintingTemplateRequest,
  signal?: AbortSignal
): Promise<PrintingTemplate> {
  const value = await printingUpdateTemplate(http, { templateId, body }, signal);
  if (!isPrintingTemplate(value) || value.id.toLowerCase() !== templateId.toLowerCase()) {
    throw new Error('client.invalid_printing_template');
  }
  return value;
}

export async function publishPrintingTemplate(
  templateId: string,
  body: PublishPrintingTemplateRequest,
  signal?: AbortSignal
): Promise<PrintingTemplateVersion> {
  const payload = { ...body };
  if (!isGuid(templateId) || templateId === '00000000-0000-0000-0000-000000000000' || !Number.isSafeInteger(payload.version) || payload.version < 1)
    throw new Error('client.invalid_printing_template_publish_request');
  const value = await printingPublishTemplate(http, { templateId, body: { ...payload, changeNote: payload.changeNote ?? null } }, signal);
  if (value.templateId.toLowerCase() !== templateId.toLowerCase()) throw new Error('client.invalid_printing_template_version');
  return value;
}

export async function previewPrintingTemplate(
  templateId: string,
  body: PreviewPrintingTemplateRequest = {},
  signal?: AbortSignal
): Promise<PrintingTemplatePreview> {
  const payload = { ...body };
  if (!isGuid(templateId) || templateId === '00000000-0000-0000-0000-000000000000'
    || payload.versionNumber != null && (!Number.isSafeInteger(payload.versionNumber) || payload.versionNumber < 1)
    || payload.recordId != null && (!isGuid(payload.recordId) || payload.recordId === '00000000-0000-0000-0000-000000000000')) throw new Error('client.invalid_printing_template_preview_request');
  const value = await printingPreviewTemplate(http, { templateId, body: { ...payload, versionNumber: payload.versionNumber ?? null } }, signal);
  if (!isPrintingTemplatePreview(value)) {
    throw new Error('client.invalid_printing_template_preview');
  }
  return value;
}

/** 编辑只读取选中模板的草稿正文；摘要列表不承担草稿并发版本。 */
export async function getPrintingTemplate(templateId: string, signal?: AbortSignal): Promise<PrintingTemplate> {
  const value = await printingGetTemplate(http, { templateId }, signal);
  if (value.id.toLowerCase() !== templateId.toLowerCase()) throw new Error('client.invalid_printing_template');
  return value;
}
/** 历史版本只读分页，布局正文按需加载。 */
export async function listPrintingTemplateVersions(templateId: string, page = 1, signal?: AbortSignal): Promise<PagedResult<PrintingTemplateVersionSummary>> {
  const value = await printingListTemplateVersions(http, { templateId, page, pageSize: 20 }, signal);
  if (value.page !== page || value.pageSize !== 20 || !Number.isSafeInteger(value.total) || value.total < 0 || value.items.length > 20
    || value.items.some(item => item.templateId.toLowerCase() !== templateId.toLowerCase() || !Number.isSafeInteger(item.versionNumber) || item.versionNumber < 1)
    || new Set(value.items.map(item => item.versionNumber)).size !== value.items.length
    || new Set(value.items.map(item => item.id.toLowerCase())).size !== value.items.length) throw new Error('client.invalid_printing_template_version');
  return value;
}
/** 校验模板和版本身份，避免把其他版本的排版当作本次选择。 */
export async function getPrintingTemplateVersion(templateId: string, versionNumber: number, signal?: AbortSignal): Promise<PrintingTemplateVersion> {
  const value = await printingGetTemplateVersion(http, { templateId, versionNumber }, signal);
  if (value.templateId.toLowerCase() !== templateId.toLowerCase() || value.versionNumber !== versionNumber) throw new Error('client.invalid_printing_template_version');
  return value;
}

/** 列出打印模板可用的表单 schema 目录。 */
export async function listPrintingFormSchemas(
  signal?: AbortSignal
): Promise<PrintingFormSchemaDefinition[]> {
  return printingListFormSchemas(http, {}, signal);
}

/** 读取指定打印表单 schema 定义。 */
export async function getPrintingFormSchema(
  formSchemaKey: string,
  signal?: AbortSignal
): Promise<PrintingFormSchemaDefinition> {
  const value = await printingGetFormSchema(http, { formSchemaKey }, signal);
  // 字段只能来自请求的固定目录，拒绝混入其他 Schema 或可执行占位语法。
  if (value.formSchemaKey !== formSchemaKey || value.fields.length > 64
    || value.fields.some(field => !/^[a-zA-Z0-9_]{1,64}$/.test(field.fieldKey))
    || new Set(value.fields.map(field => field.fieldKey)).size !== value.fields.length) throw new Error('client.invalid_printing_form_schema');
  return value;
}

/** 租户只消费获授的不可变版本目录，不读取 Host 草稿与布局。 */
export async function listPrintingPublishedTemplates(signal?: AbortSignal): Promise<PrintingPublishedTemplateResponse[]> {
  const rows = await printingListPublishedTemplates(http, {}, signal);
  if (rows.some(row => !isGuid(row.templateId) || row.templateId === '00000000-0000-0000-0000-000000000000' || !Number.isSafeInteger(row.versionNumber) || row.versionNumber < 1 || !row.formSchemaKey.trim() || !row.templateKey.trim())
    || new Set(rows.map(row => row.templateId.toLowerCase() + ':' + row.versionNumber)).size !== rows.length) throw new Error('client.invalid_printing_published_template_list');
  return rows;
}

/** 通过生成操作发送精确版本，不传入可伪造的租户标识。 */
export async function previewPrintingPublishedTemplate(templateId: string, body: PreviewPrintingTemplateRequest = {},
  signal?: AbortSignal): Promise<PrintingTemplatePreview> {
  const payload = { versionNumber: body.versionNumber ?? null, recordId: body.recordId ?? null };
  if (!isGuid(templateId) || templateId === '00000000-0000-0000-0000-000000000000'
    || payload.versionNumber !== null && (!Number.isSafeInteger(payload.versionNumber) || payload.versionNumber < 1)
    || payload.recordId !== null && (!isGuid(payload.recordId) || payload.recordId === '00000000-0000-0000-0000-000000000000')) throw new Error('client.invalid_printing_template_preview_request');
  const value = await printingPreviewPublishedTemplate(http, { templateId, body: payload }, signal);
  if (!isPrintingTemplatePreview(value) || Object.values(value.boundFields).some(field => field !== null && typeof field !== 'string')
    || value.templateId.toLowerCase() !== templateId.toLowerCase() || !Number.isSafeInteger(value.versionNumber) || value.versionNumber < 1 || payload.versionNumber !== null && value.versionNumber !== payload.versionNumber)
    throw new Error('client.invalid_printing_template_preview');
  return value;
}

/** 分页读取精确发布版本的租户授权。 */
export async function listPrintingTenantVersionGrants(templateId: string, versionNumber: number,
  page = 1, pageSize = 20, signal?: AbortSignal): Promise<PagedResultOfGuid> {
  const result = await printingListTenantVersionGrants(http, {templateId,versionNumber,page,pageSize}, signal);
  if (result.page !== page || result.pageSize !== pageSize || result.total < 0 || result.items.length > pageSize
    || new Set(result.items.map(id => id.toLowerCase())).size !== result.items.length || result.items.includes('00000000-0000-0000-0000-000000000000')) throw new Error('client.invalid_printing_tenant_grants_page');
  return result;
}

/** 授予或撤销独立版本，拒绝把 false 响应报告为成功。 */
export async function setPrintingTenantVersionGrant(templateId: string, versionNumber: number,
  tenantId: string, grant: boolean, signal?: AbortSignal): Promise<boolean> {
  const parameters = {templateId,versionNumber,tenantId};
  const value = grant ? await printingGrantTenantVersion(http,parameters,signal) : await printingRevokeTenantVersion(http,parameters,signal);
  if (value !== true) throw new Error('client.invalid_printing_tenant_grant');
  return value;
}
