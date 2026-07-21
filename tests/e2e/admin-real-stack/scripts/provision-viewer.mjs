import path from 'node:path';
import { pathToFileURL } from 'node:url';

const viewerRoleCode = 'e2e-host-viewer';
const viewerRoleName = 'E2E 受限查看者';
const viewerDisplayName = 'E2E 受限查看者';
const viewerPermissions = [
  'platform.dashboard.read',
  'identity.navigation.read',
  'tenancy.tenants.read',
  'tenancy.tenants.switch'
];

/**
 * 经真实 Host 管理 API 幂等创建受限查看者，场景账号不会进入生产程序集或 Host 配置模型。
 */
export async function provisionViewer(environment = process.env) {
  const apiUrl = (environment.FULLNET_E2E_API_URL ?? 'http://localhost:5149')
    .replace(/\/$/, '');
  const origin = environment.FULLNET_E2E_ADMIN_ORIGIN ?? 'http://localhost:25173';
  const adminUsername = environment.FULLNET_E2E_USERNAME ?? 'admin';
  const adminPassword = environment.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
  const viewerUsername = environment.FULLNET_E2E_VIEWER_USERNAME ?? 'e2e-viewer';
  const viewerPassword = environment.FULLNET_E2E_VIEWER_PASSWORD ?? adminPassword;
  const accessToken = await login(
    apiUrl,
    origin,
    adminUsername,
    adminPassword);
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: origin
  };

  const role = await ensureRole(apiUrl, headers);
  const user = await ensureUser(
    apiUrl,
    headers,
    viewerUsername,
    viewerPassword);
  await ensureUserRole(apiUrl, headers, user.id, role.id);

  return {
    roleId: role.id,
    userId: user.id,
    username: user.username
  };
}

async function login(apiUrl, origin, username, password) {
  const response = await requestJson(`${apiUrl}/api/v1/auth/login`, {
    method: 'POST',
    headers: { Origin: origin },
    body: { username, password }
  });
  if (typeof response.accessToken !== 'string' || response.accessToken.length === 0) {
    throw new Error('Bootstrap 管理员登录响应缺少 accessToken。');
  }

  return response.accessToken;
}

async function ensureRole(apiUrl, headers) {
  const page = await requestJson(
    `${apiUrl}/api/v1/identity/roles?page=1&pageSize=100`,
    { headers });
  const matches = page.items.filter(item => item.code === viewerRoleCode);
  if (matches.length > 1) {
    throw new Error(`角色 ${viewerRoleCode} 存在 ${matches.length} 条重复记录。`);
  }

  let role = matches[0];
  if (!role) {
    role = await requestJson(`${apiUrl}/api/v1/identity/roles`, {
      method: 'POST',
      headers,
      body: { code: viewerRoleCode, name: viewerRoleName }
    });
  } else {
    role = await requestJson(`${apiUrl}/api/v1/identity/roles/${role.id}`, {
      headers
    });
  }

  if (!sameSet(role.permissionCodes, viewerPermissions)) {
    role = await requestJson(
      `${apiUrl}/api/v1/identity/roles/${role.id}/permissions`,
      {
        method: 'PUT',
        headers,
        body: {
          permissionCodes: viewerPermissions,
          version: role.version
        }
      });
  }

  return role;
}

async function ensureUser(apiUrl, headers, username, password) {
  const page = await requestJson(
    `${apiUrl}/api/v1/identity/users?page=1&pageSize=100`,
    { headers });
  const normalizedUsername = username.toUpperCase();
  const matches = page.items.filter(item =>
    item.username.toUpperCase() === normalizedUsername);
  if (matches.length > 1) {
    throw new Error(`用户 ${username} 存在 ${matches.length} 条重复记录。`);
  }

  const existing = matches[0];
  if (existing) {
    if (!existing.isActive) {
      throw new Error(`用户 ${username} 已存在但处于禁用状态。`);
    }

    return existing;
  }

  return requestJson(`${apiUrl}/api/v1/identity/users`, {
    method: 'POST',
    headers,
    body: {
      username,
      displayName: viewerDisplayName,
      password
    }
  });
}

async function ensureUserRole(apiUrl, headers, userId, roleId) {
  const assignment = await requestJson(
    `${apiUrl}/api/v1/identity/users/${userId}/roles`,
    { headers });
  if (sameSet(assignment.roleIds, [roleId])) {
    return assignment;
  }

  return requestJson(`${apiUrl}/api/v1/identity/users/${userId}/roles`, {
    method: 'PUT',
    headers,
    body: {
      roleIds: [roleId],
      version: assignment.version
    }
  });
}

function sameSet(actual, expected) {
  if (!Array.isArray(actual) || actual.length !== expected.length) {
    return false;
  }

  const normalizedActual = [...actual].sort();
  const normalizedExpected = [...expected].sort();
  return normalizedActual.every((value, index) => value === normalizedExpected[index]);
}

async function requestJson(url, options = {}) {
  const headers = {
    ...options.headers
  };
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  const response = await fetch(url, {
    method: options.method ?? 'GET',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body)
  });
  const text = await response.text();
  const body = text.length === 0 ? undefined : JSON.parse(text);
  if (!response.ok) {
    throw new Error(
      `${options.method ?? 'GET'} ${url} 返回 ${response.status}: ${text}`);
  }

  return body;
}

const isDirectExecution = process.argv[1]
  && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;

if (isDirectExecution) {
  const result = await provisionViewer();
  console.log(`Viewer ${result.username} provisioned (${result.userId}).`);
}
