import {

  codeGenerationCreateTemplate,

  codeGenerationDeleteTemplate,

  codeGenerationGetTemplate,

  codeGenerationListTemplates,

  codeGenerationUpdateTemplate,

  isCodeGenerationTemplatePage,

  isCodeGenerationTemplateResponse,

  type CodeGenerationTemplatePage,

  type CodeGenerationTemplateResponse,

  type CreateCodeGenerationTemplateRequest,

  type UpdateCodeGenerationTemplateRequest

} from '@fullnet/client-contracts';

import { http } from './http';



export async function listCodeGenerationTemplates(

  page = 1,

  pageSize = 20,

  filters: {

    name?: string;

    tableName?: string;

  } = {},

  signal?: AbortSignal

): Promise<CodeGenerationTemplatePage> {

  const name = filters.name?.trim();

  const tableName = filters.tableName?.trim();

  const value = await codeGenerationListTemplates(

    http,

    {

      page,

      pageSize,

      ...(name ? { name } : {}),

      ...(tableName ? { tableName } : {})

    },

    signal

  );

  if (!isCodeGenerationTemplatePage(value)) {

    throw new Error('client.invalid_code_generation_template_page');

  }



  return value;

}



export async function getCodeGenerationTemplate(

  templateId: string,

  signal?: AbortSignal

): Promise<CodeGenerationTemplateResponse> {

  const value = await codeGenerationGetTemplate(

    http,

    { templateId },

    signal

  );

  return readTemplate(value);

}



export async function createCodeGenerationTemplate(

  input: CreateCodeGenerationTemplateRequest,

  signal?: AbortSignal

): Promise<CodeGenerationTemplateResponse> {

  const value = await codeGenerationCreateTemplate(
    http,
    { body: input as unknown as Parameters<typeof codeGenerationCreateTemplate>[1]['body'] },
    signal
  );

  return readTemplate(value);

}



export async function updateCodeGenerationTemplate(

  templateId: string,

  input: UpdateCodeGenerationTemplateRequest,

  signal?: AbortSignal

): Promise<CodeGenerationTemplateResponse> {

  const value = await codeGenerationUpdateTemplate(
    http,
    { templateId, body: input as unknown as Parameters<typeof codeGenerationUpdateTemplate>[1]['body'] },
    signal
  );

  return readTemplate(value);

}



export async function deleteCodeGenerationTemplate(

  templateId: string,

  version: number,

  signal?: AbortSignal

): Promise<void> {

  await codeGenerationDeleteTemplate(

    http,

    { templateId, body: { version } },

    signal

  );

}



function readTemplate(value: unknown): CodeGenerationTemplateResponse {

  if (!isCodeGenerationTemplateResponse(value)) {

    throw new Error('client.invalid_code_generation_template');

  }



  return value;

}



export type {

  CodeGenerationTemplatePage,

  CodeGenerationTemplateResponse,

  CreateCodeGenerationTemplateRequest,

  UpdateCodeGenerationTemplateRequest

};


