import { expect } from '@playwright/test';
import {
  adminOrigin,
  loginHostAdminAccessToken,
  loginTenantAdminAccessToken
} from './real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const dataSourcesPath = `${apiBaseUrl}/api/v1/reporting/data-sources`;
const groupsPath = `${apiBaseUrl}/api/v1/reporting/groups`;
const definitionsPath = `${apiBaseUrl}/api/v1/reporting/definitions`;
const queryPortsPath = `${apiBaseUrl}/api/v1/reporting/query-ports`;
const exportTasksPath = `${apiBaseUrl}/api/v1/reporting/export-tasks`;

export const reportingExportFormatExcel = 'excel';

export const reportingDatabaseEngineVersionPortKey = 'reporting.database_engine_version';
export const reportingSchemaInventoryPortKey = 'reporting.schema_inventory';

function authHeaders(clientKind, accessToken) {
  return {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(clientKind),
    'Content-Type': 'application/json'
  };
}

/** Host 上下文分页列出报表数据源（列表项已脱敏）。 */
export async function listReportingDataSourcesViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(`${dataSourcesPath}?page=1&pageSize=20`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 读取报表数据源详情（不回显密码）。 */
export async function getReportingDataSourceViaApi(
  request,
  clientKind,
  dataSourceId,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(`${dataSourcesPath}/${dataSourceId}`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 创建报表数据源（结构化字段，非任意连接串）。 */
export async function createReportingDataSourceViaApi(
  request,
  clientKind,
  body,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(dataSourcesPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** 对已保存数据源执行连接测试。 */
export async function testReportingDataSourceViaApi(
  request,
  clientKind,
  dataSourceId,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(`${dataSourcesPath}/${dataSourceId}/test`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 列表项不得携带明文凭据或连接串字段。 */
export function expectReportingDataSourceListItemMasked(item) {
  expect(item).toBeTruthy();
  expect(typeof item.maskedServerEndpoint).toBe('string');
  expect(typeof item.maskedDatabaseName).toBe('string');
  expect(typeof item.maskedUsername).toBe('string');
  expect(item).not.toHaveProperty('password');
  expect(item).not.toHaveProperty('connectionString');
  expect(item).not.toHaveProperty('passwordProtected');
}

/** 详情不得回显密码。 */
export function expectReportingDataSourceDetailSafe(detail) {
  expect(detail).toBeTruthy();
  expect(typeof detail.hasPassword).toBe('boolean');
  expect(detail).not.toHaveProperty('password');
  expect(detail).not.toHaveProperty('connectionString');
  expect(detail).not.toHaveProperty('passwordProtected');
}

/** 列出静态 Query Port 目录（不含审查 SQL 文本）。 */
export async function listReportingQueryPortsViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(queryPortsPath, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 读取单个 Query Port 元数据。 */
export async function getReportingQueryPortViaApi(
  request,
  clientKind,
  queryPortKey,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(
    `${queryPortsPath}/${encodeURIComponent(queryPortKey)}`,
    { headers: authHeaders(clientKind, token) }
  );
  return { response, accessToken: token };
}

/** 列出报表分组。 */
export async function listReportingGroupsViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(groupsPath, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 创建报表分组。 */
export async function createReportingGroupViaApi(request, clientKind, body, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(groupsPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** 列出报表定义草稿。 */
export async function listReportingDefinitionsViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(definitionsPath, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 创建报表定义草稿。 */
export async function createReportingDefinitionViaApi(
  request,
  clientKind,
  body,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(definitionsPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** 发布报表定义并返回版本。 */
export async function publishReportingDefinitionViaApi(
  request,
  clientKind,
  definitionId,
  body,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(`${definitionsPath}/${definitionId}/publish`, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** 列出已发布版本。 */
export async function listReportingDefinitionVersionsViaApi(
  request,
  clientKind,
  definitionId,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(`${definitionsPath}/${definitionId}/versions`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** Query Port 目录不得暴露 SQL 或任意查询文本。 */
/** 租户上下文分页列出导出任务。 */
export async function listReportingExportTasksViaApi(
  request,
  clientKind,
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.get(`${exportTasksPath}?page=1&pageSize=20`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 创建 Excel 导出任务（需租户上下文与已发布定义）。 */
export async function createReportingExportTaskViaApi(
  request,
  clientKind,
  body,
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.post(exportTasksPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** 下载已完成导出 xlsx（需 download 权限）。 */
export async function downloadReportingExportTaskViaApi(
  request,
  clientKind,
  taskId,
  accessToken = null
) {
  const token = accessToken ?? (await loginTenantAdminAccessToken(request, clientKind));
  const response = await request.get(`${exportTasksPath}/${taskId}/download`, {
    headers: {
      Authorization: `Bearer ${token}`,
      Origin: adminOrigin(clientKind)
    }
  });
  return { response, accessToken: token };
}

/** 对已发布定义执行 Query Port（有界分页；依赖有效数据源与发布版本）。 */
export async function executeReportingDefinitionViaApi(
  request,
  clientKind,
  definitionId,
  body,
  page = 1,
  pageSize = 50,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(
    `${definitionsPath}/${definitionId}/execute?page=${page}&pageSize=${pageSize}`,
    {
      headers: authHeaders(clientKind, token),
      data: body
    }
  );
  return { response, accessToken: token };
}

export function expectReportingQueryPortCatalogSafe(ports) {
  expect(Array.isArray(ports)).toBeTruthy();
  expect(ports.length).toBeGreaterThan(0);
  for (const port of ports) {
    expect(port.queryPortKey).toMatch(/^reporting\./);
    expect(port).not.toHaveProperty('sql');
    expect(port).not.toHaveProperty('sqlText');
    expect(port).not.toHaveProperty('statement');
  }
  const keys = ports.map((port) => port.queryPortKey);
  expect(keys).toContain(reportingDatabaseEngineVersionPortKey);
  expect(keys).toContain(reportingSchemaInventoryPortKey);
}
