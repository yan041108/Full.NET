import {
  isCodeGenerationTemplatePage,
  isCodeGenerationTemplateResponse
} from '@fullnet/client-contracts';

const templatesPath = '/api/v1/code-generation/templates';

export function createCodeGenerationTemplatesApi(request) {
  return {
    async list(page = 1, pageSize = 20) {
      const value = await request(
        `${templatesPath}?page=${page}&pageSize=${pageSize}`
      );
      if (!isCodeGenerationTemplatePage(value)) {
        throw new Error('client.invalid_code_generation_template_page');
      }
      return value;
    },
    async get(templateId) {
      return readTemplate(await request(
        `${templatesPath}/${encodeURIComponent(templateId)}`
      ));
    },
    async create(input) {
      return readTemplate(await request(
        templatesPath,
        jsonRequest('POST', input)
      ));
    },
    async update(templateId, input) {
      return readTemplate(await request(
        `${templatesPath}/${encodeURIComponent(templateId)}`,
        jsonRequest('PUT', input)
      ));
    },
    async remove(templateId, version) {
      await request(
        `${templatesPath}/${encodeURIComponent(templateId)}/delete`,
        jsonRequest('POST', { version })
      );
    }
  };
}

function readTemplate(value) {
  if (!isCodeGenerationTemplateResponse(value)) {
    throw new Error('client.invalid_code_generation_template');
  }
  return value;
}

function jsonRequest(method, body) {
  return {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  };
}
