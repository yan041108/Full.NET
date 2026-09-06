export interface PrintingFormFieldDefinition {
  fieldKey: string;
  displayName: string;
}

export interface PrintingFormSchemaDefinition {
  formSchemaKey: string;
  displayName: string;
  description: string;
  fields: PrintingFormFieldDefinition[];
}

export interface PrintingTemplate {
  id: string;
  templateKey: string;
  name: string;
  formSchemaKey: string;
  layoutHtml: string;
  latestPublishedVersionNumber: number;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface CreatePrintingTemplateRequest {
  templateKey: string;
  name: string;
  formSchemaKey: string;
  layoutHtml: string;
  isEnabled: boolean;
}

export interface UpdatePrintingTemplateRequest {
  name: string;
  layoutHtml: string;
  isEnabled: boolean;
  version: number;
}

export interface PublishPrintingTemplateRequest {
  changeNote?: string | null;
  version: number;
}

export interface PrintingTemplateVersion {
  id: string;
  templateId: string;
  versionNumber: number;
  layoutHtml: string;
  changeNote: string | null;
  publishedByUserId: string;
  publishedAtUtc: string;
}

export interface PreviewPrintingTemplateRequest {
  versionNumber?: number | null;
}

export interface PrintingTemplatePreview {
  templateId: string;
  templateKey: string;
  templateName: string;
  versionNumber: number;
  formSchemaKey: string;
  html: string;
  boundFields: Record<string, string | null>;
  generatedAtUtc: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isPrintingTemplate(value: unknown): value is PrintingTemplate {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.templateKey === 'string'
    && typeof value.name === 'string'
    && typeof value.formSchemaKey === 'string'
    && typeof value.layoutHtml === 'string'
    && typeof value.latestPublishedVersionNumber === 'number'
    && typeof value.isEnabled === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

export function isPrintingTemplatePreview(value: unknown): value is PrintingTemplatePreview {
  return isRecord(value)
    && isGuid(value.templateId)
    && typeof value.templateKey === 'string'
    && typeof value.templateName === 'string'
    && typeof value.versionNumber === 'number'
    && typeof value.formSchemaKey === 'string'
    && typeof value.html === 'string'
    && isRecord(value.boundFields)
    && typeof value.generatedAtUtc === 'string';
}
