import { expect } from '@playwright/test';
import {
  adminOrigin,
  loginHostAdminAccessToken,
  loginTenantAdminAccessToken
} from './real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const formSchemasPath = `${apiBaseUrl}/api/v1/printing/form-schemas`;
const templatesPath = `${apiBaseUrl}/api/v1/printing/templates`;

export const printingTenantProfileCardSchemaKey = 'printing.tenant_profile_card';

function authHeaders(clientKind, accessToken, json = true) {
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(clientKind)
  };
  if (json) {
    headers['Content-Type'] = 'application/json';
  }
  return headers;
}

/** 列出固定表单 Schema 目录（Host）。 */
export async function listPrintingFormSchemasViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(formSchemasPath, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 读取单个表单 Schema。 */
export async function getPrintingFormSchemaViaApi(
  request,
  clientKind,
  formSchemaKey,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(
    `${formSchemasPath}/${encodeURIComponent(formSchemaKey)}`,
    { headers: authHeaders(clientKind, token) }
  );
  return { response, accessToken: token };
}

/** 列出打印模板（Host）。 */
export async function listPrintingTemplatesViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(templatesPath, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 预览已发布模板（租户上下文 + 服务端数据绑定）。 */
export async function previewPrintingTemplateViaApi(
  request,
  clientKind,
  templateId,
  body = { versionNumber: null },
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.post(`${templatesPath}/${templateId}/preview`, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** Schema 目录仅暴露固定字段，不接受客户端自定义绑定键。 */
export function expectPrintingFormSchemaCatalog(schemas) {
  expect(Array.isArray(schemas)).toBeTruthy();
  const match = schemas.find((item) => item.formSchemaKey === printingTenantProfileCardSchemaKey);
  expect(match).toBeTruthy();
  const fieldKeys = match.fields.map((field) => field.fieldKey);
  expect(fieldKeys).toContain('tenantName');
  expect(fieldKeys).toContain('printedAtUtc');
  for (const schema of schemas) {
    expect(schema).not.toHaveProperty('layoutHtml');
    expect(schema).not.toHaveProperty('bindingEndpoint');
  }
}
