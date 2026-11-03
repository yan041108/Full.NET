import { isRecord, isInteger, type PagedResult, type PrintingTemplateSummary, isPrintingTemplateSummary } from '@fullnet/client-contracts';
import {
  isPrintingTemplate,
  isPrintingTemplatePreview,
  printingGetFormSchema,
  printingListFormSchemas,
  printingGetTemplate,
  printingGetTemplateVersion,
  printingListTemplateVersions,
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
import { http } from './http';

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
  const value = await printingCreateTemplate(http, { body }, signal);
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
  const value = await printingPublishTemplate(http, { templateId, body: { ...body, changeNote: body.changeNote ?? null } }, signal);
  if (value.templateId.toLowerCase() !== templateId.toLowerCase()) throw new Error('client.invalid_printing_template_version');
  return value;
}

export async function previewPrintingTemplate(
  templateId: string,
  body: PreviewPrintingTemplateRequest = {},
  signal?: AbortSignal
): Promise<PrintingTemplatePreview> {
  const value = await printingPreviewTemplate(http, { templateId, body: { ...body, versionNumber: body.versionNumber ?? null } }, signal);
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
  if (value.items.some(item => item.templateId.toLowerCase() !== templateId.toLowerCase())) throw new Error('client.invalid_printing_template_version');
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
