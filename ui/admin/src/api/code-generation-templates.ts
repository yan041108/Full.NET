import {
  isCodeGenerationTemplatePage,
  isCodeGenerationTemplateResponse,
  type CodeGenerationTemplatePage,
  type CodeGenerationTemplateResponse,
  type CreateCodeGenerationTemplateRequest,
  type UpdateCodeGenerationTemplateRequest
} from '@fullnet/client-contracts';
import { request } from './http';

const templatesPath = '/api/v1/code-generation/templates';

export async function listCodeGenerationTemplates(
  page = 1,
  pageSize = 20,
  filters: {
    name?: string;
    tableName?: string;
  } = {}
): Promise<CodeGenerationTemplatePage> {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });
  const name = filters.name?.trim();
  const tableName = filters.tableName?.trim();
  if (name) {
    query.set('name', name);
  }
  if (tableName) {
    query.set('tableName', tableName);
  }

  const value = await request<unknown>(`${templatesPath}?${query.toString()}`);
  if (!isCodeGenerationTemplatePage(value)) {
    throw new Error('client.invalid_code_generation_template_page');
  }

  return value;
}

export async function getCodeGenerationTemplate(
  templateId: string
): Promise<CodeGenerationTemplateResponse> {
  return readTemplate(await request<unknown>(
    `${templatesPath}/${encodeURIComponent(templateId)}`
  ));
}

export async function createCodeGenerationTemplate(
  input: CreateCodeGenerationTemplateRequest
): Promise<CodeGenerationTemplateResponse> {
  return readTemplate(await request<unknown>(templatesPath, jsonRequest(
    'POST',
    input
  )));
}

export async function updateCodeGenerationTemplate(
  templateId: string,
  input: UpdateCodeGenerationTemplateRequest
): Promise<CodeGenerationTemplateResponse> {
  return readTemplate(await request<unknown>(
    `${templatesPath}/${encodeURIComponent(templateId)}`,
    jsonRequest('PUT', input)
  ));
}

export async function deleteCodeGenerationTemplate(
  templateId: string,
  version: number
): Promise<void> {
  await request<unknown>(
    `${templatesPath}/${encodeURIComponent(templateId)}/delete`,
    jsonRequest('POST', { version })
  );
}

function readTemplate(value: unknown): CodeGenerationTemplateResponse {
  if (!isCodeGenerationTemplateResponse(value)) {
    throw new Error('client.invalid_code_generation_template');
  }

  return value;
}

function jsonRequest(method: string, body: unknown): RequestInit {
  return {
    method,
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  };
}
