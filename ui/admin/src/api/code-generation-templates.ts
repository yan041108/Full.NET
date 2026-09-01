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



/** 分页查询代码生成模板列表，并对筛选词做 trim 规范化。 */
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



/** 读取单个代码生成模板详情。 */
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



/** 创建代码生成模板，并对返回结构做失败关闭校验。 */
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



/** 更新代码生成模板，并对返回结构做失败关闭校验。 */
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



/** 删除代码生成模板。 */
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



/** 校验模板响应结构，避免调用方重复编写同样的失败关闭逻辑。 */
function readTemplate(value: unknown): CodeGenerationTemplateResponse {

  if (!isCodeGenerationTemplateResponse(value)) {

    throw new Error('client.invalid_code_generation_template');

  }



  return value;

}



/** 导出代码生成模板分页、详情与写入模型，供模板列表、编辑页与删除确认流程共享同一契约。 */
export type {

  CodeGenerationTemplatePage,

  CodeGenerationTemplateResponse,

  CreateCodeGenerationTemplateRequest,

  UpdateCodeGenerationTemplateRequest

};


