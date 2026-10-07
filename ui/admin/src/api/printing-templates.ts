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
  printingGetFormSchema,
  printingListFormSchemas,
  type CreatePrintingTemplateRequest,
  type PreviewPrintingTemplateRequest,
  type PrintingFormSchemaDefinition,
  type PrintingTemplate,
  type PrintingTemplatePreview,
  type PublishPrintingTemplateRequest,
  type UpdatePrintingTemplateRequest
} from '@fullnet/client-contracts';
import { http, request } from './http';

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
  if (!Array.isArray(value) || !value.every(isPrintingTemplate)) {
    throw new Error('client.invalid_printing_template_list');
  }
  return value;
}

export async function createPrintingTemplate(
  body: CreatePrintingTemplateRequest,
  signal?: AbortSignal
): Promise<PrintingTemplate> {
  const value = await request<unknown>('/api/v1/printing/templates', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }, signal);
  if (!isPrintingTemplate(value)) {
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
): Promise<void> {
  await request<unknown>(
    `/api/v1/printing/templates/${encodeURIComponent(templateId)}/publish`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
}

export async function previewPrintingTemplate(
  templateId: string,
  body: PreviewPrintingTemplateRequest = {},
  signal?: AbortSignal
): Promise<PrintingTemplatePreview> {
  const value = await request<unknown>(
    `/api/v1/printing/templates/${encodeURIComponent(templateId)}/preview`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isPrintingTemplatePreview(value)) {
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
  return printingListPublishedTemplates(http, {}, signal);
}

/** 通过生成操作发送精确版本，不传入可伪造的租户标识。 */
export async function previewPrintingPublishedTemplate(templateId: string, body: PreviewPrintingTemplateRequest = {},
  signal?: AbortSignal): Promise<PrintingTemplatePreview> {
  const value = await printingPreviewPublishedTemplate(http, {templateId,body:{versionNumber:body.versionNumber ?? null}}, signal);
  if (!isPrintingTemplatePreview(value) || Object.values(value.boundFields).some(field => field !== null && typeof field !== 'string'))
    throw new Error('client.invalid_printing_template_preview');
  return value;
}

/** Host 授权面板只读取真实已发布版本，不能用连续编号猜测历史版本。 */
export async function listPrintingTemplateVersions(templateId: string, signal?: AbortSignal): Promise<PrintingTemplateVersionResponse[]> {
  return printingListTemplateVersions(http, {templateId}, signal);
}

/** 分页读取精确发布版本的租户授权。 */
export async function listPrintingTenantVersionGrants(templateId: string, versionNumber: number,
  page = 1, pageSize = 20, signal?: AbortSignal): Promise<PagedResultOfGuid> {
  return printingListTenantVersionGrants(http, {templateId,versionNumber,page,pageSize}, signal);
}

/** 授予或撤销独立版本，拒绝把 false 响应报告为成功。 */
export async function setPrintingTenantVersionGrant(templateId: string, versionNumber: number,
  tenantId: string, grant: boolean, signal?: AbortSignal): Promise<boolean> {
  const parameters = {templateId,versionNumber,tenantId};
  const value = grant ? await printingGrantTenantVersion(http,parameters,signal) : await printingRevokeTenantVersion(http,parameters,signal);
  if (value !== true) throw new Error('client.invalid_printing_tenant_grant');
  return value;
}
