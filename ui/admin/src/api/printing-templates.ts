import {
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
