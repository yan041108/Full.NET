import { expect } from '@playwright/test';
import { adminOrigin, loginTenantAdminAccessToken } from './real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const schemasPath = `${apiBaseUrl}/api/v1/import-export/schemas`;
const tasksPath = `${apiBaseUrl}/api/v1/import-export/tasks`;

export const organizationTenantPositionsSchemaKey = 'organization.tenant_positions';
export const organizationPositionsWorksheetKey = 'positions';

function authHeaders(clientKind, accessToken) {
  return {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(clientKind)
  };
}

/** 租户上下文内列出静态导入 Schema。 */
export async function listImportExportSchemasViaApi(request, clientKind) {
  const accessToken = await loginTenantAdminAccessToken(request, clientKind);
  const response = await request.get(schemasPath, {
    headers: authHeaders(clientKind, accessToken)
  });
  return { response, accessToken };
}

/** 下载指定 Schema 工作表模板 xlsx。 */
export async function downloadImportExportTemplateViaApi(
  request,
  clientKind,
  schemaKey,
  worksheetKey,
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  return request.get(
    `${schemasPath}/${encodeURIComponent(schemaKey)}/worksheets/${encodeURIComponent(worksheetKey)}/template`,
    { headers: authHeaders(clientKind, token) }
  );
}

/** 上传工作簿并同步预校验，创建导入任务。 */
export async function createImportExportTaskViaApi(
  request,
  clientKind,
  schemaKey,
  worksheetKey,
  fileBuffer,
  fileName = 'import.xlsx',
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  return request.post(tasksPath, {
    headers: authHeaders(clientKind, token),
    multipart: {
      schemaKey,
      worksheetKey,
      file: {
        name: fileName,
        mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        buffer: fileBuffer
      }
    }
  });
}

/** 断言租户职位 Schema 存在（清单 65 首选消费者）。 */
export function expectOrganizationPositionsSchema(schemas) {
  const match = schemas.find((item) => item.schemaKey === organizationTenantPositionsSchemaKey);
  expect(match).toBeTruthy();
  expect(match.worksheets.some((ws) => ws.worksheetKey === organizationPositionsWorksheetKey)).toBe(
    true
  );
  return match;
}

/** 读取导入任务详情。 */
export async function getImportExportTaskViaApi(request, clientKind, taskId, accessToken = null) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.get(`${tasksPath}/${taskId}`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 将预校验成功任务排队执行（66：execute）。 */
export async function executeImportExportTaskViaApi(
  request,
  clientKind,
  taskId,
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.post(`${tasksPath}/${taskId}/execute`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 从 execution_partial 检查点恢复（66：resume）。 */
export async function resumeImportExportTaskViaApi(
  request,
  clientKind,
  taskId,
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.post(`${tasksPath}/${taskId}/resume`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 重置失败/部分成功并重新排队（66：retry）。 */
export async function retryImportExportTaskViaApi(
  request,
  clientKind,
  taskId,
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.post(`${tasksPath}/${taskId}/retry`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 下载错误回执 xlsx（66：仅 hasErrorReceipt 任务应 200）。 */
export async function downloadImportExportErrorReceiptViaApi(
  request,
  clientKind,
  taskId,
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.get(`${tasksPath}/${taskId}/error-receipt`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

const executionTerminalStatuses = new Set([
  'execution_succeeded',
  'execution_partial',
  'execution_failed'
]);

const executionInFlightStatuses = new Set(['queued', 'executing']);

/** real-stack 无 RunSynchronously 时 execute 可能停在 queued；有 Worker/同步配置则进入终态。 */
export function expectImportExportExecuteAccepted(task) {
  expect(task).toBeTruthy();
  expect([
    ...executionTerminalStatuses,
    ...executionInFlightStatuses,
    'preview_succeeded'
  ]).toContain(task.statusKey);
}
