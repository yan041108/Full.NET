import {
  isCodeGenerationPreviewRequest,
  type CodeGenerationPreviewRequest
} from './code-generation-previews.js';

export interface CreateCodeGenerationTemplateRequest {
  name: string;
  description: string | null;
  schema: CodeGenerationPreviewRequest;
}

export interface UpdateCodeGenerationTemplateRequest
  extends CreateCodeGenerationTemplateRequest {
  version: number;
}

export interface DeleteCodeGenerationTemplateRequest {
  version: number;
}

export interface CodeGenerationTemplateResponse {
  id: string;
  name: string;
  description: string | null;
  schema: CodeGenerationPreviewRequest;
  schemaSha256: string;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
}

export interface CodeGenerationTemplatePage {
  items: CodeGenerationTemplateResponse[];
  page: number;
  pageSize: number;
  total: number;
}

const uuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const sha256Pattern = /^[0-9a-f]{64}$/;

export function isCodeGenerationTemplateResponse(
  value: unknown
): value is CodeGenerationTemplateResponse {
  if (!isRecord(value)) {
    return false;
  }

  const hasUpdateAudit = value.updatedAtUtc !== null
    && value.updatedByUserId !== null;
  const hasNoUpdateAudit = value.updatedAtUtc === null
    && value.updatedByUserId === null;
  return isUuid(value.id)
    && isBoundedText(value.name, 1, 128)
    && (value.description === null
      || isBoundedText(value.description, 1, 512))
    && isCodeGenerationPreviewRequest(value.schema)
    && typeof value.schemaSha256 === 'string'
    && sha256Pattern.test(value.schemaSha256)
    && isDateTime(value.createdAtUtc)
    && isUuid(value.createdByUserId)
    && (hasNoUpdateAudit || (
      hasUpdateAudit
      && isDateTime(value.updatedAtUtc)
      && isUuid(value.updatedByUserId)
    ))
    && Number.isSafeInteger(value.version)
    && (value.version as number) >= 1;
}

export function isCodeGenerationTemplatePage(
  value: unknown
): value is CodeGenerationTemplatePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isCodeGenerationTemplateResponse)
    && Number.isSafeInteger(value.page)
    && (value.page as number) >= 1
    && Number.isSafeInteger(value.pageSize)
    && (value.pageSize as number) >= 1
    && (value.pageSize as number) <= 100
    && Number.isSafeInteger(value.total)
    && (value.total as number) >= 0;
}

function isDateTime(value: unknown): value is string {
  return typeof value === 'string'
    && value.length > 0
    && Number.isFinite(Date.parse(value));
}

function isUuid(value: unknown): value is string {
  return typeof value === 'string' && uuidPattern.test(value);
}

function isBoundedText(
  value: unknown,
  minimum: number,
  maximum: number
): value is string {
  return typeof value === 'string'
    && value.trim().length >= minimum
    && value.length <= maximum;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
