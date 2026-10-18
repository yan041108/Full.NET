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
  type CreatePrintingTemplateRequest,
  type PreviewPrintingTemplateRequest,
  type PrintingFormSchemaDefinition,
  type PrintingTemplate,
  type PrintingTemplatePreview,
  type PrintingTemplateVersion,
  type PublishPrintingTemplateRequest,
  type UpdatePrintingTemplateRequest
} from '@fullnet/client-contracts';
import { http, request } from './http';
import { validHostPrintingTemplate, validHostPrintingVersion } from '../guards/printing-host-guards';

export async function listPrintingTemplates(
  nameContains?: string,
  signal?: AbortSignal
): Promise<PrintingTemplate[]> {
  const params = new URLSearchParams();
  if (nameContains) {
    params.set('nameContains', nameContains);
  }
  const suffix = params.size > 0 ? `?${params.toString()}` : '';
  const value = await request<unknown>(`/api/v1/printing/templates${suffix}`, { method: 'GET' }, signal);
  if (!Array.isArray(value) || !value.every(validHostPrintingTemplate)
    || new Set(value.map(row => row.id.toLowerCase())).size !== value.length) {
    throw new Error('client.invalid_printing_template_list');
  }
  return value;
}

export async function createPrintingTemplate(
  body: CreatePrintingTemplateRequest,
  signal?: AbortSignal
): Promise<PrintingTemplate> {
  const payload = { ...body };
  const value = await request<unknown>('/api/v1/printing/templates', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload) }, signal);
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
  const value = await request<unknown>(
    `/api/v1/printing/templates/${encodeURIComponent(templateId)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isPrintingTemplate(value)) {
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
  const value = await request<unknown>(
    `/api/v1/printing/templates/${encodeURIComponent(templateId)}/publish`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload) },
    signal
  );
  if (!validHostPrintingVersion(value) || value.templateId.toLowerCase() !== templateId.toLowerCase()
    || value.changeNote !== (payload.changeNote?.trim() || null)) throw new Error('client.invalid_printing_template_version');
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
  const value = await request<unknown>(
    `/api/v1/printing/templates/${encodeURIComponent(templateId)}/preview`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload) },
    signal
  );
  if (!isPrintingTemplatePreview(value) || value.templateId.toLowerCase() !== templateId.toLowerCase()
    || !Number.isSafeInteger(value.versionNumber) || value.versionNumber < 1
    || Object.values(value.boundFields).some(field => field !== null && typeof field !== 'string')
    || payload.versionNumber != null && value.versionNumber !== payload.versionNumber) {
    throw new Error('client.invalid_printing_template_preview');
  }
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
  return printingGetFormSchema(http, { formSchemaKey }, signal);
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

/** Host 授权面板只读取真实已发布版本，不能用连续编号猜测历史版本。 */
export async function listPrintingTemplateVersions(templateId: string, signal?: AbortSignal): Promise<PrintingTemplateVersionResponse[]> {
  const rows = await printingListTemplateVersions(http, {templateId}, signal);
  if (rows.some(row => row.templateId.toLowerCase() !== templateId.toLowerCase() || row.versionNumber < 1)
    || new Set(rows.map(row => row.versionNumber)).size !== rows.length || new Set(rows.map(row => row.id.toLowerCase())).size !== rows.length) throw new Error('client.invalid_printing_template_version');
  return rows;
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
